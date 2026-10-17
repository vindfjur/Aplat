using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A vertical viewport with a thin, theme-coloured scrollbar.
    //
    // Replaces AutoScroll panels in the side panel: those showed a horizontal bar
    // (and the grey corner box between the two bars) whenever a child was a pixel
    // too wide, and their chunky OS scrollbars ignored the theme. Here the content
    // always spans the viewport's full width — horizontal scrolling can't happen —
    // and the scrollbar is an 8px strip that lives in the content's right padding.
    //
    // The content control owns its height (InspectorPanel sizes itself; a plain
    // panel gets an explicit Height). The wheel scrolls from anywhere over the
    // content, except over a focused slider / numeric / combo that uses it itself.
    public sealed class ScrollHost : Panel
    {
        private const int BarWidth = 8;
        private const int WheelStep = 48;   // px per wheel notch

        private readonly ThinScrollBar _bar;
        private Control _content;
        private int _offset;

        public ScrollHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            AutoScroll = false;
            _bar = new ThinScrollBar(this) { Visible = false };
            Controls.Add(_bar);
            BackColor = Theme.Card;
            Content = new Panel();
        }

        // The scrolled control. Its Width is managed here; its Height is its own.
        public Control Content
        {
            get { return _content; }
            set
            {
                if (value == null || ReferenceEquals(value, _content)) return;
                if (_content != null)
                {
                    _content.SizeChanged -= Content_SizeChanged;
                    Unhook(_content);
                    Controls.Remove(_content);
                }
                _content = value;
                _content.Location = Point.Empty;
                _content.BackColor = BackColor;
                Controls.Add(_content);
                _bar.BringToFront();
                _content.SizeChanged += Content_SizeChanged;
                Hook(_content);
                _offset = 0;
                PerformLayout();
            }
        }

        public int ViewportWidth  { get { return ClientSize.Width; } }
        public int ViewportHeight { get { return ClientSize.Height; } }
        public int MaxOffset      { get { return Math.Max(0, (_content != null ? _content.Height : 0) - ClientSize.Height); } }

        public int ScrollOffset
        {
            get { return _offset; }
            set
            {
                int v = Math.Max(0, Math.Min(MaxOffset, value));
                if (v == _offset) return;
                _offset = v;
                if (_content != null) _content.Top = -_offset;
                _bar.Invalidate();
            }
        }

        public void ScrollBy(int dy) { ScrollOffset = _offset + dy; }

        // Scrolls just enough to show `c` (a descendant of the content).
        public void ScrollIntoView(Control c)
        {
            if (_content == null || c == null || !_content.Contains(c)) return;
            Rectangle r = _content.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            const int margin = 12;
            if (r.Top - margin < _offset) ScrollOffset = r.Top - margin;
            else if (r.Bottom + margin > _offset + ClientSize.Height) ScrollOffset = r.Bottom + margin - ClientSize.Height;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_content == null) return;
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset));
            _content.SetBounds(0, -_offset, ClientSize.Width, _content.Height, BoundsSpecified.Location | BoundsSpecified.Width);
            _bar.SetBounds(ClientSize.Width - BarWidth - 2, 2, BarWidth, Math.Max(0, ClientSize.Height - 4));
            _bar.Visible = MaxOffset > 0;
            _bar.Invalidate();
        }

        private void Content_SizeChanged(object sender, EventArgs e)
        {
            // Height changed (content re-laid out) → re-clamp and show/hide the bar.
            if (IsHandleCreated) BeginInvoke((Action)PerformLayout);
            else PerformLayout();
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (_content != null) _content.BackColor = BackColor;
            if (_bar != null) _bar.Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(e);
        }

        // ── Wheel & focus from anywhere inside the content ────────
        private void Hook(Control c)
        {
            c.MouseWheel   += Descendant_MouseWheel;
            c.Enter        += Descendant_Enter;
            c.ControlAdded += Descendant_ControlAdded;
            foreach (Control child in c.Controls) Hook(child);
        }

        private void Unhook(Control c)
        {
            c.MouseWheel   -= Descendant_MouseWheel;
            c.Enter        -= Descendant_Enter;
            c.ControlAdded -= Descendant_ControlAdded;
            foreach (Control child in c.Controls) Unhook(child);
        }

        private void Descendant_ControlAdded(object sender, ControlEventArgs e) { Hook(e.Control); }

        private void Descendant_Enter(object sender, EventArgs e)
        {
            var c = sender as Control;
            if (c != null && !ReferenceEquals(c, _content) && c.Controls.Count == 0) ScrollIntoView(c);
        }

        private void Descendant_MouseWheel(object sender, MouseEventArgs e)
        {
            var c = sender as Control;
            // A focused value control owns the wheel (that's how you nudge a value).
            if (c != null && c.Focused && (c is ModernSlider || c is NumericUpDown || c is ComboBox)) return;
            if (c != null && c.Parent is NumericUpDown && c.Parent.ContainsFocus) return;
            Wheel(e);
        }

        private void Wheel(MouseEventArgs e)
        {
            var hme = e as HandledMouseEventArgs;
            if (hme != null && hme.Handled) return;
            if (MaxOffset > 0) ScrollBy(-e.Delta / 120 * WheelStep);
            if (hme != null) hme.Handled = true;   // don't bubble to the parent as well
        }

        // ── The scrollbar strip ───────────────────────────────────
        private sealed class ThinScrollBar : Control
        {
            private const int MinThumb = 28;
            private readonly ScrollHost _host;
            private bool _hover, _drag;
            private int _dragStartY, _dragStartOffset;

            public ThinScrollBar(ScrollHost host)
            {
                _host = host;
                SetStyle(ControlStyles.UserPaint
                       | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }

            private Rectangle Thumb
            {
                get
                {
                    int content = _host._content != null ? _host._content.Height : 0;
                    int view = _host.ClientSize.Height;
                    if (content <= view || Height <= 0) return Rectangle.Empty;
                    int h = Math.Max(MinThumb, (int)((long)Height * view / content));
                    int travel = Height - h;
                    int y = _host.MaxOffset > 0 ? (int)((long)travel * _host._offset / _host.MaxOffset) : 0;
                    int w = _hover || _drag ? 6 : 4;
                    return new Rectangle(Width - w - 1, y, w, h);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(_host.BackColor);
                var t = Thumb;
                if (t.IsEmpty) return;
                using (var b = new SolidBrush(_hover || _drag ? Theme.TextMuted : Theme.Border))
                    e.Graphics.FillRectangle(b, t);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                var t = Thumb;
                if (t.IsEmpty) return;
                if (e.Y < t.Top)         _host.ScrollBy(-_host.ClientSize.Height + 24);   // page up
                else if (e.Y > t.Bottom) _host.ScrollBy(_host.ClientSize.Height - 24);    // page down
                else
                {
                    _drag = true;
                    _dragStartY = e.Y;
                    _dragStartOffset = _host._offset;
                    Capture = true;
                }
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!_drag) return;
                var t = Thumb;
                int travel = Math.Max(1, Height - t.Height);
                _host.ScrollOffset = _dragStartOffset + (int)((long)(e.Y - _dragStartY) * _host.MaxOffset / travel);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _drag = false;
                Capture = false;
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _host.Wheel(e);
            }
        }
    }
}
