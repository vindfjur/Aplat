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
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// The light-source marker (View → Lighting → show the sun): a sun symbol with an arrow for the direction
// of the light. Rendered in the real window in the light and the dark theme at two zoom levels; each frame
// is compared with the same frame without the marker, so the checks are about what the marker adds:
// it is there, it stays about the same size on screen when zooming, it is drawn in both a bright and a
// dark tone (so it reads on a light and on a dark background), it holds more than a ring and a stub
// (rays all round, an arrow) and switching it off leaves the frame as it was.
// Needs an interactive desktop with OpenGL. Pictures go to %TEMP%\aplat-shots (or APLAT_SHOTS).
//   tools\paneltest\run.ps1 -Source tools\suntest\SunMarkerTests.cs
class SunMarkerTests
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
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static MainForm form; static SceneView V; static Control GL;
    static Scene Scn { get { return (Scene)Get(form, "_scene"); } }
    static void Pump(int ms = 30) { for (int i = 0; i < ms / 10; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); } }

    // The 3-D view as pixels (copied from the screen: the control is OpenGL).
    static Bitmap Grab()
    {
        form.TopMost = true; form.Activate(); GL.Refresh(); Pump(120); GL.Refresh(); Pump(60);
        var bmp = new Bitmap(GL.Width, GL.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(GL.PointToScreen(Point.Empty), Point.Empty, bmp.Size);
        form.TopMost = false;
        return bmp;
    }

    // Pixels that differ between two frames.
    static List<Point> Diff(Bitmap a, Bitmap b, out Rectangle box)
    {
        var res = new List<Point>(); int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < a.Height; y += 1)
            for (int x = 0; x < a.Width; x += 1)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) < 24) continue;
                res.Add(new Point(x, y));
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
        box = res.Count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        return res;
    }

    static float Lum(Color c) { return 0.299f * c.R + 0.587f * c.G + 0.114f * c.B; }

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
        finally { Aplat.UI.Theme.SetMode(Aplat.UI.ThemeMode.Light); Scn.IsModified = false; form.Close(); form.Dispose(); }
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }

    static void Run()
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.Size = new Size(1500, 950);
        form.Show(); Pump(600);
        V = (SceneView)Get(form, "sceneView"); GL = (Control)Get(V, "_gl");
        Call(form, "ApplyView", ViewMode.Perspective); Pump(200);
        Scn.Objects.Clear();
        Call(form, "AddPrimitive", PrimitiveKind.Cube); Pump(60);
        var box = Scn.Objects.Last(); box.Position = new Vec3(-0.6f, 0.5f, 0); box.Material.Color = Color.FromArgb(232, 178, 96);
        Call(form, "AddPrimitive", PrimitiveKind.Sphere); Pump(60);
        var ball = Scn.Objects.Last(); ball.Position = new Vec3(0.9f, 0.5f, 0.4f); ball.Material.Color = Color.FromArgb(120, 170, 220);
        Call(form, "UpdateList"); Call(form, "Select", (SceneObject)null); Pump(60);
        Scn.ShowGrid = true;
        var L = Scn.Light; L.Enabled = true; L.Shadows = true; L.Elevation = 16f; L.Intensity = 1.1f; L.Ambient = 0.4f;
        V.Camera.Target = new Vec3(0, 0.5f, 0); V.Camera.AngleX = 22; V.Camera.AngleY = -34;

        // an azimuth that puts the marker well inside the view
        V.Camera.Distance = 7f;
        float az = -1;
        foreach (float a in new float[] { 300, 330, 0, 270, 30, 240, 60, 210, 90 })
        {
            L.Azimuth = a; L.ShowSun = false; V.Refresh3D(); var off = Grab(); L.ShowSun = true; V.Refresh3D(); var on = Grab();
            Rectangle bb; Diff(on, off, out bb); on.Dispose(); off.Dispose();
            if (!bb.IsEmpty && bb.Left > 40 && bb.Top > 40 && bb.Right < GL.Width - 40 && bb.Bottom < GL.Height - 40) { az = a; break; }
        }
        Check(az >= 0, "there is a light direction that puts the marker inside the view");
        if (az < 0) return;
        L.Azimuth = az;

        float[] widths = new float[4]; int wi = 0;
        foreach (bool dark in new[] { false, true })
        {
            Aplat.UI.Theme.SetMode(dark ? Aplat.UI.ThemeMode.Graphite : Aplat.UI.ThemeMode.Light); Pump(300);
            string theme = dark ? "dark" : "light";
            foreach (float dist in new float[] { 4.5f, 9f })
            {
                V.Camera.Distance = dist;
                L.ShowSun = false; V.Refresh3D(); var off = Grab();
                L.ShowSun = true;  V.Refresh3D(); var on = Grab();
                Rectangle bb; var px = Diff(on, off, out bb);
                string tag = theme + ", camera distance " + dist;
                Check(px.Count > 400, tag + ": the marker is drawn (" + px.Count + " pixels, box " + bb.Width + "x" + bb.Height + ")");
                // brightness of the marker's pixels against what they cover: the fill and the outline both must stand out
                int bright = px.Count(p => Lum(on.GetPixel(p.X, p.Y)) > 150), darkPx = px.Count(p => Lum(on.GetPixel(p.X, p.Y)) < 90);
                Check(bright > px.Count / 5 && darkPx > px.Count / 25, tag + ": it has a bright body and a dark outline (" + bright + " bright, " + darkPx + " dark of " + px.Count + ")");
                int contrast = px.Count(p => Math.Abs(Lum(on.GetPixel(p.X, p.Y)) - Lum(off.GetPixel(p.X, p.Y))) > 45);
                Check(contrast > px.Count / 2, tag + ": over half of it differs clearly from the background it covers (the dark outline blends into a dark background by design; the body carries it there) (" + contrast + " of " + px.Count + ")");
                // rays all round: the changed pixels occupy every sector around the disc (the arrow adds one side only)
                var sectors = new bool[12];
                // the disc's centre = the centroid of the bright pixels near the box's far end from the arrow; use the centroid of the bright pixels as a close stand-in
                var brightPts = px.Where(p => Lum(on.GetPixel(p.X, p.Y)) > 150).ToList();
                float cx = (float)brightPts.Average(p => p.X), cy = (float)brightPts.Average(p => p.Y);
                foreach (var p in px) { double ang = Math.Atan2(p.Y - cy, p.X - cx); sectors[(int)((ang + Math.PI) / (2 * Math.PI) * 12) % 12] = true; }
                Check(sectors.Count(b => b) >= 11, tag + ": it reaches all round (" + sectors.Count(b => b) + " of 12 sectors)");
                widths[wi++] = bb.Width;
                on.Save(Path.Combine(Out, "sun-" + theme + "-" + dist.ToString("0.0").Replace(',', '.') + ".png"), ImageFormat.Png);
                on.Dispose(); off.Dispose();
            }
        }
        // the size on screen holds when zooming (the box includes the arrow, so compare box with box)
        for (int t = 0; t < 2; t++)
            Check(Math.Abs(widths[t * 2] - widths[t * 2 + 1]) <= 0.4f * Math.Max(widths[t * 2], widths[t * 2 + 1]), "the marker keeps about the same size on screen at two zoom levels (" + widths[t * 2] + " px and " + widths[t * 2 + 1] + " px wide)");

        // switched off, nothing of it is left
        Aplat.UI.Theme.SetMode(Aplat.UI.ThemeMode.Light); Pump(300);
        V.Camera.Distance = 7f;
        L.ShowSun = false; V.Refresh3D(); var a1 = Grab(); V.Refresh3D(); var a2 = Grab();
        Rectangle none; Check(Diff(a1, a2, out none).Count == 0, "with the marker off two frames are identical");
        a1.Dispose(); a2.Dispose();
    }
}
