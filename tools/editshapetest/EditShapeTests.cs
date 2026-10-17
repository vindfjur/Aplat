using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;
using Aplat.IO;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// The "Edit shape" mode in the real window, driven with real mouse and keyboard input: picking a point,
// an edge and a face, free and axis-constrained drags, Shift (precise) and Ctrl (snap), one undo step per
// drag, locked objects, a rotated and non-uniformly scaled object, save and load. Needs an interactive
// desktop with OpenGL. Writes a picture to edit-shape.png in SHOTS_OUT (default: a folder in the temp directory).
//   tools\paneltest\run.ps1 -Source tools\editshapetest\EditShapeTests.cs
class EditShapeTests
{
    static readonly string Out = Environment.GetEnvironmentVariable("SHOTS_OUT") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aplat-tests", "shape");
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F).First(x => x.Name == n && x.GetParameters().Length == a.Length);
        try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; }
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static MainForm form; static SceneView V; static Control GL;
    static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static UndoHistory Hist { get { return (UndoHistory)Get(form, "_history"); } }
    static SceneObject Obj(string name) { return Scn.Objects.First(o => o.Name == name); }

    static void Pump(int ms = 30) { for (int i = 0; i < ms / 10; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); } }
    // Absolute input events: the move and the button arrive in order, as from a real mouse
    // (SetCursorPos alone posts its move late, after a button event that follows it).
    static void Mouse(uint flags, Point p)
    {
        var s = GL.PointToScreen(p); var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        int ax = (int)((s.X * 65535L + b.Width / 2) / (b.Width - 1)), ay = (int)((s.Y * 65535L + b.Height / 2) / (b.Height - 1));
        mouse_event(0x8001 | flags, ax, ay, 0, UIntPtr.Zero); Pump(14);
    }
    static void Key(byte vk, bool down) { keybd_event(vk, 0, down ? 0u : 2u, UIntPtr.Zero); Pump(40); }

    static Point Scr(SceneObject o, Vec3 local) { return Point.Round(V.Camera.Project(SceneGeometry.TransformVertex(local, o), GL.Width, GL.Height)); }
    static Point ScreenWorld(Vec3 world) { return Point.Round(V.Camera.Project(world, GL.Width, GL.Height)); }

    // A left drag from a to b in small steps; `hold` keys stay down during the drag.
    static void Drag(Point a, Point b, params byte[] hold)
    {
        foreach (var k in hold) Key(k, true);
        Mouse(0, a); Mouse(0x0002, a);
        for (int i = 1; i <= 12; i++) Mouse(0, new Point(a.X + (b.X - a.X) * i / 12, a.Y + (b.Y - a.Y) * i / 12));
        Mouse(0x0004, b);
        foreach (var k in hold) Key(k, false);
        Pump(300);   // the VM draws on the CPU: let the queued moves finish
    }
    static void Click(Point p) { Mouse(0, p); Mouse(0x0002, p); Mouse(0x0004, p); Pump(60); }

    static void Select(SceneObject o) { Call(form, "Select", o); Pump(60); }
    static void SetMode(ToolMode m) { Call(form, "SetTool", m); Pump(60); }
    static bool Near(float a, float b, float e = 1e-4f) { return Math.Abs(a - b) <= e; }
    static bool Same(Mesh a, Mesh b) { return a.Vertices.Count == b.Vertices.Count && a.Vertices.Zip(b.Vertices, (x, y) => (x - y).Length < 1e-6f).All(t => t); }
    static int Changed(Mesh a, Mesh b) { int n = 0; for (int i = 0; i < a.Vertices.Count; i++) if ((a.Vertices[i] - b.Vertices[i]).Length > 1e-6f) n++; return n; }

    static Rectangle Frame()
    {
        RECT r; DwmGetWindowAttribute(form.Handle, 9, out r, Marshal.SizeOf(typeof(RECT)));
        return Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
    }

    static void Shot(string name)
    {
        form.TopMost = true; form.Activate(); Pump(250);
        Mouse(0, new Point(GL.Width - 40, GL.Height - 40)); Pump(250);
        var r = Frame();
        using (var bmp = new Bitmap(r.Width, r.Height))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
        }
        form.TopMost = false;
        Console.WriteLine("shot " + name + " " + r.Width + "x" + r.Height);
    }

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

    static void Run()
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.Size = new Size(1500, 950);
        form.Show(); Pump(600);
        V = (SceneView)Get(form, "sceneView"); GL = (Control)Get(V, "_gl");
        Call(form, "ApplyView", ViewMode.Perspective); Pump(200);
        Scn.Objects.RemoveAll(o => o.PrimitiveType == "Canvas");
        Scn.ShowGrid = true; Scn.GridExtent = 4f;

        // ── the mode in the toolbar, the palette and the hotkey ──
        var tb = (ToolStripButton)Get(form, "tbEditShape");
        Check(tb != null && tb.Text == "Shape" && tb.ToolTipText.Contains("(V)"), "the toolbar has an English 'Shape' button with the V hint");
        tb.PerformClick(); Pump(60);
        Check(V.CurrentTool == ToolMode.EditShape && tb.Checked && !((ToolStripButton)Get(form, "tbMove")).Checked, "the button switches the mode on and the others off");
        SetMode(ToolMode.Move);
        var msg = new Message();
        var pck = typeof(MainForm).GetMethod("ProcessCmdKey", F);
        V.Focus(); Pump(60);
        pck.Invoke(form, new object[] { msg, Keys.V }); Pump(60);
        Check(V.CurrentTool == ToolMode.EditShape, "the V key switches the mode on");
        var cmds = (System.Collections.IEnumerable)Call(form, "BuildCommands");
        bool inPalette = false; foreach (object c in cmds) { var t = (string)c.GetType().GetField("Title").GetValue(c); if (t == "Shape") inPalette = true; }
        Check(inPalette, "the command palette lists 'Shape'");
        SetMode(ToolMode.Select);

        // ── a cube: pick and drag a corner, free ──
        Call(form, "AddPrimitive", PrimitiveKind.Cube); Pump(100);
        var cube = Scn.Objects.Last(); cube.Name = "Cube";
        cube.Position = new Vec3(0, 0.5f, 0);
        V.Camera.Target = new Vec3(0, 0.5f, 0); V.Camera.AngleX = 22; V.Camera.AngleY = -32; V.Camera.Distance = 4.6f;
        Select(cube); SetMode(ToolMode.EditShape); V.Refresh3D(); Pump(300);
        int undo0 = Hist.UndoCount;
        var original = cube.Mesh.Clone();

        // the corner nearest the viewer: the one with the largest camera-space z
        int best = 0; float bz = float.MinValue;
        for (int i = 0; i < cube.Mesh.Vertices.Count; i++) { float z = V.Camera.ToCameraSpace(SceneGeometry.TransformVertex(cube.Mesh.Vertices[i], cube)).Z; if (z > bz) { bz = z; best = i; } }
        Point corner = Scr(cube, cube.Mesh.Vertices[best]);
        Click(corner);
        Check(Hist.UndoCount == undo0 && Same(cube.Mesh, original), "a plain click picks without changing the mesh or the undo history");
        Vec3 v0 = cube.Mesh.Vertices[best];
        Point target = new Point(corner.X + 70, corner.Y - 50);
        Drag(corner, target);
        cube = Obj("Cube");
        Check(Changed(cube.Mesh, original) == 1 && cube.Mesh.Faces.Count == original.Faces.Count, "dragging a corner moves that one vertex only; the topology is intact");
        Point after = Scr(cube, cube.Mesh.Vertices[best]);
        Check(Math.Abs(after.X - target.X) <= 3 && Math.Abs(after.Y - target.Y) <= 3, "the corner follows the pointer (" + after + " vs " + target + ")");
        Check(Hist.UndoCount == undo0 + 1, "the whole drag is one undo step");

        Call(form, "Undo"); Pump(100);
        cube = Obj("Cube");
        Check(Same(cube.Mesh, original), "Undo restores the cube");
        Call(form, "Redo"); Pump(100);
        cube = Obj("Cube");
        Check(Changed(cube.Mesh, original) == 1, "Redo brings the edit back");
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube); V.Refresh3D(); Pump(200);

        // ── axis handle: the picked corner moves along X only ──
        Click(Scr(cube, cube.Mesh.Vertices[best]));
        Vec3 ctrW = SceneGeometry.TransformVertex(cube.Mesh.Vertices[best], cube);
        Point tipX = ScreenWorld(ctrW + new Vec3(0.65f, 0, 0));
        Vec3 beforeAxis = cube.Mesh.Vertices[best];
        Drag(tipX, new Point(tipX.X + 60, tipX.Y));
        cube = Obj("Cube");
        Vec3 d = cube.Mesh.Vertices[best] - beforeAxis;
        Check(Math.Abs(d.X) > 0.05f && Near(d.Y, 0, 1e-5f) && Near(d.Z, 0, 1e-5f), "dragging the X handle moves along X only (" + d + ")");
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube);

        // ── Shift: precise; Ctrl: snap ──
        Click(Scr(cube, cube.Mesh.Vertices[best]));
        Point c1 = Scr(cube, cube.Mesh.Vertices[best]);
        Vec3 pre = cube.Mesh.Vertices[best];
        Drag(c1, new Point(c1.X + 80, c1.Y));
        float freeLen = (Obj("Cube").Mesh.Vertices[best] - pre).Length;
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube);
        Click(Scr(cube, cube.Mesh.Vertices[best]));
        Drag(c1, new Point(c1.X + 80, c1.Y), 0x10);
        float shiftLen = (Obj("Cube").Mesh.Vertices[best] - pre).Length;
        Check(shiftLen > 0 && Math.Abs(shiftLen / freeLen - 0.25f) < 0.05f, "Shift makes the drag a quarter as fast (" + (shiftLen / freeLen).ToString("0.00") + ")");
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube);
        Click(Scr(cube, cube.Mesh.Vertices[best]));
        Drag(c1, new Point(c1.X + 63, c1.Y - 37), 0x11);
        cube = Obj("Cube");
        Vec3 sw = SceneGeometry.TransformVertex(cube.Mesh.Vertices[best], cube);
        float g = V.SnapGrid;
        Check(Near(sw.X / g, (float)Math.Round(sw.X / g), 1e-3f) && Near(sw.Y / g, (float)Math.Round(sw.Y / g), 1e-3f) && Near(sw.Z / g, (float)Math.Round(sw.Z / g), 1e-3f), "Ctrl snaps the corner to the grid (" + sw + ")");
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube);

        // ── held X key constrains a free drag ──
        Click(Scr(cube, cube.Mesh.Vertices[best]));
        Key(0x59, true);   // Y
        Mouse(0, c1); Mouse(0x0002, c1);
        for (int i = 1; i <= 10; i++) Mouse(0, new Point(c1.X + 6 * i, c1.Y - 3 * i));
        Mouse(0x0004, new Point(c1.X + 60, c1.Y - 30));
        Key(0x59, false); Pump(60);
        cube = Obj("Cube");
        Vec3 dy = cube.Mesh.Vertices[best] - pre;
        Check(Math.Abs(dy.Y) > 0.05f && Near(dy.X, 0, 1e-5f) && Near(dy.Z, 0, 1e-5f), "holding Y while dragging constrains the move to Y (" + dy + ")");
        Call(form, "Undo"); Pump(100); cube = Obj("Cube"); Select(cube);

        // ── a face of the cube: the click lands inside it, the whole side moves ──
        // the triangle that shows the largest on screen (its centroid is far from every edge)
        Func<int[], float> area = f => { Point a = Scr(cube, cube.Mesh.Vertices[f[0]]), b = Scr(cube, cube.Mesh.Vertices[f[1]]), c = Scr(cube, cube.Mesh.Vertices[f[2]]); return Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)); };
        var f0 = cube.Mesh.Faces.OrderByDescending(area).First();
        Vec3 inside = (cube.Mesh.Vertices[f0[0]] + cube.Mesh.Vertices[f0[1]] + cube.Mesh.Vertices[f0[2]]) * (1f / 3f);
        Point fp = Scr(cube, inside);
        Drag(fp, new Point(fp.X + 40, fp.Y - 60));
        cube = Obj("Cube");
        Check(Changed(cube.Mesh, original) == 4, "dragging a side of the cube moves its four corners together (" + Changed(cube.Mesh, original) + " changed)");
        Vec3 off = cube.Mesh.Vertices.Where((v, i) => (v - original.Vertices[i]).Length > 1e-6f).Select((v) => v).First() - original.Vertices[Enumerable.Range(0, 8).First(i => (cube.Mesh.Vertices[i] - original.Vertices[i]).Length > 1e-6f)];
        Check(Enumerable.Range(0, 8).Where(i => (cube.Mesh.Vertices[i] - original.Vertices[i]).Length > 1e-6f).All(i => ((cube.Mesh.Vertices[i] - original.Vertices[i]) - off).Length < 1e-4f), "...by the same offset");
        var dropped = ExportManager.ToBinarySTL(new[] { cube }, true);
        Check(dropped.Length == 84 + 50 * (int)BitConverter.ToUInt32(dropped, 80), "the edited cube exports to a well-formed STL");

        // ── a locked object cannot be edited ──
        cube.IsLocked = true; Call(form, "UpdateList"); Pump(60);
        Select(cube);
        var lockedMesh = cube.Mesh.Clone(); int u1 = Hist.UndoCount;
        Point lp = Scr(cube, cube.Mesh.Vertices[0]);
        Drag(lp, new Point(lp.X + 50, lp.Y + 30));
        Check(Same(Obj("Cube").Mesh, lockedMesh) && Hist.UndoCount == u1, "a locked object does not change and takes no undo step");
        cube.IsLocked = false;

        // ── a rotated, non-uniformly scaled sphere: the point still follows the pointer ──
        Scn.Objects.Remove(cube);
        var ball = new SceneObject { Name = "Ball", PrimitiveType = "Sphere", Mesh = PrimitiveFactory.CreateSphere(0.5f, 10, 14), Position = new Vec3(0, 0.8f, 0), Rotation = new Vec3(20, 40, 10), Scale = new Vec3(1.6f, 0.7f, 1.1f), Material = new Material { Color = Color.FromArgb(120, 170, 220) } };
        Scn.Objects.Add(ball); Call(form, "UpdateList"); Select(ball); V.Camera.Target = new Vec3(0, 0.8f, 0); V.Refresh3D(); Pump(300);
        var ballOrig = ball.Mesh.Clone();
        int top = 0; float bz2 = float.MinValue;
        for (int i = 0; i < ball.Mesh.Vertices.Count; i++) { float z = V.Camera.ToCameraSpace(SceneGeometry.TransformVertex(ball.Mesh.Vertices[i], ball)).Z; if (z > bz2) { bz2 = z; top = i; } }
        Point bp = Scr(ball, ball.Mesh.Vertices[top]);
        Drag(bp, new Point(bp.X - 55, bp.Y - 45));
        ball = Obj("Ball");
        Point bafter = Scr(ball, ball.Mesh.Vertices[top]);
        Check(Math.Abs(bafter.X - (bp.X - 55)) <= 3 && Math.Abs(bafter.Y - (bp.Y - 45)) <= 3, "on a rotated, stretched sphere the point follows the pointer (from " + bp + " to " + bafter + ", wanted " + new Point(bp.X - 55, bp.Y - 45) + ")");
        Check(Changed(ball.Mesh, ballOrig) >= 1 && ball.Mesh.Faces.Count == ballOrig.Faces.Count, "the sphere keeps its topology (" + Changed(ball.Mesh, ballOrig) + " welded vertices moved)");
        Check(ball.Mesh.Vertices.All(v => !float.IsNaN(v.X + v.Y + v.Z) && !float.IsInfinity(v.X + v.Y + v.Z)), "every vertex stays finite");

        // ── save and load keep the edit ──
        string path = Path.Combine(Path.GetTempPath(), "aplat-es-" + Guid.NewGuid().ToString("N") + ".aplat");
        try
        {
            SceneFileManager.Save(Scn, path);
            var back = SceneFileManager.Load(path);
            Check(Same(back.Objects.First(o => o.Name == "Ball").Mesh, ball.Mesh), "the edited sphere round-trips through a saved .aplat");
        }
        finally { try { File.Delete(path); } catch { } }

        // ── the picture: the top of a lit cube pulled up by its handle, one corner of it pulled aside ──
        Scn.Objects.Clear(); Call(form, "UpdateList");
        Call(form, "AddPrimitive", PrimitiveKind.Cube); Pump(100);
        var box = Scn.Objects.Last(); box.Name = "Box"; box.Position = new Vec3(0, 0.5f, 0); box.Material.Color = Color.FromArgb(232, 178, 96);
        var L = Scn.Light; L.Enabled = true; L.Shadows = true; L.Azimuth = 300f; L.Elevation = 38f; L.Intensity = 1.1f; L.Ambient = 0.45f;
        V.Camera.Target = new Vec3(0, 0.9f, 0); V.Camera.AngleX = 27; V.Camera.AngleY = -36; V.Camera.Distance = 3.6f;
        Select(box); SetMode(ToolMode.EditShape); V.Refresh3D(); Pump(300);
        var topTri = box.Mesh.Faces.First(f => f.All(i => box.Mesh.Vertices[i].Y > 0.4f));
        Point tp = Scr(box, (box.Mesh.Vertices[topTri[0]] + box.Mesh.Vertices[topTri[1]] + box.Mesh.Vertices[topTri[2]]) * (1f / 3f));
        Click(tp);
        Vec3 tc = SceneGeometry.TransformVertex(new Vec3(0, 0.5f, 0), box);
        Point yTip = ScreenWorld(tc + new Vec3(0, 0.65f, 0));
        Drag(yTip, new Point(yTip.X, yTip.Y - 70));
        box = Obj("Box");
        Check(box.Mesh.Vertices.Count(v => v.Y > 0.9f) == 4 && box.Mesh.Vertices.Count(v => Near(v.Y, -0.5f)) == 4, "the top of the cube was pulled up by its Y handle: four corners above 0.9, four still at the bottom");
        int corner2 = Enumerable.Range(0, 8).Where(i => box.Mesh.Vertices[i].Y > 0.9f).OrderByDescending(i => V.Camera.ToCameraSpace(SceneGeometry.TransformVertex(box.Mesh.Vertices[i], box)).Z).First();
        Point cp = Scr(box, box.Mesh.Vertices[corner2]);
        Drag(cp, new Point(cp.X + 55, cp.Y + 10));
        Click(Scr(Obj("Box"), Obj("Box").Mesh.Vertices[corner2]));
        Shot("edit-shape");

        // ── leaving the mode ──
        SetMode(ToolMode.Select);
        Check(V.CurrentTool == ToolMode.Select && !((ToolStripButton)Get(form, "tbEditShape")).Checked, "leaving the mode unchecks the button");
    }
}
