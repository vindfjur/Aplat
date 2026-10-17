using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Visual-Studio-style toolbars: dock them to any window edge (drag by the grip),
    // reorder rows, float them in a small tool window, dock back by double-clicking
    // its caption or dropping it on an edge. The layout persists between runs; the
    // default layout is the familiar one (both bars across the top).
    //
    // Toolbars change orientation with the edge: on the left/right they turn
    // vertical and show icons only (text items fall back to their Tag icon).
    public sealed class ToolbarDock
    {
        private sealed class Bar
        {
            public ToolStrip Strip;
            public string    Key, Title;
            public int       DefaultRow;
            public int       Thickness;            // row height when horizontal / column width when vertical
            public Padding   HPadding;
            public FloatWindow Float;
            public DockStyle LastEdge = DockStyle.Top;
            public readonly Dictionary<ToolStripItem, ToolStripItemDisplayStyle> Display =
                new Dictionary<ToolStripItem, ToolStripItemDisplayStyle>();
        }

        private readonly Form _host;
        private readonly List<Bar> _bars = new List<Bar>();
        private readonly ToolStripRenderer _renderer;

        public ToolStripPanel Top    { get; private set; }
        public ToolStripPanel Bottom { get; private set; }
        public ToolStripPanel Left   { get; private set; }
        public ToolStripPanel Right  { get; private set; }

        public ToolbarDock(Form host, ToolStripRenderer renderer)
        {
            _host = host;
            _renderer = renderer;
            Top    = MakePanel(DockStyle.Top,    Orientation.Horizontal);
            Bottom = MakePanel(DockStyle.Bottom, Orientation.Horizontal);
            Left   = MakePanel(DockStyle.Left,   Orientation.Vertical);
            Right  = MakePanel(DockStyle.Right,  Orientation.Vertical);
        }

        public bool OwnsWindow(Form window)
        {
            return _bars.Exists(bar => bar.Float == window);
        }

        private ToolStripPanel MakePanel(DockStyle dock, Orientation o)
        {
            return new ToolStripPanel
            {
                Dock        = dock,
                Orientation = o,
                RenderMode  = ToolStripRenderMode.Professional,
                Renderer    = _renderer,
                BackColor   = Theme.Card,
                RowMargin   = new Padding(0)
            };
        }

        private ToolStripPanel PanelFor(DockStyle edge)
        {
            switch (edge)
            {
                case DockStyle.Bottom: return Bottom;
                case DockStyle.Left:   return Left;
                case DockStyle.Right:  return Right;
                default:               return Top;
            }
        }

        private DockStyle EdgeOf(ToolStrip strip)
        {
            var p = strip.Parent as ToolStripPanel;
            if (p == null) return DockStyle.None;
            return p == Bottom ? DockStyle.Bottom : p == Left ? DockStyle.Left : p == Right ? DockStyle.Right : DockStyle.Top;
        }

        // ── Registration ──────────────────────────────────────────
        public void Register(ToolStrip strip, string key, string title, int defaultRow)
        {
            var bar = new Bar
            {
                Strip = strip, Key = key, Title = title, DefaultRow = defaultRow,
                Thickness = strip.Height, HPadding = strip.Padding
            };
            foreach (ToolStripItem it in strip.Items) bar.Display[it] = it.DisplayStyle;
            _bars.Add(bar);

            strip.Name       = key;
            strip.Text       = title;
            strip.GripStyle  = ToolStripGripStyle.Visible;
            strip.Stretch    = true;
            strip.AutoSize   = false;
            strip.LayoutStyleChanged += (s, e) => ApplyOrientation(bar);
            strip.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowMenu(bar, e.Location); };

            Top.Join(strip, defaultRow);
            ApplyOrientation(bar);
        }

        // Horizontal: the designed look (text / icon+text). Vertical: icons only,
        // a column as wide as the row was tall.
        private void ApplyOrientation(Bar bar)
        {
            var s = bar.Strip;
            bool vertical = s.Orientation == Orientation.Vertical;
            s.SuspendLayout();
            foreach (ToolStripItem it in s.Items)
            {
                if (!bar.Display.ContainsKey(it)) continue;
                it.DisplayStyle = vertical && it.Image != null ? ToolStripItemDisplayStyle.Image : bar.Display[it];
            }
            if (vertical)
            {
                s.Padding = new Padding(bar.HPadding.Top, 8, bar.HPadding.Bottom, 8);
                s.Width   = bar.Thickness;
            }
            else
            {
                s.Padding = bar.HPadding;
                s.Height  = bar.Thickness;
            }
            s.ResumeLayout(true);
        }

        // ── Position as one list index (for the Panels dialog) ────
        // 0 top, 1 bottom, 2 left, 3 right, 4 floating window.
        public int PositionIndex(ToolStrip strip)
        {
            var bar = _bars.Find(b => b.Strip == strip);
            if (bar == null || bar.Float != null) return 4;
            switch (EdgeOf(strip))
            {
                case DockStyle.Bottom: return 1;
                case DockStyle.Left:   return 2;
                case DockStyle.Right:  return 3;
                default:               return 0;
            }
        }

        public void SetPosition(ToolStrip strip, int index)
        {
            if (index == 4) { Float(strip); return; }
            DockStyle edge = index == 1 ? DockStyle.Bottom : index == 2 ? DockStyle.Left : index == 3 ? DockStyle.Right : DockStyle.Top;
            DockTo(strip, edge);
        }

        // ── Dock / float ──────────────────────────────────────────
        public void DockTo(ToolStrip strip, DockStyle edge)
        {
            var bar = _bars.Find(b => b.Strip == strip);
            if (bar == null) return;
            Detach(bar);
            strip.Dock = DockStyle.None;
            strip.Stretch = true;
            var panel = PanelFor(edge);
            panel.Join(strip, panel.Rows.Length);
            bar.LastEdge = edge;
            ApplyOrientation(bar);
        }

        public void Float(ToolStrip strip, Point? screenLocation = null)
        {
            var bar = _bars.Find(b => b.Strip == strip);
            if (bar == null || bar.Float != null) return;
            DockStyle edge = EdgeOf(strip);
            if (edge != DockStyle.None) bar.LastEdge = edge;
            Point at = screenLocation ?? strip.PointToScreen(new Point(0, 0));
            if (strip.Parent != null) strip.Parent.Controls.Remove(strip);

            var win = new FloatWindow(bar.Title) { Owner = _host, StartPosition = FormStartPosition.Manual };
            strip.Stretch = false;
            strip.Dock    = DockStyle.Fill;
            win.Controls.Add(strip);
            bar.Float = win;
            ApplyOrientation(bar);   // floating bars are horizontal
            int width = Math.Max(160, strip.GetPreferredSize(Size.Empty).Width + 8);
            win.ClientSize = new Size(width, bar.Thickness);
            win.Location = at;
            win.DockBackRequested += (s, e) => DockTo(strip, bar.LastEdge);
            win.Dropped += (s, e) => TryDockAtCursor(bar);
            ThemeManager.Attach(win);
            if (_host.Visible) win.Show();
        }

        // A floating bar released over a window edge docks there (VS behaviour).
        private void TryDockAtCursor(Bar bar)
        {
            Point p = _host.PointToClient(Control.MousePosition);
            Rectangle client = _host.ClientRectangle;
            if (!client.Contains(p)) return;
            const int zone = 48;
            int top = Top.Bottom, bottom = Bottom.Top;
            if (p.Y <= top + zone)                 DockTo(bar.Strip, DockStyle.Top);
            else if (p.Y >= bottom - zone)         DockTo(bar.Strip, DockStyle.Bottom);
            else if (p.X <= Left.Right + zone)     DockTo(bar.Strip, DockStyle.Left);
            else if (p.X >= Right.Left - zone)     DockTo(bar.Strip, DockStyle.Right);
        }

        // Takes a bar off wherever it is (panel row or floating window).
        private void Detach(Bar bar)
        {
            if (bar.Float != null)
            {
                var f = bar.Float;
                bar.Float = null;
                f.Controls.Remove(bar.Strip);
                f.AllowClose = true;
                f.Close();
                f.Dispose();
            }
            else if (bar.Strip.Parent != null) bar.Strip.Parent.Controls.Remove(bar.Strip);
        }

        // Back to the familiar layout: every bar visible, across the top, in order.
        public void ResetLayout()
        {
            foreach (var bar in _bars) Detach(bar);
            var ordered = new List<Bar>(_bars);
            ordered.Sort((a, b) => a.DefaultRow.CompareTo(b.DefaultRow));
            foreach (var bar in ordered)
            {
                bar.Strip.Visible = true;
                bar.Strip.Dock    = DockStyle.None;
                bar.Strip.Stretch = true;
                Top.Join(bar.Strip, bar.DefaultRow);
                bar.LastEdge = DockStyle.Top;
                ApplyOrientation(bar);
            }
        }

        public void SetVisible(ToolStrip strip, bool visible)
        {
            var bar = _bars.Find(b => b.Strip == strip);
            if (bar == null) return;
            strip.Visible = visible;
            if (bar.Float != null) bar.Float.Visible = visible && _host.Visible;
        }

        // Shows floating windows once the host is on screen (they are owned windows).
        public void ShowFloats()
        {
            foreach (var bar in _bars)
                if (bar.Float != null && bar.Strip.Visible && !bar.Float.Visible) bar.Float.Show();
        }

        // ── Context menu ──────────────────────────────────────────
        private void ShowMenu(Bar bar, Point at)
        {
            var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional, Renderer = _renderer, Font = Theme.Font(9.25f) };
            if (bar.Float != null)
                menu.Items.Add(Loc.T("Закрепить"), null, (s, e) => DockTo(bar.Strip, bar.LastEdge));
            else
                menu.Items.Add(Loc.T("Открепить в окно"), null, (s, e) =>
                    Float(bar.Strip, bar.Strip.PointToScreen(new Point(24, 24))));
            menu.Items.Add(new ToolStripSeparator());
            AddEdge(menu, bar, Loc.T("Закрепить сверху"), DockStyle.Top);
            AddEdge(menu, bar, Loc.T("Закрепить слева"),  DockStyle.Left);
            AddEdge(menu, bar, Loc.T("Закрепить справа"), DockStyle.Right);
            AddEdge(menu, bar, Loc.T("Закрепить снизу"),  DockStyle.Bottom);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("Сбросить расположение панелей"), null, (s, e) => ResetLayout());
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            menu.Show(bar.Strip, at);
        }

        private void AddEdge(ContextMenuStrip menu, Bar bar, string text, DockStyle edge)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => DockTo(bar.Strip, edge))
            {
                Checked = bar.Float == null && EdgeOf(bar.Strip) == edge
            };
            menu.Items.Add(item);
        }

        // ── Persistence: key|state|edge|x|y|visible  (state = dock / float) ──
        public void Save(string path)
        {
            try
            {
                var lines = new List<string>();
                foreach (var bar in _bars)
                {
                    bool floating = bar.Float != null;
                    Point loc = floating ? bar.Float.Location : bar.Strip.Location;
                    DockStyle edge = floating ? bar.LastEdge : EdgeOf(bar.Strip);
                    lines.Add(string.Join("|", bar.Key, floating ? "float" : "dock", edge.ToString(),
                        loc.X.ToString(CultureInfo.InvariantCulture), loc.Y.ToString(CultureInfo.InvariantCulture),
                        bar.Strip.Visible ? "1" : "0"));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        public void Load(string path)
        {
            string[] lines;
            try { if (!File.Exists(path)) return; lines = File.ReadAllLines(path); }
            catch { return; }

            foreach (var line in lines)
            {
                var f = line.Split('|');
                if (f.Length < 6) continue;
                var bar = _bars.Find(b => b.Key == f[0]);
                if (bar == null) continue;
                DockStyle edge;
                if (!Enum.TryParse(f[2], out edge) || edge == DockStyle.None || edge == DockStyle.Fill) edge = DockStyle.Top;
                int x, y;
                int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);

                if (f[1] == "float")
                {
                    bar.LastEdge = edge;
                    Float(bar.Strip, OnScreen(new Point(x, y)));
                }
                else
                {
                    if (bar.Strip.Parent != null) bar.Strip.Parent.Controls.Remove(bar.Strip);
                    PanelFor(edge).Join(bar.Strip, new Point(Math.Max(0, x), Math.Max(0, y)));
                    bar.LastEdge = edge;
                    ApplyOrientation(bar);
                }
                SetVisible(bar.Strip, f[5] != "0");
            }
        }

        // A saved floating position on a monitor that is no longer there comes back on screen.
        private static Point OnScreen(Point p)
        {
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(p)) return p;
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Left + 80, wa.Top + 80);
        }

        // ── The floating tool window ──────────────────────────────
        private sealed class FloatWindow : Form
        {
            public bool AllowClose;
            public event EventHandler DockBackRequested;
            public event EventHandler Dropped;

            public FloatWindow(string title)
            {
                Text            = title;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar   = false;
                MaximizeBox     = false;
                MinimizeBox     = false;
                BackColor       = Theme.Card;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void WndProc(ref Message m)
            {
                const int WM_NCLBUTTONDBLCLK = 0x00A3;
                if (m.Msg == WM_NCLBUTTONDBLCLK)
                {
                    var h = DockBackRequested;          // double-click the caption → dock back
                    if (h != null) { BeginInvoke((Action)(() => h(this, EventArgs.Empty))); return; }
                }
                base.WndProc(ref m);
            }

            protected override void OnResizeEnd(EventArgs e)
            {
                base.OnResizeEnd(e);                    // end of a caption drag
                var h = Dropped;
                if (h != null) BeginInvoke((Action)(() => h(this, EventArgs.Empty)));
            }

            // Closing the window (its ×) docks the bar back instead of losing it; when
            // the main window closes, owned windows get FormOwnerClosing and just go.
            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    var h = DockBackRequested;
                    if (h != null) BeginInvoke((Action)(() => h(this, EventArgs.Empty)));
                    return;
                }
                base.OnFormClosing(e);
            }
        }
    }
}
