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
using Aplat.UI;

// Drives the real window with real mouse input: dragging the floating cards to edges and corners (preview,
// docking, free placement, stacking), keeping them anchored through a maximise, remembering the layout
// between runs, the auto-rotate card and the Cursor / Hand tools. Needs an interactive desktop with OpenGL.
// It also saves a screenshot of every step into SHOTS_OUT (default: a folder in the temp directory).
//   tools\paneltest\run.ps1 -Source tools\docktest\DockUiTests.cs
class DockUiTests
{
    static readonly string Out = Environment.GetEnvironmentVariable("SHOTS_OUT") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aplat-tests", "dock");
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { var f = o.GetType().GetField(n, F); if (f == null) throw new Exception("no field " + n); return f.GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethods(F).First(x => x.Name == n && x.GetParameters().Length == a.Length);
        try { return m.Invoke(o, a); } catch (TargetInvocationException e) { throw e.InnerException; }
    }
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);

    static MainForm form;
    static void Pump(int ms = 30) { for (int i = 0; i < ms / 10; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); } }
    static void TopAll() { foreach (Form f in Application.OpenForms) if (f.Visible && !f.TopMost) f.TopMost = true; }
    static void Mouse(uint flags, Point screen) { if (flags == 0x0002) TopAll(); SetCursorPos(screen.X, screen.Y); if (flags != 0) mouse_event(flags, 0, 0, 0, UIntPtr.Zero); Pump(flags != 0 ? 120 : 40); }

    static List<object> Cards { get { return ((System.Collections.IEnumerable)Get(form, "_overlays")).Cast<object>().ToList(); } }
    static Form Win(int i) { return (Form)Get(Cards[i], "Window"); }
    static PanelPlacement Place(int i) { return (PanelPlacement)Get(Cards[i], "Placement"); }
    static Control Scene { get { return (Control)Get(form, "panelMain"); } }
    static Size SceneSize { get { return (Size)Call(form, "SceneSize"); } }
    // Card rectangle in viewport coordinates.
    static Rectangle R(int i) { var o = Scene.PointToScreen(Point.Empty); var b = Win(i).Bounds; return new Rectangle(b.X - o.X, b.Y - o.Y, b.Width, b.Height); }
    static bool Near(int a, int b, int tol = 1) { return Math.Abs(a - b) <= tol; }

    static Control Handle(int i)
    {
        switch (i)
        {
            case 0: return (Control)Get(form, "floatingToolsFlow");
            case 1: return (Control)Get(form, "recentToolsPanel");   // its padding is bare; the button fills the flow
            case 2: return (Control)Get(form, "layersHeader");
            case 3: return (Control)Get(form, "viewFlow");
            default: return (Control)Get(form, "spinFlow");
        }
    }

    // Presses on the card's background, moves by (dx, dy) in steps, optionally runs `during` while the button
    // is still down (the dock preview is visible then), and releases.
    static void DragCard(int i, int dx, int dy, Action during = null)
    {
        var h = Handle(i);
        var start = h.PointToScreen(new Point(3, 3));
        // The first press after a drop is now and then swallowed by the VM's input queue: retry until the drag starts.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Mouse(0, start); Mouse(0x0002, start);
            Mouse(0, new Point(start.X + 12, start.Y + 12));
            if (Get(form, "_dragged") != null) break;
            { var under = Control.FromHandle(WindowFromPoint(start)); Console.WriteLine("     press missed at " + start + ": under the pointer " + (under == null ? "(foreign window)" : under.GetType().Name + " '" + under.Name + "'") + " active=" + (Form.ActiveForm == null ? "none" : Form.ActiveForm.GetType().Name)); }
            Mouse(0x0004, new Point(start.X + 12, start.Y + 12));
            Pump(200);
            Mouse(0, start);
            start = h.PointToScreen(new Point(3, 3));
        }
        int sx = Win(i).Left - 0, sy = Win(i).Top;   // where the card is now (it followed the first move)
        var cur = Cursor.Position;
        int tx = cur.X + (dx - (cur.X - start.X)), ty = cur.Y + (dy - (cur.Y - start.Y));
        for (int s = 1; s <= 8; s++) Mouse(0, new Point(cur.X + (tx - cur.X) * s / 8, cur.Y + (ty - cur.Y) * s / 8));
        Pump(120);
        if (during != null) during();
        Mouse(0x0004, new Point(tx, ty));
        Pump(250);
        Console.WriteLine("     card " + i + " dragged by " + dx + "," + dy + " → " + R(i) + " " + Place(i).Serialize());
    }

    static void Shot(string name, bool keepMouse = false)
    {
        if (string.IsNullOrEmpty(Out)) return;
        TopAll();
        if (!keepMouse) form.Activate();
        Pump(250);
        if (!keepMouse) Mouse(0, new Point(form.Left + form.Width / 2, form.Top + 4));   // parks the pointer out of the picture
        Pump(150);
        var r = form.WindowState == FormWindowState.Maximized ? Screen.FromControl(form).Bounds : form.Bounds;
        using (var bmp = new Bitmap(r.Width, r.Height))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            bmp.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png);
        }
        Console.WriteLine("shot " + name);
    }

    static string Settings { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aplat"); } }

    static void Open(Aplat.UI.ThemeMode theme, Size size)
    {
        Aplat.UI.Theme.SetMode(theme);
        form = new MainForm();
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.Size = size;
        form.TopMost = true;   // other test programs share this desktop: stay above their windows so the pointer lands on ours
        form.Show(); Pump(900); form.Activate(); Pump(200);
    }

    static void CloseForm() { form.Close(); Pump(300); form.Dispose(); }

    [STAThread]
    static int Main()
    {
        SetProcessDPIAware();
        try { Directory.CreateDirectory(Out); } catch { }
        // The test must not leave the user's remembered layout changed: keep a copy and put it back.
        string panels = Path.Combine(Settings, "panels.txt"), bars = Path.Combine(Settings, "toolbars.txt"), win = Path.Combine(Settings, "window.txt");
        var saved = new Dictionary<string, string>();
        foreach (var p in new[] { panels, bars, win }) { if (File.Exists(p)) saved[p] = File.ReadAllText(p); File.Delete(p); }
        try { Run(panels); }
        finally { foreach (var p in new[] { panels, bars, win }) { if (saved.ContainsKey(p)) File.WriteAllText(p, saved[p]); else File.Delete(p); } }
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }

    static void Run(string panelsFile)
    {
        Open(Aplat.UI.ThemeMode.Light, new Size(1400, 900));
        var scene = SceneSize;

        // 1. Defaults.
        Check(Cards.Count == 5, "five floating cards (tools, recent tool, objects, view, auto-rotate)");
        Check(Place(0).Anchor == DockAnchor.Top && Place(1).Anchor == DockAnchor.Left && Place(2).Anchor == DockAnchor.BottomLeft && Place(3).Anchor == DockAnchor.Bottom,
            "default anchors: tools top, recent left, objects bottom-left, view bottom");
        Check(Near(R(0).X + R(0).Width / 2, scene.Width / 2, 2) && R(0).Y == PanelDocking.Margin, "tools card is centred at the top");
        Check(R(2).X == PanelDocking.Margin && R(2).Bottom == scene.Height - PanelDocking.Margin, "objects card sits in the bottom-left corner");
        Check(!Win(4).Visible, "the auto-rotate card is hidden while the spin is off");
        Shot("01-default-light");

        // 2. Dragging the tools card to the top-left corner shows the preview, then docks.
        var o = Scene.PointToScreen(Point.Empty);
        int dx = (o.X + 24) - Win(0).Left, dy = (o.Y + 22) - Win(0).Top;
        DragCard(0, dx, dy, () =>
        {
            var prev = (Form)Get(form, "_dockPreview");
            Check(prev != null && prev.Visible, "a dock preview is shown while the card is near an edge");
            Shot("02-drag-preview-light", true);
        });
        Check(Place(0).Anchor == DockAnchor.TopLeft, "dropped near the corner → docked top-left");
        Check(R(0).X == PanelDocking.Margin && R(0).Y == PanelDocking.Margin, "...and snapped to the corner margin");
        Check(!((Form)Get(form, "_dockPreview")).Visible, "the preview is gone after the drop");
        Shot("03-docked-corner-light");

        // 3. Dropping the view card on the same corner puts it in the row, ahead of or behind the first card
        //    depending on where it lands.
        dx = (o.X + 30) - Win(3).Left; dy = (o.Y + 20) - Win(3).Top;
        DragCard(3, dx, dy);
        Check(Place(3).Anchor == DockAnchor.TopLeft && Place(0).Anchor == DockAnchor.TopLeft, "the view card docks to the same corner");
        Check(R(3).X == PanelDocking.Margin && R(0).X == R(3).Right + PanelDocking.Gap && R(0).Y == R(3).Y - (R(3).Height - R(0).Height) * 0, "...ahead of the first card, with a gap between them");
        Shot("04-stacked-light");

        // 4. A card dragged into the middle becomes free; dragging it back to an edge docks it again.
        dx = (o.X + scene.Width / 2 - 60) - Win(1).Left; dy = (o.Y + scene.Height / 2) - Win(1).Top;
        DragCard(1, dx, dy);
        Check(!Place(1).Docked, "dropped in the middle of the viewport → free (undocked)");
        var freeBefore = R(1);

        // 5. A maximise keeps docked cards anchored and free cards at the same distance from their corner.
        form.WindowState = FormWindowState.Maximized; Pump(900);
        var big = SceneSize;
        Check(big.Width > scene.Width, "the window is bigger after maximise");
        Check(R(2).X == PanelDocking.Margin && R(2).Bottom == big.Height - PanelDocking.Margin, "objects card follows the new bottom edge");
        Check(R(3).X == PanelDocking.Margin && Near(R(0).X, R(3).Right + PanelDocking.Gap) && R(0).Y == PanelDocking.Margin, "the stacked top-left cards stay together at the corner");
        var p1 = Place(1);
        int expDx = p1.FromRight ? big.Width - R(1).Right : R(1).Left, expDy = p1.FromBottom ? big.Height - R(1).Bottom : R(1).Top;
        Check(Near(expDx, p1.Dx, 2) && Near(expDy, p1.Dy, 2), "the free card keeps its distance from its corner");
        Shot("05-maximised-light");
        form.WindowState = FormWindowState.Normal; Pump(700);

        // 6. The Panels menu and dialog: one list for toolbars and cards.
        var menu = (ToolStripMenuItem)Get(form, "menuPanels");
        Call(form, "RefreshToolbarMenuChecks");
        var texts = menu.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
        Check(texts.Count >= 9, "View → Panels lists both toolbars and every card plus the dialog and reset");
        Check(menu.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.CheckOnClick || i.Checked).Count() >= 5, "...with a check mark on each shown panel");
        var timer = new Timer { Interval = 700 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            var dlg = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.GetType().Name == "PanelSettingsDialog");
            Check(dlg != null, "the Panels dialog opens");
            if (dlg != null)
            {
                Check(dlg.Controls.Find("_rows", true).Length == 1, "...and lists the panels");
                if (!string.IsNullOrEmpty(Out))
                    using (var bmp = new Bitmap(dlg.Width, dlg.Height))
                    {
                        dlg.TopMost = true; Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(dlg.Location, Point.Empty, dlg.Size);
                        bmp.Save(Path.Combine(Out, "06-panels-dialog-light.png"), ImageFormat.Png);
                    }
                dlg.Close();
            }
        };
        timer.Start();
        Call(form, "menuPanels_Click", null, EventArgs.Empty);
        timer.Dispose();

        // Hide and show a card from the list.
        Call(form, "SetOverlayShown", Cards[1], false); Pump(150);
        Check(!Win(1).Visible, "a card hidden from the Panels list disappears");
        Call(form, "SetOverlayShown", Cards[1], true); Pump(150);
        Check(Win(1).Visible, "...and comes back");

        // 7. Remembered between runs.
        var placementsBefore = Enumerable.Range(0, 4).Select(i => Place(i).Serialize()).ToArray();
        CloseForm();
        Check(File.Exists(panelsFile), "closing the window writes panels.txt");
        Open(Aplat.UI.ThemeMode.Light, new Size(1400, 900));
        var placementsAfter = Enumerable.Range(0, 4).Select(i => Place(i).Serialize()).ToArray();
        Check(placementsBefore.SequenceEqual(placementsAfter), "the next run restores every card's placement");
        Check(Place(0).Anchor == DockAnchor.TopLeft && Place(3).Anchor == DockAnchor.TopLeft && !Place(1).Docked, "...docked and free alike");

        // 8. Garbage in the settings file must not break anything.
        CloseForm();
        File.WriteAllText(panelsFile, "opacity|banana\ncard|tools|1|dock:Nowhere:x\ncard|view|1|free:TL:oops\nsomething else\ncard|objects|0|dock:Right:0\n");
        Open(Aplat.UI.ThemeMode.Light, new Size(1400, 900));
        Check(Place(0).Anchor == DockAnchor.Top, "an unreadable line keeps the default");
        Check(Place(2).Anchor == DockAnchor.Right && !Win(2).Visible, "a readable line is applied (hidden card, docked right)");
        Call(form, "ResetPanels"); Pump(300);
        Check(Place(2).Anchor == DockAnchor.BottomLeft && Win(2).Visible, "Reset puts every card back to its default place and shows it");

        // 9. Auto-rotate card.
        var V = (SceneView)Get(form, "sceneView");
        Call(form, "ToggleAutoRotate"); Pump(400);
        Check(Win(4).Visible, "switching the spin on shows the auto-rotate card");
        Check(Place(4).Anchor == DockAnchor.BottomRight, "...docked bottom-right by default");
        float a0 = V.Camera.AngleY; Pump(500);
        Check(Math.Abs(V.Camera.AngleY - a0) > 0.5f, "the camera turns while it runs");
        Shot("07-spin-card-light");
        var pause = (Button)Get(form, "btnSpinPause");
        pause.PerformClick(); Pump(150);
        Check(V.AutoRotatePaused, "Pause holds the rotation");
        float a1 = V.Camera.AngleY; Pump(400);
        Check(Near((int)(a1 * 100), (int)(V.Camera.AngleY * 100), 1), "...the camera stands still");
        Check(Win(4).Visible, "...and the card stays");
        pause.PerformClick(); Pump(150);
        Check(!V.AutoRotatePaused, "Resume continues");
        float start = (float)Get(V, "_spinStartAngle");
        ((Button)Get(form, "btnSpinStop")).PerformClick(); Pump(200);
        Check(V.AutoRotatePaused, "Stop holds the rotation too");
        Check(Math.Abs(V.Camera.AngleY - start) < 0.01f, "Stop returns to the starting angle");
        ((Button)Get(form, "btnSpinOff")).PerformClick(); Pump(300);
        Check(!V.AutoRotate && !Win(4).Visible, "Off switches the spin off and hides the card");

        // 10. Cursor and Hand tools.
        var cursor = (Button)Get(form, "btnCursor"); var hand = (Button)Get(form, "btnHand");
        hand.PerformClick(); Pump(100);
        Check(V.CurrentTool == ToolMode.Hand && !((RoundedButton)cursor).Active && ((RoundedButton)hand).Active, "Hand is active, the cursor is not");
        cursor.PerformClick(); Pump(100);
        Check(V.CurrentTool == ToolMode.Select && ((RoundedButton)cursor).Active && !((RoundedButton)hand).Active, "the Select button switches to the cursor tool");
        Check(((Control)Get(V, "_gl")).Cursor == Cursors.Default, "...with the ordinary arrow cursor");
        Check(cursor.Left < hand.Left && cursor.Parent == hand.Parent, "the cursor button sits next to the Hand in the floating bar");
        Call(form, "SetBrush", BrushKind.Brush, true); Pump(100);
        Check(V.CurrentTool == ToolMode.Paint && !((RoundedButton)cursor).Active, "picking a drawing tool leaves the cursor");
        Shot("08-tools-light");

        CloseForm();

        // Dark theme pictures.
        Open(Aplat.UI.ThemeMode.Graphite, new Size(1400, 900));
        Call(form, "ToggleAutoRotate"); Pump(400);
        Shot("09-dark-default-spin");
        DragCard(0, 0, 0);   // a click on the card's background must not undock it
        Check(Place(0).Anchor == DockAnchor.Top, "a plain click on a card does not move or undock it");
        var o2 = Scene.PointToScreen(Point.Empty);
        DragCard(3, (o2.X + SceneSize.Width - 150) - Win(3).Left, (o2.Y + 10) - Win(3).Top, () => Shot("10-dark-drag-preview", true));
        Shot("11-dark-docked-right");
        var timer2 = new Timer { Interval = 700 };
        timer2.Tick += (s, e) =>
        {
            timer2.Stop();
            var dlg = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.GetType().Name == "PanelSettingsDialog");
            if (dlg != null)
            {
                if (!string.IsNullOrEmpty(Out))
                    using (var bmp = new Bitmap(dlg.Width, dlg.Height))
                    {
                        dlg.TopMost = true; Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(dlg.Location, Point.Empty, dlg.Size);
                        bmp.Save(Path.Combine(Out, "12-panels-dialog-dark.png"), ImageFormat.Png);
                    }
                dlg.Close();
            }
        };
        timer2.Start();
        Call(form, "menuPanels_Click", null, EventArgs.Empty);
        timer2.Dispose();
        CloseForm();
    }
}
