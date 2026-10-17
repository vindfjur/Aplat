using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aplat;
using Aplat.Controls;

// The FPS / load plate (View → Show FPS) and the About dialog, on the real main window (not shown on screen).
//   tools\paneltest\run.ps1 -Source tools\perftest\PerfTests.cs
class PerfTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F).First(x => x.Name == n && x.GetParameters().Length == a.Length);
        try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
    // Mouse messages with a position in screen coordinates (converted to the plate's client coordinates).
    static void Mouse(Form hud, uint msg, System.Drawing.Point screen)
    {
        var c = hud.PointToClient(screen);
        SendMessage(hud.Handle, msg, msg == WM_MOUSEMOVE || msg == WM_LBUTTONUP ? IntPtr.Zero : (IntPtr)1, (IntPtr)(((c.Y & 0xFFFF) << 16) | (c.X & 0xFFFF)));
    }

    static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
    }

    [STAThread]
    static int Main()
    {
        try { return Run(); }
        catch (Exception e) { Console.WriteLine("FAIL crashed: " + e); return 2; }
    }

    static int Run()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm();
        form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-4000, -4000);
        form.Show(); Pump(500);
        var view = (SceneView)Get(form, "sceneView");
        var menu = (ToolStripMenuItem)Get(form, "menuPerf");

        Check(Get(form, "_perfHud") == null && !menu.Checked, "the plate is off by default");
        long before = view.FrameCount;
        view.Refresh3D(); Pump(300);
        Check(view.FrameCount > before, "every drawn frame is counted (" + (view.FrameCount - before) + ")");

        Call(form, "SetShowPerf", true, false); Pump(1300);
        var hud = (Form)Get(form, "_perfHud");
        Check(hud != null && hud.Visible && menu.Checked, "switched on: the plate is shown and the menu item is checked");
        Check(hud != null && hud.Owner == form, "the plate belongs to the main window");
        Check(hud != null && hud.Width > 90 && hud.Width < 400, "its width fits the text (" + (hud != null ? hud.Width : 0) + " px)");
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
        int ex = hud != null ? GetWindowLong(hud.Handle, GWL_EXSTYLE) : 0;
        Check((ex & WS_EX_TRANSPARENT) == 0 && (ex & WS_EX_NOACTIVATE) != 0, "it receives the mouse (to be dragged) and takes no focus");
        var panel = (Control)Get(form, "panelMain");
        var corner = panel.PointToScreen(System.Drawing.Point.Empty);
        Check(hud != null && hud.Left == corner.X + 16 && hud.Top == corner.Y + 16, "it sits in the top-left corner of the viewport");
        view.Refresh3D(); Pump(700);
        Call(form, "UpdatePerf");
        Check(hud != null && hud.Visible, "it keeps running while frames are drawn");

        // Dragging: the plate follows the mouse, stays inside the viewport and is remembered.
        string perfFile = (string)typeof(MainForm).GetProperty("PerfPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);
        string backup = System.IO.File.Exists(perfFile) ? System.IO.File.ReadAllText(perfFile) : null;
        var area = panel.RectangleToScreen(panel.ClientRectangle);
        var start = hud.Location;
        var grab = new System.Drawing.Point(hud.Left + 12, hud.Top + 10);
        Mouse(hud, WM_LBUTTONDOWN, grab);
        Mouse(hud, WM_MOUSEMOVE, new System.Drawing.Point(grab.X + 120, grab.Y + 70));
        Check(hud.Left == start.X + 120 && hud.Top == start.Y + 70, "dragging moves the plate with the mouse (" + hud.Location + ")");
        Mouse(hud, WM_MOUSEMOVE, new System.Drawing.Point(area.Right + 500, area.Bottom + 500));
        Check(hud.Right <= area.Right && hud.Bottom <= area.Bottom && hud.Right >= area.Right - 1, "it cannot be dragged out past the bottom-right of the viewport");
        Mouse(hud, WM_MOUSEMOVE, new System.Drawing.Point(area.Left - 500, area.Top - 500));
        Check(hud.Left == area.Left && hud.Top == area.Top, "nor past the top-left of the viewport");
        Mouse(hud, WM_MOUSEMOVE, new System.Drawing.Point(grab.X + 120, grab.Y + 70));
        var spot = hud.Location;
        Mouse(hud, WM_LBUTTONUP, new System.Drawing.Point(grab.X + 120, grab.Y + 70));
        Pump(100);
        string saved = System.IO.File.Exists(perfFile) ? System.IO.File.ReadAllText(perfFile) : "";
        string[] lines = saved.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length == 2 && lines[0] == "on" && lines[1] == (spot.X - area.X) + " " + (spot.Y - area.Y), "the spot is saved next to the on flag (" + saved.Replace("\n", "|") + ")");
        Check(GetForegroundWindow() != hud.Handle, "dragging does not give the plate the focus");
        Call(form, "PositionPerfHud");
        Check(hud.Location == spot, "it is put back at the remembered spot when the window is laid out again");
        Call(form, "StopPerf"); Call(form, "StartPerf"); Pump(300);
        hud = (Form)Get(form, "_perfHud");
        Check(hud.Location == spot, "a new plate (switched off and on) appears at the remembered spot");

        // An old file with only the flag still reads, and a saved spot outside a smaller viewport is pulled back in.
        System.IO.File.WriteAllText(perfFile, "on");
        Call(form, "LoadPerf");
        Check(Get(form, "_perfOffset") == null, "an old one-line file is still read (no saved spot)");
        System.IO.File.WriteAllText(perfFile, "on\n99999 99999\n");
        Call(form, "LoadPerf"); Call(form, "PositionPerfHud");
        Check(hud.Right <= area.Right && hud.Bottom <= area.Bottom, "an out-of-range saved spot is clamped into the viewport");

        // Double click: back to the corner, and that is saved too.
        var dbl = new System.Drawing.Point(hud.Left + 12, hud.Top + 10);
        Mouse(hud, WM_LBUTTONDOWN, dbl); Mouse(hud, WM_LBUTTONUP, dbl);
        Mouse(hud, WM_LBUTTONDBLCLK, dbl); Mouse(hud, WM_LBUTTONUP, dbl);   // WinForms raises the double click on the release
        Pump(100);
        Check(hud.Left == area.X + 16 && hud.Top == area.Y + 16, "double click puts the plate back in the corner");
        saved = System.IO.File.ReadAllText(perfFile).Trim();
        Check(saved == "on", "and the saved spot is cleared (" + saved.Replace("\n", "|") + ")");
        if (backup != null) System.IO.File.WriteAllText(perfFile, backup); else System.IO.File.Delete(perfFile);

        Call(form, "SetShowPerf", false, false); Pump(100);
        Check(Get(form, "_perfHud") == null && !menu.Checked, "switched off: the plate is gone");

        // Help → "Check for updates at start-up": the click must really switch the setting (and save it) and the
        // icon must tell the two states apart (a lit bell / a crossed-out bell).
        var auto = (ToolStripMenuItem)Get(form, "menuAutoUpdate");
        string updFile = (string)typeof(MainForm).GetProperty("UpdatesPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);
        string updBackup = System.IO.File.Exists(updFile) ? System.IO.File.ReadAllText(updFile) : null;
        System.IO.File.WriteAllText(updFile, "on\n2000-01-01\n"); Call(form, "LoadUpdateSettings");
        Call(form, "ShowAutoUpdateState");
        Check(auto.Checked && auto.Tag.ToString() == "Bell", "updates at start-up on: lit bell");
        auto.PerformClick();
        Check(!(bool)Get(form, "_autoUpdate") && !auto.Checked && auto.Tag.ToString() == "BellOff", "a click switches it off: crossed-out bell, not lit");
        Check(System.IO.File.ReadAllText(updFile).StartsWith("off") && System.IO.File.ReadAllText(updFile).Contains("2000-01-01"), "the off state is saved with the date of the last check");
        auto.PerformClick();
        Check((bool)Get(form, "_autoUpdate") && auto.Checked && System.IO.File.ReadAllText(updFile).StartsWith("on"), "a second click switches it back on and saves it");
        if (updBackup != null) System.IO.File.WriteAllText(updFile, updBackup); else System.IO.File.Delete(updFile);
        Call(form, "LoadUpdateSettings"); Call(form, "ShowAutoUpdateState");

        using (var about = new Aplat.Dialogs.AboutDialog(null))
        {
            about.Show(); Pump(300);
            var labels = about.Controls.OfType<Label>().ToList();
            Check(labels.Any(l => l.Text.StartsWith("Aplat")) && labels.Any(l => l.Text.Contains("vindfjur")), "About: a description and the developer");
            Check(about.Controls.OfType<Control>().All(c => c.Bottom <= about.ClientSize.Height), "About: nothing is cut off at the bottom");
            Check(about.Controls.OfType<Control>().All(c => c.Right <= about.ClientSize.Width), "About: nothing is cut off at the side");
            about.Close();
        }

        ((Aplat.Models.Scene)Get(form, "_scene")).IsModified = false;
        form.Close();
        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
