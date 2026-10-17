using System;
using System.Collections.Generic;
using System.Diagnostics;
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

// Painting on a shape that was deformed in Shape mode, in the real window: the pick under the cursor
// agrees with an independent ray cast, a dab lands exactly where the cursor is (every painted texel
// projects inside the brush circle), the paint stays on its faces when the shape is edited again, and a
// dab costs little (time per dab, per frame, per stroke is printed — compare it before and after a change).
// Needs an interactive desktop with OpenGL. Pictures go to %TEMP%\aplat-shots (or APLAT_SHOTS).
//   tools\paneltest\run.ps1 -Source tools\editshapetest\PaintDeformedTests.cs
class PaintDeformedTests
{
    static readonly string Out = Environment.GetEnvironmentVariable("APLAT_SHOTS") ?? Path.Combine(Path.GetTempPath(), "aplat-shots");
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

    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static MainForm form; static SceneView V; static Control GL;
    static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static void Pump(int ms = 30) { for (int i = 0; i < ms / 10; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); } }
    static void Mouse(uint flags, Point p)
    {
        var s = GL.PointToScreen(p); var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        int ax = (int)((s.X * 65535L + b.Width / 2) / (b.Width - 1)), ay = (int)((s.Y * 65535L + b.Height / 2) / (b.Height - 1));
        mouse_event(0x8001 | flags, ax, ay, 0, UIntPtr.Zero); Pump(14);
    }
    static void Select(SceneObject o) { Call(form, "Select", o); Pump(60); }
    static void SetMode(ToolMode m) { Call(form, "SetTool", m); Pump(60); }

    // ── the same maths as the app, written again from scratch ──
    static int Cols(Mesh m) { return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, m.Faces.Count)))); }
    static PointF Atlas(Mesh m, int fi, int k, int n)
    {
        int cols = Cols(m); float cell = 1f / cols;
        float a = (float)(2 * Math.PI * k / n - Math.PI / 2);
        return new PointF((fi % cols + 0.5f + 0.38f * (float)Math.Cos(a)) * cell, (fi / cols + 0.5f + 0.38f * (float)Math.Sin(a)) * cell);
    }
    static Vec3 W(SceneObject o, int vi) { return SceneGeometry.TransformVertex(o.Mesh.Vertices[vi], o); }

    // World ray through a pixel (perspective view).
    static void Ray(Point p, out Vec3 org, out Vec3 dir)
    {
        var c = V.Camera; float fov = Math.Min(GL.Width, GL.Height) * 0.72f;
        Vec3 t = c.Target, c0 = c.ToCameraSpace(t);
        Vec3 cx = c.ToCameraSpace(t + new Vec3(1, 0, 0)) - c0, cy = c.ToCameraSpace(t + new Vec3(0, 1, 0)) - c0, cz = c.ToCameraSpace(t + new Vec3(0, 0, 1)) - c0;
        Func<float, Vec3> at = d =>
        {
            Vec3 cam = new Vec3((p.X - GL.Width / 2f) * d / fov, -(p.Y - GL.Height / 2f) * d / fov, -d);
            Vec3 v = cam - c0;
            return t + new Vec3(Vec3.Dot(cx, v), Vec3.Dot(cy, v), Vec3.Dot(cz, v));
        };
        org = at(0.5f); dir = (at(5f) - org).Normalized();
    }

    // The nearest front-facing triangle (fan of every face) under the pixel: object-space hit point, or null.
    static Vec3? RayHit(SceneObject o, Point p, out int face)
    {
        Ray(p, out Vec3 org, out Vec3 dir);
        float best = float.MaxValue; Vec3? hit = null; face = -1;
        for (int fi = 0; fi < o.Mesh.Faces.Count; fi++)
        {
            var f = o.Mesh.Faces[fi];
            for (int i = 1; i + 1 < f.Length; i++)
            {
                Vec3 a = W(o, f[0]), b = W(o, f[i]), c = W(o, f[i + 1]);
                Vec3 e1 = b - a, e2 = c - a, h = Vec3.Cross(dir, e2);
                float det = Vec3.Dot(e1, h);
                if (det < 1e-9f) continue;                // front faces only: counter-clockwise seen from outside, so the normal opposes the ray (det = -dir·normal)
                float inv = 1f / det; Vec3 s = org - a;
                float u = Vec3.Dot(s, h) * inv; if (u < 0 || u > 1) continue;
                Vec3 q = Vec3.Cross(s, e1); float v = Vec3.Dot(dir, q) * inv; if (v < 0 || u + v > 1) continue;
                float t = Vec3.Dot(e2, q) * inv;
                if (t > 0 && t < best) { best = t; hit = org + dir * t; face = fi; }
            }
        }
        return hit;
    }

    static bool Pick(Point p, out SceneObject o, out PointF uv, out Vec3 P, out Vec3 n)
    {
        var args = new object[] { p, null, null, null, null };
        bool ok = (bool)Call(V, "PickPaintTarget", args);
        o = (SceneObject)args[1]; uv = ok ? (PointF)args[2] : PointF.Empty; P = ok ? (Vec3)args[3] : Vec3.Zero; n = ok ? (Vec3)args[4] : Vec3.Zero;
        return ok;
    }

    // A painted texel back on the surface: (face, triangle) from the cell, world position from its barycentrics.
    static bool TexelWorld(SceneObject o, int tx, int ty, int size, out Vec3 world, out int face)
    {
        world = Vec3.Zero; face = -1;
        int cols = Cols(o.Mesh);
        int cx = tx * cols / size, cy = ty * cols / size, fi = cy * cols + cx;
        if (fi >= o.Mesh.Faces.Count) return false;
        var f = o.Mesh.Faces[fi]; float x = tx + 0.5f, y = ty + 0.5f;
        for (int i = 1; i + 1 < f.Length; i++)
        {
            PointF a = Atlas(o.Mesh, fi, 0, f.Length), b = Atlas(o.Mesh, fi, i, f.Length), c = Atlas(o.Mesh, fi, i + 1, f.Length);
            a = new PointF(a.X * size, a.Y * size); b = new PointF(b.X * size, b.Y * size); c = new PointF(c.X * size, c.Y * size);
            float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(det) < 1e-6f) continue;
            float l0 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det, l1 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det, l2 = 1 - l0 - l1;
            if (l0 < -0.01f || l1 < -0.01f || l2 < -0.01f) continue;
            world = W(o, f[0]) * l0 + W(o, f[i]) * l1 + W(o, f[i + 1]) * l2; face = fi;
            return true;
        }
        return false;
    }

    // Texel of the layer → where it shows on the screen now (the point on the shape it is attached to).
    static List<PointF> PaintedOnScreen(SceneObject o, out int count, out HashSet<int> faces, int minAlpha = 1)
    {
        var res = new List<PointF>(); faces = new HashSet<int>(); count = 0;
        var layer = o.Material.PaintLayer; int size = layer.Width;
        var d = layer.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[size * size]; Marshal.Copy(d.Scan0, px, 0, px.Length); layer.UnlockBits(d);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if ((px[y * size + x] >> 24 & 0xFF) < minAlpha) continue;
                count++;
                if (!TexelWorld(o, x, y, size, out Vec3 w, out int fi)) continue;
                faces.Add(fi); res.Add(V.Camera.Project(w, GL.Width, GL.Height));
            }
        return res;
    }

    static void ClearPaint(SceneObject o) { o.Material.ClearPaint(); o.Material.EnsurePaintLayer(); }

    // ── deforming through the editor, as a drag in Shape mode does ──
    static void PullPoint(SceneObject o, MeshEditor ed, Vec3 near, Vec3 delta)
    {
        int best = 0; float bd = float.MaxValue;
        for (int p = 0; p < ed.PointCount; p++) { float d = (ed.PointAt(p) - near).Length; if (d < bd) { bd = d; best = p; } }
        ed.SelectPoint(best); ed.BeginDrag(); ed.MoveBy(delta); ed.EndDrag();
    }
    static void PullFace(MeshEditor ed, int face, Vec3 delta) { ed.SelectFace(face); ed.BeginDrag(); ed.MoveBy(delta); ed.EndDrag(); }

    static SceneObject Make(string name, PrimitiveKind kind, Vec3 pos)
    {
        Call(form, "AddPrimitive", kind); Pump(60);
        var o = Scn.Objects.Last(); o.Name = name; o.Position = pos; o.Material.Color = Color.FromArgb(150, 190, 235);
        return o;
    }

    static void Deform(SceneObject o)
    {
        var ed = new MeshEditor(o.Mesh);
        var vs = o.Mesh.Vertices; var rnd = new Random(7);
        // a few points pulled hard in different directions, then a face dragged sideways
        for (int k = 0; k < 5; k++)
        {
            Vec3 at = vs[rnd.Next(vs.Count)];
            PullPoint(o, ed, at, new Vec3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.3f, (float)rnd.NextDouble() - 0.5f) * 1.1f);
        }
        PullFace(ed, o.Mesh.Faces.Count / 3, new Vec3(0.35f, 0.15f, -0.3f));
        Call(form, "Select", o);
    }

    static void Frame(SceneObject o)
    {
        Scn.Objects.RemoveAll(x => x != o);
        Call(form, "UpdateList");
        V.Camera.Mode = ViewMode.Perspective; V.Camera.Target = o.Position; V.Camera.AngleX = 24; V.Camera.AngleY = -34; V.Camera.Distance = 4.2f;
        Select(o); V.Refresh3D(); GL.Refresh(); Pump(100);
    }

    static double Median(List<double> l) { l.Sort(); return l[l.Count / 2]; }

    static void Shot(string name)
    {
        form.TopMost = true; form.Activate(); Pump(250);
        Mouse(0, new Point(GL.Width - 40, GL.Height - 40)); Pump(250);
        RECT r; DwmGetWindowAttribute(form.Handle, 9, out r, Marshal.SizeOf(typeof(RECT)));
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(new Point(r.L, r.T), Point.Empty, bmp.Size);
            bmp.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
        }
        form.TopMost = false;
        Console.WriteLine("shot " + Path.Combine(Out, name + ".png"));
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
        V.BrushRadius = 28; V.BrushOpacity = 100; V.BrushColor = Color.FromArgb(220, 38, 38);

        form.Activate(); Mouse(0, new Point(GL.Width - 30, GL.Height - 30)); Mouse(0x0002, new Point(GL.Width - 30, GL.Height - 30)); Mouse(0x0004, new Point(GL.Width - 30, GL.Height - 30));
        var kinds = new[] { PrimitiveKind.Sphere, PrimitiveKind.Cube, PrimitiveKind.Cylinder };
        foreach (var kind in kinds)
        {
            Console.WriteLine("── " + kind + " ──");
            var o = Make(kind.ToString(), kind, new Vec3(0, 0.6f, 0));
            Frame(o);
            var pristine = new List<double>(); var sw0 = new Stopwatch();
            for (int i = 0; i < 6; i++) { sw0.Restart(); GL.Refresh(); pristine.Add(sw0.Elapsed.TotalMilliseconds); }
            Console.WriteLine("PERF " + kind + " frame of the untouched shape=" + Median(pristine).ToString("0.0") + "ms");
            Deform(o);
            Frame(o);
            SetMode(ToolMode.Paint);
            V.PaintKind = BrushKind.Brush;
            o.Material.EnsurePaintLayer();
            int faces = o.Mesh.Faces.Count;

            // 1. the pick agrees with a ray cast, point and all
            int tried = 0, agreed = 0, missed = 0, onScreen = 0; float worst = 0;
            var samples = new List<Point>();
            for (int y = 60; y < GL.Height - 60; y += 23)
                for (int x = 60; x < GL.Width - 60; x += 23)
                {
                    var p = new Point(x, y);
                    int rf; Vec3? rh = RayHit(o, p, out rf);
                    bool ok = Pick(p, out SceneObject po, out PointF uv, out Vec3 P, out Vec3 n);
                    if (rh == null && !ok) continue;
                    tried++;
                    if (rh == null || !ok) { missed++; Console.WriteLine("  miss at " + p + ": ray " + (rh == null ? "none" : "face " + rf + " " + rh.Value) + ", pick " + (ok ? "found" : "none")); continue; }
                    PointF sp = V.Camera.Project(P, GL.Width, GL.Height);
                    float d = (float)Math.Sqrt((sp.X - p.X) * (sp.X - p.X) + (sp.Y - p.Y) * (sp.Y - p.Y));
                    if (d > worst) worst = d;
                    if ((P - rh.Value).Length < 2e-3f) agreed++;
                    if (d < 1f) onScreen++;
                    if (ok) samples.Add(p);
                }
            samples = Enumerable.Range(0, 14).Select(i => samples[i * (samples.Count - 1) / 13]).ToList();
            Check(tried > 20, kind + ": " + tried + " cursor positions are on the deformed shape");
            Check(missed <= tried / 40, kind + ": the pick finds a surface wherever the ray cast does (" + missed + " of " + tried + " differ; a pixel exactly on an edge may go either way)");
            Check(agreed >= tried - tried / 40 - 1, kind + ": the pick returns the nearest visible surface point (" + agreed + " of " + tried + " equal the ray cast)");
            Check(onScreen >= tried - tried / 40 - 1 && worst < 3f, kind + ": the picked point projects back onto the cursor (worst " + worst.ToString("0.00") + " px)");

            // 2. a dab lands under the cursor: every painted texel is on the surface inside the brush circle
            int placedOk = 0, placedTotal = 0; float maxOut = 0;
            foreach (var p in samples)
            {
                ClearPaint(o);
                if (!Pick(p, out SceneObject _, out PointF uv, out Vec3 _, out Vec3 _)) continue;
                Call(V, "PaintDab", p);
                int count; HashSet<int> fs;
                var pts = PaintedOnScreen(o, out count, out fs);
                placedTotal++;
                float far = pts.Count == 0 ? 999f : pts.Max(q => (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)));
                var layer = o.Material.PaintLayer;
                bool centre = layer.GetPixel(Math.Min(layer.Width - 1, (int)(uv.X * layer.Width)), Math.Min(layer.Height - 1, (int)(uv.Y * layer.Height))).A > 100;
                if (far > maxOut) maxOut = far;
                if (count > 0 && far <= V.BrushRadius * 1.0f + 2f && centre) placedOk++;
            }
            Check(placedTotal >= 8 && placedOk == placedTotal, kind + ": every painted texel of a dab is inside the brush circle around the cursor, and the cursor texel is painted (" + placedOk + "/" + placedTotal + ", farthest " + maxOut.ToString("0.0") + " px of " + V.BrushRadius + ")");

            // 3. the paint stays on its faces when the shape is edited further. Tried at several spots until
            //    the dabbed point is still in view after the edit (an edit may well hide it behind the shape).
            bool seen = false;
            foreach (var cand in samples.Skip(3))
            {
                ClearPaint(o);
                var backup = o.Mesh.Vertices.ToList();
                Pick(cand, out SceneObject _, out PointF uv0, out Vec3 P0, out Vec3 _);
                Call(V, "PaintDab", cand);
                int c0; HashSet<int> f0; PaintedOnScreen(o, out c0, out f0);
                var ed2 = new MeshEditor(o.Mesh);
                PullPoint(o, ed2, o.Mesh.Vertices[o.Mesh.Faces[f0.First()][0]], new Vec3(0.25f, -0.2f, 0.3f));
                V.Refresh3D(); GL.Refresh(); Pump(60);
                int c1; HashSet<int> f1; PaintedOnScreen(o, out c1, out f1);
                Check(c0 == c1 && f0.SetEquals(f1), kind + ": editing the shape afterwards leaves the paint on the same faces (" + c0 + " texels on " + f0.Count + " faces)");
                int tx = (int)(uv0.X * o.Material.PaintLayer.Width), ty = (int)(uv0.Y * o.Material.PaintLayer.Height);
                TexelWorld(o, tx, ty, o.Material.PaintLayer.Width, out Vec3 wNow, out int _);
                Point scrNow = Point.Round(V.Camera.Project(wNow, GL.Width, GL.Height));
                int rf; Vec3? rh = RayHit(o, scrNow, out rf);
                if (rh == null || (rh.Value - wNow).Length > 0.01f) { for (int i = 0; i < backup.Count; i++) o.Mesh.Vertices[i] = backup[i]; continue; }   // hidden now: try another spot
                seen = true;
                // the texel that was under the cursor is somewhere else on the screen now; a pick there finds it again
                bool ok = Pick(scrNow, out SceneObject _, out PointF uv1, out Vec3 _, out Vec3 _);
                Check(ok && Math.Abs(uv1.X - uv0.X) < 0.02f && Math.Abs(uv1.Y - uv0.Y) < 0.02f, kind + ": picking where the painted point moved to returns the same texel again (" + uv0.X.ToString("0.000") + "," + uv0.Y.ToString("0.000") + " → " + (ok ? uv1.X.ToString("0.000") + "," + uv1.Y.ToString("0.000") : "nothing") + ")");

                // 4. the eraser takes the paint off exactly where it now is
                V.PaintKind = BrushKind.Eraser;
                Call(V, "PaintDab", scrNow);
                var lay = o.Material.PaintLayer;
                int centreA = lay.GetPixel(Math.Min(lay.Width - 1, (int)(uv0.X * lay.Width)), Math.Min(lay.Height - 1, (int)(uv0.Y * lay.Height))).A;
                PaintedOnScreen(o, out int c2, out HashSet<int> _, 40);
                Check(centreA < 40 && c2 < c0 / 2, kind + ": the eraser where the dab has moved to takes the paint off its centre (" + c0 + " → " + c2 + " texels, centre alpha " + centreA + ")");
                V.PaintKind = BrushKind.Brush;
                break;
            }
            Check(seen, kind + ": a painted point stays in view after an edit at one of the spots tried");

            // 5. the cost of painting
            ClearPaint(o);
            var pick = new List<double>(); var dab = new List<double>(); var frame = new List<double>();
            var sw = new Stopwatch();
            for (int rep = 0; rep < 2; rep++)
                for (int i = 0; i < samples.Count; i++)
                {
                    var p = samples[i];
                    sw.Restart(); Pick(p, out SceneObject _, out PointF _, out Vec3 _, out Vec3 _); double tp = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); Call(V, "PaintDab", p); double td = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); V.Refresh3D(); GL.Refresh(); double tf = sw.Elapsed.TotalMilliseconds;
                    if (rep == 1) { pick.Add(tp); dab.Add(td); frame.Add(tf); }
                }
            // an idle frame, for reference
            var idle = new List<double>();
            for (int i = 0; i < 6; i++) { sw.Restart(); GL.Refresh(); idle.Add(sw.Elapsed.TotalMilliseconds); }
            Console.WriteLine("PERF " + kind + " faces=" + faces + " pick=" + Median(pick).ToString("0.0") + "ms dab(pick+paint)=" + Median(dab).ToString("0.0") + "ms frame-after-dab=" + Median(frame).ToString("0.0") + "ms idle-frame=" + Median(idle).ToString("0.0") + "ms");

            // a stroke: 40 mouse moves across the shape, each handled as the view does (the dabs, then a frame)
            ClearPaint(o);
            Point a0 = samples[0], b0 = samples[samples.Count - 1];
            var lastPt = typeof(SceneView).GetField("_lastPaintPt", F);
            lastPt.SetValue(V, a0);
            var perMove = new List<double>();
            var all = Stopwatch.StartNew();
            for (int i = 1; i <= 40; i++)
            {
                sw.Restart();
                Call(V, "PaintLineTo", new Point(a0.X + (b0.X - a0.X) * i / 40, a0.Y + (b0.Y - a0.Y) * i / 40));
                GL.Refresh();
                perMove.Add(sw.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine("PERF " + kind + " stroke of 40 moves over " + (int)Math.Sqrt((b0.X - a0.X) * (b0.X - a0.X) + (b0.Y - a0.Y) * (b0.Y - a0.Y)) + " px: " + all.Elapsed.TotalMilliseconds.ToString("0") + "ms in all, " + Median(perMove).ToString("0.0") + "ms per move (median) incl. the frame");
            PaintedOnScreen(o, out int cs, out HashSet<int> _);
            Check(cs > 200, kind + ": the stroke painted (" + cs + " texels)");

            // the once-per-stroke and once-per-click costs, on a layer that holds a stroke
            sw.Restart(); Call(form, "PushUndo", "t"); double tUndo = sw.Elapsed.TotalMilliseconds;
            Point fp = samples[samples.Count / 2];
            Pick(fp, out SceneObject _, out PointF fuv, out Vec3 _, out Vec3 _);
            ClearPaint(o);
            sw.Restart(); Call(V, "DoFill", fp); double tFill = sw.Elapsed.TotalMilliseconds;
            var fl = o.Material.PaintLayer;
            Color fc = fl.GetPixel(Math.Min(fl.Width - 1, (int)(fuv.X * fl.Width)), Math.Min(fl.Height - 1, (int)(fuv.Y * fl.Height)));
            Console.WriteLine("PERF " + kind + " undo snapshot at the start of a stroke=" + tUndo.ToString("0") + "ms, one fill click=" + tFill.ToString("0") + "ms");
            Check(fc.A > 200 && Math.Abs(fc.R - V.BrushColor.R) < 3, kind + ": the fill colours the face under the cursor");

            if (kind == PrimitiveKind.Sphere)
            {
                V.PaintKind = BrushKind.Marker; V.BrushColor = Color.FromArgb(40, 150, 90);
                foreach (var p in samples.Take(5)) Call(V, "PaintDab", p);
                V.PaintKind = BrushKind.Brush; V.BrushColor = Color.FromArgb(220, 38, 38);
                V.Refresh3D(); GL.Refresh();
                Shot("paint-deformed");
            }
        }
    }
}
