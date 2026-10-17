using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// Drives the real window the way a user does: synthetic mouse / key events into the viewport.
// Needs an interactive desktop (OpenGL): see tools/paneltest/run.ps1 and tools/coverage/run.ps1.
class UiTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length)
             ?? o.GetType().GetMethods(F).FirstOrDefault(x => x.Name == n && x.GetParameters().Length == a.Length);
        if (m == null) throw new Exception("no method " + n + "/" + a.Length);
        try { return m.Invoke(o, a); }
        catch (TargetInvocationException e)
        {
            if (e.InnerException is InvalidOperationException && e.InnerException.Message.Contains("ServiceNotification")) { dialogs++; return null; }
            throw e.InnerException;
        }
    }
    static int dialogs;
    static bool NoThrow(Action a, string msg)
    {
        try { a(); Check(true, msg); return true; }
        catch (Exception e) { Check(false, msg + " threw " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace.Split('\n').FirstOrDefault()); return false; }
    }

    // Dialog watchdog (a MessageBox would otherwise wait for a click forever).
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    static void StartDialogWatchdog()
    {
        new System.Threading.Thread(() =>
        {
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            while (true)
            {
                System.Threading.Thread.Sleep(150);
                EnumWindows((h, l) =>
                {
                    uint pid; GetWindowThreadProcessId(h, out pid);
                    var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                    if (pid == me && cls.ToString() == "#32770" && IsWindowVisible(h))
                    {
                        IntPtr first = IntPtr.Zero;
                        EnumChildWindows(h, (c, l2) => { var cc = new StringBuilder(64); GetClassName(c, cc, 64); if (cc.ToString() == "Button" && first == IntPtr.Zero) first = c; return true; }, IntPtr.Zero);
                        if (first != IntPtr.Zero) { dialogs++; SendMessage(first, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }) { IsBackground = true }.Start();
    }

    static MainForm form; static SceneView V; static Control GL;
    static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static SceneObject Canvas { get { return Scn.Objects.First(o => o.PrimitiveType == "Canvas"); } }
    static int Ops { get { var ink = Canvas.Material.Ink; return ink == null ? 0 : ink.Ops.Count; } }

    // The viewport reads Control.MouseButtons (the physical state), so synthetic event objects alone
    // are not enough: the pointer is moved and the buttons are pressed through the OS input queue.
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    static void Mouse(string handler, MouseButtons b, Point p, int delta = 0)
    {
        var s = GL.PointToScreen(p);
        SetCursorPos(s.X, s.Y);
        uint down = b == MouseButtons.Right ? 0x0008u : b == MouseButtons.Middle ? 0x0020u : 0x0002u;
        uint up = down << 1;
        if (handler == "Gl_MouseDown") mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        else if (handler == "Gl_MouseUp") mouse_event(up, 0, 0, 0, UIntPtr.Zero);
        else if (handler == "Gl_MouseWheel") mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero);
        else mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero);   // a move: the cursor is already there
        for (int i = 0; i < 4; i++) { System.Threading.Thread.Sleep(8); Application.DoEvents(); }
    }
    static void Key(Keys k, Keys mods = Keys.None) { Call(V, "Gl_KeyDown", GL, new KeyEventArgs(k | mods)); Call(V, "Gl_KeyUp", GL, new KeyEventArgs(k | mods)); Application.DoEvents(); }
    static void Type(string s) { foreach (char c in s) Call(V, "Gl_KeyPress", GL, new KeyPressEventArgs(c)); Application.DoEvents(); }

    // A point on the canvas (u, v in 0..1) in viewport pixels, through the real camera.
    static Point OnCanvas(float u, float v)
    {
        var c = Canvas; var xf = SceneGeometry.Xform.For(c); var vt = c.Mesh.Vertices;
        Vec3 bl = xf.Apply(vt[0]), br = xf.Apply(vt[1]), tr = xf.Apply(vt[2]), tl = xf.Apply(vt[3]);
        var p = V.Camera.Project(tl + (tr - tl) * u + (bl - tl) * v, GL.Width, GL.Height);
        return Point.Round(p);
    }
    static int AlphaAt(float u, float v)
    {
        var layer = Canvas.Material.PaintLayer; if (layer == null) return 0;
        // The stroke is smoothed, so look at a thin band around the point, not one pixel.
        int x = Math.Max(0, Math.Min(layer.Width - 1, (int)(u * layer.Width))), y = Math.Max(0, Math.Min(layer.Height - 1, (int)(v * layer.Height)));
        int best = 0;
        for (int dy = -90; dy <= 90; dy += 3) { int yy = Math.Max(0, Math.Min(layer.Height - 1, y + dy)); best = Math.Max(best, layer.GetPixel(x, yy).A); }
        return best;
    }
    // All alpha in a horizontal strip of the layer: it falls when something is erased there.
    static long StripAlpha(float v)
    {
        var layer = Canvas.Material.PaintLayer; long sum = 0;
        int y0 = Math.Max(0, (int)(v * layer.Height) - 90), y1 = Math.Min(layer.Height - 1, (int)(v * layer.Height) + 90);
        for (int y = y0; y <= y1; y += 3) for (int x = 0; x < layer.Width; x += 3) sum += layer.GetPixel(x, y).A;
        return sum;
    }
    static void Drag(MouseButtons b, params Point[] path)
    {
        Mouse("Gl_MouseDown", b, path[0]);
        for (int i = 1; i < path.Length; i++) Mouse("Gl_MouseMove", b, path[i]);
        Mouse("Gl_MouseUp", b, path[path.Length - 1]);
    }
    static void StrokeAcross(float v) { Drag(MouseButtons.Left, OnCanvas(0.15f, v), OnCanvas(0.3f, v + 0.02f), OnCanvas(0.5f, v), OnCanvas(0.7f, v + 0.02f), OnCanvas(0.85f, v)); }
    static void Brush(BrushKind k) { Call(form, "SetTool", ToolMode.Paint); Call(form, "SetBrush", k, true); }

    [STAThread]
    static int Main()
    {
        StartDialogWatchdog();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        form = new MainForm();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.ClientSize = new Size(1400, 900);
        form.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(500); Application.DoEvents();
        V = (SceneView)Get(form, "sceneView"); GL = (Control)Get(V, "_gl");
        Check(GL.Width > 300 && GL.Height > 300, "the viewport has a real size: " + GL.Width + "x" + GL.Height);
        Call(form, "ApplyView", ViewMode.Front); Application.DoEvents();

        // ── 2D painting with every brush ──
        float row = 0.12f;
        foreach (var k in new[] { BrushKind.Brush, BrushKind.Pencil, BrushKind.Pen, BrushKind.Marker })
        {
            Brush(k);
            int before = Ops;
            float v = row; row += 0.1f;
            NoThrow(() => StrokeAcross(v), "stroke with " + k);
            Check(Ops == before + 1, k + ": one stroke is one object in the journal (" + before + " -> " + Ops + ")");
            if (AlphaAt(0.5f, v) == 0)
            {
                var layer = Canvas.Material.PaintLayer; var op = Canvas.Material.Ink.Ops[Canvas.Material.Ink.Ops.Count - 1];
                int mx = 0, my = -1; for (int y = 0; y < layer.Height; y += 4) for (int x = 0; x < layer.Width; x += 4) { int a = layer.GetPixel(x, y).A; if (a > mx) { mx = a; my = y; } }
                Console.WriteLine("DIAG layer " + layer.Size + " op " + op.GetType().Name + " bounds " + op.Bounds + " maxAlpha " + mx + " atY " + my + " expectedY " + (int)(v * layer.Height) + " screen " + OnCanvas(0.5f, v));
            }
            Check(AlphaAt(0.5f, v) > 0, k + ": the canvas has ink where the stroke passed");
        }
        // undo / redo of strokes
        int opsNow = Ops;
        Call(form, "Undo"); Check(Ops == opsNow - 1, "undo takes the last stroke back"); Call(form, "Redo"); Check(Ops == opsNow, "redo puts it back");

        // fill
        Brush(BrushKind.Fill); Call(form, "SetBrushSwatch", Color.SeaGreen); V.BrushColor = Color.SeaGreen;
        int b0 = Ops;
        NoThrow(() => Drag(MouseButtons.Left, OnCanvas(0.9f, 0.9f)), "fill an empty area");
        Check(Ops == b0 + 1 && AlphaAt(0.9f, 0.9f) > 0, "fill paints the area");

        // eyedropper
        Brush(BrushKind.Eyedropper);
        Color picked = Color.Empty; EventHandler<Color> h = (s, c) => picked = c; V.BrushColorPicked += h;
        NoThrow(() => Drag(MouseButtons.Left, OnCanvas(0.9f, 0.9f)), "eyedropper click");
        V.BrushColorPicked -= h;
        Check(picked.G > picked.R, "the eyedropper picked the green: " + picked);

        // precise eraser removes pixels
        Brush(BrushKind.Eraser);
        long a0 = StripAlpha(0.12f);
        NoThrow(() => StrokeAcross(0.12f), "precise eraser stroke");
        Check(a0 > 0 && StripAlpha(0.12f) < a0, "precise eraser lowers the alpha where it passed");

        // object eraser removes a whole object
        Brush(BrushKind.ObjectEraser);
        int e0 = Ops;
        NoThrow(() => Drag(MouseButtons.Left, OnCanvas(0.5f, 0.22f)), "object eraser click on the second stroke");
        Check(Ops == e0 - 1, "object eraser removes exactly one object (" + e0 + " -> " + Ops + ")");
        Call(form, "Undo"); Check(Ops == e0, "...and undo brings it back");

        // shapes
        foreach (var k in new[] { BrushKind.ShapeLine, BrushKind.ShapeRect, BrushKind.ShapeEllipse, BrushKind.ShapeTriangle })
        {
            Brush(k);
            int s0 = Ops;
            NoThrow(() => Drag(MouseButtons.Left, OnCanvas(0.55f, 0.55f), OnCanvas(0.7f, 0.65f), OnCanvas(0.8f, 0.8f)), "draw " + k);
            V.CommitLive(); Application.DoEvents();
            Check(Ops == s0 + 1, k + ": a committed shape is one object (" + s0 + " -> " + Ops + ")");
            NoThrow(() => Key(Keys.Z, Keys.Control), "ctrl+z after " + k);
        }
        // live shape: nudge with arrows, delete with Esc
        Brush(BrushKind.ShapeRect);
        NoThrow(() => { Drag(MouseButtons.Left, OnCanvas(0.1f, 0.6f), OnCanvas(0.3f, 0.8f)); Key(Keys.Left); Key(Keys.Up, Keys.Shift); Key(Keys.Delete); }, "live rectangle: arrows and delete");

        // text
        Brush(BrushKind.Text2D);
        int t0 = Ops;
        NoThrow(() =>
        {
            Drag(MouseButtons.Left, OnCanvas(0.1f, 0.5f));
            Type("Hi there");
            Key(Keys.Left, Keys.Shift); Key(Keys.Back); Key(Keys.Home); Key(Keys.End); Key(Keys.A, Keys.Control); Key(Keys.B, Keys.Control);
            Key(Keys.Return, Keys.Control);
        }, "type text on the canvas");
        Drag(MouseButtons.Left, OnCanvas(0.9f, 0.05f));
        Key(Keys.Return);
        Check(Ops >= t0, "text becomes a journal object or is dropped when empty (" + t0 + " -> " + Ops + ")");

        // select / move a region
        Brush(BrushKind.Select2D);
        NoThrow(() => { Drag(MouseButtons.Left, OnCanvas(0.1f, 0.05f), OnCanvas(0.6f, 0.3f)); Drag(MouseButtons.Left, OnCanvas(0.3f, 0.15f), OnCanvas(0.4f, 0.2f)); Key(Keys.Escape); }, "marquee select, move, Esc");

        // lasso
        Brush(BrushKind.Lasso);
        NoThrow(() => { Drag(MouseButtons.Left, OnCanvas(0.05f, 0.05f), OnCanvas(0.95f, 0.05f), OnCanvas(0.95f, 0.45f), OnCanvas(0.05f, 0.45f), OnCanvas(0.05f, 0.06f)); Key(Keys.Escape); }, "lasso loop, Esc");

        // ruler
        NoThrow(() => { Key(Keys.R); Brush(BrushKind.Brush); StrokeAcross(0.7f); Key(Keys.R); }, "ruler on, stroke, ruler off");

        // wheel / hand
        NoThrow(() => { Call(form, "SetTool", ToolMode.Hand); Drag(MouseButtons.Left, OnCanvas(0.5f, 0.5f), OnCanvas(0.55f, 0.55f)); Mouse("Gl_MouseWheel", MouseButtons.None, OnCanvas(0.5f, 0.5f), 120); Mouse("Gl_MouseWheel", MouseButtons.None, OnCanvas(0.5f, 0.5f), -240); }, "hand tool and wheel zoom");

        // ── 3D ──
        Call(form, "ApplyView", ViewMode.Perspective); Application.DoEvents();
        Call(form, "AddPrimitive", PrimitiveKind.Cube);
        Call(form, "AddPrimitive", PrimitiveKind.Sphere);
        float ang0 = V.Camera.AngleY;
        NoThrow(() => Drag(MouseButtons.Right, new Point(300, 300), new Point(360, 320), new Point(420, 330)), "orbit with the right button");
        Check(Math.Abs(V.Camera.AngleY - ang0) > 0.1f, "the orbit changed the camera angle (" + ang0 + " -> " + V.Camera.AngleY + ")");
        var t = V.Camera.Target;
        NoThrow(() => Drag(MouseButtons.Middle, new Point(300, 300), new Point(340, 330)), "pan with the middle button");
        float dist0 = V.Camera.Distance;
        NoThrow(() => Mouse("Gl_MouseWheel", MouseButtons.None, new Point(500, 400), 240), "wheel zoom in 3D");
        Check(V.Camera.Distance == dist0, "the plain wheel in 3-D changes fly speed, not the camera distance");
        foreach (ToolMode m in new[] { ToolMode.Select, ToolMode.Move, ToolMode.Rotate, ToolMode.Scale })
        {
            Call(form, "SetTool", m);
            var obj = Scn.Objects.Last(o => o.PrimitiveType != "Canvas");
            V.SetSelection(new List<SceneObject> { obj }); Application.DoEvents();
            NoThrow(() =>
            {
                var c = V.Camera.Project(SceneGeometry.Center(obj), GL.Width, GL.Height);
                var p0 = Point.Round(c);
                Mouse("Gl_MouseMove", MouseButtons.None, p0);
                Drag(MouseButtons.Left, p0, new Point(p0.X + 25, p0.Y), new Point(p0.X + 50, p0.Y - 20));
                Mouse("Gl_MouseMove", MouseButtons.None, new Point(p0.X + 120, p0.Y + 120));
            }, "drag with the " + m + " tool");
        }
        Call(form, "SetTool", ToolMode.Select);
        NoThrow(() => { Drag(MouseButtons.Left, new Point(50, 400)); Key(Keys.Left); Key(Keys.Right, Keys.Shift); Key(Keys.Up); Key(Keys.Down); }, "click empty space then arrow keys");
        foreach (var k in new[] { Keys.W, Keys.A, Keys.S, Keys.D, Keys.Q, Keys.E, Keys.F, Keys.Delete, Keys.Escape })
            NoThrow(() => { Key(k); Call(V, "FlyTick", null, EventArgs.Empty); }, "key " + k);
        foreach (var mode in new[] { ViewMode.Top, ViewMode.Front, ViewMode.Side, ViewMode.Perspective })
            NoThrow(() => { V.SetViewMode(mode); V.FocusAll(); Application.DoEvents(); }, "view " + mode + " and focus all");
        NoThrow(() => { V.ShowWireframe = true; V.ShowVertices = true; V.OutlineSelection = true; V.ShowGrid = true; V.AutoRotate = true; Call(V, "SpinTick", 0.1f, false); V.AutoRotate = false; Application.DoEvents(); }, "wireframe, vertices, outline, grid, auto-rotate");
        Bitmap frame = null;
        NoThrow(() => frame = V.CaptureFrame(), "capture a frame of the viewport");
        Check(frame != null && frame.Width == GL.Width, "the captured frame has the viewport's size");
        int turns = 0;
        NoThrow(() => V.CaptureTurntable(4, (i, bmp) => { turns++; bmp.Dispose(); }), "capture a turntable of 4 frames");
        Check(turns == 4, "the turntable delivered 4 frames");

        // right-click context menu event, brush wheel
        NoThrow(() => { V.ContextMenuRequested += p => { }; Mouse("Gl_MouseDown", MouseButtons.Right, new Point(200, 200)); Mouse("Gl_MouseUp", MouseButtons.Right, new Point(200, 200)); }, "right click without moving asks for a context menu");

        form.Close();
        Console.WriteLine("(dialogs closed by the watchdog: " + dialogs + ")");
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
