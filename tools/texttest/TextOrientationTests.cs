using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// Text put on a 3D shape reads from the camera, whichever face it lands on and however the face is turned in
// the shape's texture. An "L" is stamped on a cube seen from several sides; on the screen its bar must stand
// upright on the left and its foot lie at the bottom right. Needs an interactive desktop with OpenGL. Pictures go
// to SHOTS_OUT (default: a folder in the temp directory).
//   tools\paneltest\run.ps1 -Source tools\texttest\TextOrientationTests.cs
class TextOrientationTests
{
    static readonly string Out = Environment.GetEnvironmentVariable("SHOTS_OUT") ?? Path.Combine(Path.GetTempPath(), "aplat-tests", "text");
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F).First(x => x.Name == n && x.GetParameters().Length == a.Length);
        try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; }
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static MainForm form; static SceneView V; static Control GL;
    static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static Aplat.IO.UndoHistory Hist { get { return (Aplat.IO.UndoHistory)Get(form, "_history"); } }
    static void Mouse(uint flags, Point p)
    {
        var s = GL.PointToScreen(p); var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        int ax = (int)((s.X * 65535L + b.Width / 2) / (b.Width - 1)), ay = (int)((s.Y * 65535L + b.Height / 2) / (b.Height - 1));
        mouse_event(0x8001 | flags, ax, ay, 0, UIntPtr.Zero); Pump(14);
    }
    static void Click(Point p) { Mouse(0, p); Mouse(0x0002, p); Mouse(0x0004, p); Pump(120); }
    static void Pump(int ms = 30) { for (int i = 0; i < ms / 10; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); } }

    [STAThread]
    static int Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(Out);
        Loc.Set(Lang.English); Aplat.UI.Theme.SetMode(Aplat.UI.ThemeMode.Light);
        form = new MainForm();
        try { Run(); }
        catch (Exception e) { Check(false, "threw " + e); }
        finally { Scn.IsModified = false; form.Close(); form.Dispose(); }
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }

    static Bitmap Grab()
    {
        form.TopMost = true; form.Activate(); Pump(300);
        var r = GL.RectangleToScreen(GL.ClientRectangle);
        var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        form.TopMost = false;
        return bmp;
    }

    static bool IsInk(Color c) { return c.B > c.R + 25 && c.B > c.G + 25; }

    static void Run()
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.Size = new Size(1500, 950);
        form.Show(); Pump(600);
        V = (SceneView)Get(form, "sceneView"); GL = (Control)Get(V, "_gl");
        Call(form, "ApplyView", ViewMode.Perspective); Pump(200);
        Scn.Objects.RemoveAll(o => o.PrimitiveType == "Canvas");
        Scn.ShowGrid = false;

        // pitch, yaw: the cube is seen from the front, the right, the back, the left, above and below
        var views = new[] { new[] { 18f, -32f }, new[] { 18f, 58f }, new[] { 18f, 148f }, new[] { 18f, 238f }, new[] { 65f, 10f }, new[] { -70f, 0f } };
        int n = 0;
        foreach (var vw in views)
        {
            n++;
            Scn.Objects.RemoveAll(o => o.PrimitiveType != "Canvas");
            Call(form, "AddPrimitive", PrimitiveKind.Cube); Pump(100);
            var cube = Scn.Objects.Last(); cube.Position = new Vec3(0, 0.5f, 0); cube.Material.Color = Color.FromArgb(235, 150, 60);
            V.Camera.Target = new Vec3(0, 0.5f, 0); V.Camera.AngleX = vw[0]; V.Camera.AngleY = vw[1]; V.Camera.Distance = 4.6f;
            V.Refresh3D(); Pump(300);

            Point pt = Point.Round(V.Camera.Project(SceneGeometry.TransformVertex(Vec3.Zero, cube), GL.Width, GL.Height));
            var args = new object[] { pt, null, null };
            bool hit = (bool)typeof(SceneView).GetMethods(F).First(m => m.Name == "PickPaintTarget" && m.GetParameters().Length == 3).Invoke(V, args);
            Check(hit && args[1] == cube, "view " + n + ": the cube is under the point " + pt);
            if (!hit) continue;
            PointF uv = (PointF)args[2];
            V.SetTextFormat("Segoe UI", 80f, FontStyle.Bold, StringAlignment.Near);
            V.BrushColor = Color.Blue;
            int undo0 = Hist.UndoCount;
            Call(V, "BeginSurfaceText", cube, pt); Pump(100);
            Check(V.HasLiveItem && V.IsEditingText, "view " + n + ": a live text opens on the face");
            if (!V.HasLiveItem) continue;
            V.EditLive(i => i.Text = "L"); Pump(100);
            V.Refresh3D(); Pump(400);
            using (var shot = Grab()) shot.Save(Path.Combine(Out, "view" + n + ".png"), ImageFormat.Png);   // with the frame
            V.CommitLive(); Pump(300);
            Check(!V.HasLiveItem && Hist.UndoCount == undo0 + 1, "view " + n + ": committing writes the text as one undo step");
            using (var bmp = Grab())
            {
                var area = Rectangle.Intersect(new Rectangle(pt.X - 220, pt.Y - 220, 440, 440), new Rectangle(0, 0, bmp.Width, bmp.Height));
                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                for (int y = area.Top; y < area.Bottom; y++) for (int x = area.Left; x < area.Right; x++)
                    if (IsInk(bmp.GetPixel(x, y))) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
                if (x1 < 0) { Check(false, "view " + n + ": the text is visible"); continue; }
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                double top = 0, bot = 0; int nt = 0, nb = 0;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    if (!IsInk(bmp.GetPixel(x, y))) continue;
                    if (y < y0 + h / 4) { top += x; nt++; } else if (y > y1 - h / 4) { bot += x; nb++; }
                }
                double dx = (nt > 0 && nb > 0) ? bot / nb - top / nt : 0;
                Check(h * 2 >= w, "view " + n + ": the text stands upright (" + w + " x " + h + ")");
                Check(dx > w * 0.15, "view " + n + ": the foot of the L is at the bottom right (shift " + dx.ToString("0.0") + ")");
            }
            Call(form, "Undo"); Pump(200);
            using (var bmp = Grab())
            {
                int ink = 0; var area = Rectangle.Intersect(new Rectangle(pt.X - 220, pt.Y - 220, 440, 440), new Rectangle(0, 0, bmp.Width, bmp.Height));
                for (int y = area.Top; y < area.Bottom; y += 2) for (int x = area.Left; x < area.Right; x += 2) if (IsInk(bmp.GetPixel(x, y))) ink++;
                Check(ink == 0, "view " + n + ": undo takes the text away");
            }
            Call(V, "BeginSurfaceText", Scn.Objects.Last(), pt); Pump(100);
            V.EditLive(i => i.Text = "x"); Pump(60);
            V.CancelLive(); Pump(100);
            Check(!V.HasLiveItem && Hist.UndoCount == undo0, "view " + n + ": Esc removes the text and its undo step");
        }

        // ── the same with the real mouse and keyboard ──
        Scn.Objects.RemoveAll(o => o.PrimitiveType != "Canvas");
        Call(form, "AddPrimitive", PrimitiveKind.Cube); Pump(100);
        var c2 = Scn.Objects.Last(); c2.Position = new Vec3(0, 0.5f, 0); c2.Material.Color = Color.FromArgb(235, 150, 60);
        V.Camera.Target = new Vec3(0, 0.5f, 0); V.Camera.AngleX = 18f; V.Camera.AngleY = -32f; V.Camera.Distance = 4.6f;
        Call(form, "SetTool", ToolMode.Paint); V.PaintKind = BrushKind.Text2D; V.Refresh3D(); Pump(300);
        Point at = Point.Round(V.Camera.Project(SceneGeometry.TransformVertex(Vec3.Zero, c2), GL.Width, GL.Height));
        int u0 = Hist.UndoCount;
        Click(at);
        Check(V.HasLiveItem && V.IsEditingText, "a click with the Text tool on a shape starts typing there");
        SendKeys.SendWait("Hi"); Pump(200);
        Check(V.HasLiveItem && V.Live.Text == "Hi", "typed letters go into the text (" + (V.Live == null ? "none" : V.Live.Text) + ")");
        SendKeys.SendWait("{BACKSPACE}"); Pump(100);
        Check(V.Live != null && V.Live.Text == "H", "Backspace edits it");
        Click(new Point(GL.Width - 250, 250));   // a click elsewhere ends it
        Check(!V.HasLiveItem && Hist.UndoCount == u0 + 1, "a click elsewhere commits the text as one undo step");
        Call(form, "Undo"); Pump(100);
        Check(Hist.UndoCount == u0, "Ctrl+Z takes it back");
    }
}
