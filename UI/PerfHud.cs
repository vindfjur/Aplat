using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // The small "60 FPS | CPU 4% | 168 MB" plate in the corner of the viewport. A borderless window owned by
    // the main form that never takes focus. It can be dragged anywhere inside the viewport (AreaProvider gives
    // that rectangle in screen coordinates); a double click asks the owner to put it back in the corner.
    public sealed class PerfHud : Form
    {
        private const int PadX = 10, Height_ = 24, Radius = 8;
        private readonly Font _font = new Font("Consolas", 9f, FontStyle.Bold);
        private string[] _parts = new string[0];
        private bool _hot;
        private bool _dragging, _moved;
        private Point _dragCursor, _dragOrigin;

        // The viewport in screen coordinates; the plate is kept inside it.
        public Func<Rectangle> AreaProvider { get; set; }
        // Raised once when a drag ends where the plate was actually moved.
        public event EventHandler Dragged;
        // Double click: the owner restores the default corner.
        public event EventHandler ResetRequested;

        public PerfHud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            AutoScaleMode   = AutoScaleMode.None;
            DoubleBuffered  = true;
            Opacity         = 0.92;
            Size            = new Size(200, Height_);
            Cursor          = Cursors.SizeAll;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;   // off Alt-Tab, no focus
                return cp;
            }
        }

        // A click or drag on the plate must not pull the focus away from the viewport (the tool keys keep working).
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // `p` moved the least that puts the whole plate inside `area`.
        public static Point ClampTo(Point p, Size size, Rectangle area)
        {
            int x = Math.Max(area.Left, Math.Min(area.Right - size.Width, p.X));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, p.Y));
            return new Point(x, y);
        }

        public void ClampToArea()
        {
            if (AreaProvider == null) return;
            Point c = ClampTo(Location, Size, AreaProvider());
            if (c != Location) Location = c;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true; _moved = false;
            _dragCursor = PointToScreen(e.Location); _dragOrigin = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Point now = PointToScreen(e.Location);   // not Cursor.Position: the same value, but testable with posted messages
            var target = new Point(_dragOrigin.X + now.X - _dragCursor.X, _dragOrigin.Y + now.Y - _dragCursor.Y);
            if (AreaProvider != null) target = ClampTo(target, Size, AreaProvider());
            if (target != Location) { Location = target; _moved = true; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            if (_moved) { _moved = false; var h = Dragged; if (h != null) h(this, EventArgs.Empty); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var h = ResetRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        // fps null = nothing was drawn in the last interval (the view is at rest). `hot` colours the CPU value.
        public void SetValues(int? fps, int cpuPercent, int memoryMb, bool hot)
        {
            _parts = new[]
            {
                fps.HasValue ? fps.Value + " FPS" : "– FPS",
                "CPU " + cpuPercent + "%",
                memoryMb + " MB",
            };
            _hot = hot;
            int w = PadX * 2 + 2 * 20;   // room for the two separators
            foreach (var p in _parts) w += TextRenderer.MeasureText(p, _font, new Size(400, 40), TextFormatFlags.NoPadding).Width;
            if (w != Width) { Width = w; UpdateRegion(); if (!_dragging) ClampToArea(); }
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRegion(); }

        private void UpdateRegion()
        {
            using (var path = RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
                Region = new Region(path);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Card);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int x = PadX;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (i > 0)
                {
                    TextRenderer.DrawText(e.Graphics, "|", _font, new Point(x + 6, 5), Theme.TextMuted, TextFormatFlags.NoPadding);
                    x += 20;
                }
                var color = i == 1 && _hot ? Color.FromArgb(0xD8, 0x5A, 0x30) : Theme.Text;
                var size = TextRenderer.MeasureText(_parts[i], _font, new Size(400, 40), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, _parts[i], _font, new Point(x, 5), color, TextFormatFlags.NoPadding);
                x += size.Width;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
