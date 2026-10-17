using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    public sealed class OverlayDragEventArgs : EventArgs
    {
        public Point Location;   // where the window is about to be put; the host may change it
        public OverlayDragEventArgs(Point location) { Location = location; }
    }

    // A borderless, rounded, slightly translucent HUD window that floats over the
    // scene. Owned by the main form, so it stays above it, minimises/closes with it
    // and never shows up on its own in Alt-Tab.
    //
    // Real translucency comes from Form.Opacity (a layered window) — the canvas shows
    // through, which a child Panel can't do over the GL surface. The window is a fixed
    // size (no user resize, so it can never be squashed into a clipped state); the user
    // can drag it around by grabbing the background / header — buttons keep their own
    // clicks because WinForms mouse events don't bubble.
    public sealed class OverlayWindow : Form
    {
        private bool  _dragging;
        private Point _dragOffset;          // cursor offset from the window's top-left
        private int _radius = Theme.RadiusCard;

        // Drag lifecycle, so the host can snap the card to edges / other cards and show a dock preview.
        // Dragging lets the host replace the location the mouse proposes (magnetism); DragEnded is raised
        // once on release, but only if the card really moved (a plain click is not a drag).
        public event EventHandler DragStarted;
        public event EventHandler<OverlayDragEventArgs> Dragging;
        public event EventHandler DragEnded;
        // Right-click on a drag handle: the host shows its dock menu at that screen point.
        public event EventHandler<Point> ContextRequested;

        private const int DragThreshold = 4;     // pixels the mouse must travel before a press becomes a drag
        private Point _pressScreen;
        private bool  _pressed;

        public OverlayWindow(Size size)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            AutoScaleMode   = AutoScaleMode.None;
            BackColor       = Theme.Card;
            DoubleBuffered  = true;
            Padding         = new Padding(1);   // hairline border ring
            Size            = size;
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; UpdateRegion(); Invalidate(); }
        }

        // Don't steal focus from the main form when first shown.
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int CS_DROPSHADOW    = 0x00020000;
                var cp = base.CreateParams;
                cp.ExStyle    |= WS_EX_TOOLWINDOW;   // keep it out of Alt-Tab
                cp.ClassStyle |= CS_DROPSHADOW;      // soft system shadow: the card floats
                return cp;
            }
        }

        // Hosts a single content control filling the window.
        public void SetContent(Control content)
        {
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
        }

        // Registers a control as a drag handle (a grab area for moving the window, right-click for the dock menu).
        public void RegisterDragHandle(Control c)
        {
            c.MouseDown += Handle_MouseDown;
            c.MouseMove += Handle_MouseMove;
            c.MouseUp   += Handle_MouseUp;
        }

        private void Handle_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _pressed = true;
            _dragging = false;
            Point screen = ((Control)sender).PointToScreen(e.Location);
            _pressScreen = screen;
            _dragOffset = new Point(screen.X - Left, screen.Y - Top);
        }

        private void Handle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            Point screen = ((Control)sender).PointToScreen(e.Location);
            if (!_dragging)
            {
                if (Math.Abs(screen.X - _pressScreen.X) < DragThreshold && Math.Abs(screen.Y - _pressScreen.Y) < DragThreshold)
                    return;
                _dragging = true;
                var started = DragStarted;
                if (started != null) started(this, EventArgs.Empty);
            }
            var args = new OverlayDragEventArgs(new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y));
            var moving = Dragging;
            if (moving != null) moving(this, args);
            Location = args.Location;
        }

        private void Handle_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var ask = ContextRequested;
                if (ask != null) ask(this, ((Control)sender).PointToScreen(e.Location));
                return;
            }
            if (!_pressed) return;
            bool wasDragging = _dragging;
            _pressed = false;
            _dragging = false;
            if (!wasDragging) return;
            var h = DragEnded;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), _radius))
                Region = new Region(path);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Rounded card fill + hairline border (only visible in the 1px padding ring,
            // since the docked content covers the centre).
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;   // sharp 1px rectangle edges
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(rect, _radius))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
        }
    }
}
