using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Owner-drawn horizontal slider: a hairline track, an accent fill up to the value
    // and a round white knob. Replaces the native TrackBar, whose chunky OS thumb
    // and grey groove were the most dated-looking part of the panels.
    //
    // API-compatible with the TrackBar subset the app uses (Minimum, Maximum, Value,
    // SmallChange, LargeChange, TickStyle, TickFrequency, Scroll), so swapping the
    // field type is the whole migration. Semantics follow TrackBar: Scroll fires only
    // for user input (mouse, wheel, keys); ValueChanged fires for any change.
    [DefaultEvent("Scroll")]
    public sealed class ModernSlider : Control
    {
        private const int TrackThickness = 3;
        private const int KnobSize = 14;
        private const int Inset = KnobSize / 2 + 2;   // knob never clips at the ends

        private int _min;
        private int _max = 10;
        private int _value;
        private int _small = 1;
        private int _large = 5;
        private bool _hover;
        private bool _dragging;

        public event EventHandler Scroll;
        public event EventHandler ValueChanged;

        public ModernSlider()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Card;
            Cursor    = Cursors.Hand;
            TabStop   = true;
            Size      = new Size(104, 26);
        }

        [DefaultValue(0)]
        public int Minimum
        {
            get { return _min; }
            set
            {
                if (_min == value) return;
                _min = value;
                if (_max < _min) _max = _min;
                SetValueCore(_value, false);
                Invalidate();
            }
        }

        [DefaultValue(10)]
        public int Maximum
        {
            get { return _max; }
            set
            {
                if (_max == value) return;
                _max = value;
                if (_min > _max) _min = _max;
                SetValueCore(_value, false);
                Invalidate();
            }
        }

        // Out-of-range values are clamped rather than thrown on: a slider mirroring a
        // model value must never crash the UI because the model drifted past a limit.
        [DefaultValue(0)]
        public int Value
        {
            get { return _value; }
            set { SetValueCore(value, false); }
        }

        [DefaultValue(1)]
        public int SmallChange
        {
            get { return _small; }
            set { _small = Math.Max(0, value); }
        }

        [DefaultValue(5)]
        public int LargeChange
        {
            get { return _large; }
            set { _large = Math.Max(0, value); }
        }

        // Accepted for TrackBar source compatibility; the slider draws no ticks.
        [Browsable(false), DefaultValue(TickStyle.None)]
        public TickStyle TickStyle { get; set; }

        [Browsable(false), DefaultValue(1)]
        public int TickFrequency { get; set; } = 1;

        private void SetValueCore(int v, bool fromUser)
        {
            v = Math.Max(_min, Math.Min(_max, v));
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            if (fromUser) Scroll?.Invoke(this, EventArgs.Empty);
        }

        // ── Geometry ──────────────────────────────────────────────
        private int TrackLeft  { get { return Inset; } }
        private int TrackRight { get { return Math.Max(Inset + 1, Width - Inset); } }

        private int ValueToX(int v)
        {
            if (_max <= _min) return TrackLeft;
            return TrackLeft + (int)Math.Round((TrackRight - TrackLeft) * (double)(v - _min) / (_max - _min));
        }

        private int XToValue(int x)
        {
            if (TrackRight <= TrackLeft || _max <= _min) return _min;
            double t = (x - TrackLeft) / (double)(TrackRight - TrackLeft);
            t = Math.Max(0, Math.Min(1, t));
            return _min + (int)Math.Round(t * (_max - _min));
        }

        // ── Painting ──────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor.A == 255 ? BackColor : (Parent != null ? Parent.BackColor : Theme.Card));

            int cy = Height / 2;
            int kx = ValueToX(_value);
            bool on = Enabled;

            // Track + filled part: sharp rectangles, in keeping with the flat chrome.
            // A range spanning zero (rotation −180…180) fills from the zero point, so
            // "0°" reads as neutral rather than as a half-full bar.
            g.SmoothingMode = SmoothingMode.None;
            var track = new Rectangle(TrackLeft, cy - TrackThickness / 2, TrackRight - TrackLeft, TrackThickness);
            using (var b = new SolidBrush(Theme.SliderTrack)) g.FillRectangle(b, track);
            int origin = _min < 0 && _max > 0 ? ValueToX(0) : TrackLeft;
            int x0 = Math.Min(origin, kx), x1 = Math.Max(origin, kx);
            if (x1 > x0)
                using (var b = new SolidBrush(on ? Theme.Accent : Theme.TextMuted))
                    g.FillRectangle(b, new Rectangle(x0, track.Y, x1 - x0, TrackThickness));

            // Knob: a round cap, soft drop shadow, hairline ring; the ring turns accent
            // on hover / drag / keyboard focus so the grabbed state is unmistakable.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var knob = new RectangleF(kx - KnobSize / 2f, cy - KnobSize / 2f, KnobSize, KnobSize);
            using (var shadow = new SolidBrush(Color.FromArgb(on ? (Theme.IsDark ? 90 : 34) : 14, Theme.Shadow)))
                g.FillEllipse(shadow, knob.X, knob.Y + 1.2f, knob.Width, knob.Height);
            using (var b = new SolidBrush(Theme.SliderKnob)) g.FillEllipse(b, knob);

            bool hot = on && (_hover || _dragging || (Focused && ShowFocusCues));
            using (var pen = new Pen(hot ? Theme.Accent : Theme.Border, hot ? 1.6f : 1f))
                g.DrawEllipse(pen, knob.X + 0.5f, knob.Y + 0.5f, knob.Width - 1f, knob.Height - 1f);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ── Mouse ─────────────────────────────────────────────────
        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (CanFocus) Focus();
            _dragging = true;
            Capture = true;
            SetValueCore(XToValue(e.X), true);   // click-to-jump, then drag from there
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetValueCore(XToValue(e.X), true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_dragging && !Capture) { _dragging = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Only a focused slider takes the wheel; otherwise the wheel scrolls the
            // panel it sits in (see ScrollHost) instead of nudging values in passing.
            if (!Enabled || e.Delta == 0 || !Focused) return;
            int step = Math.Max(1, _small);
            SetValueCore(_value + Math.Sign(e.Delta) * step, true);
            var hme = e as HandledMouseEventArgs;
            if (hme != null) hme.Handled = true;   // don't also scroll the parent panel
        }

        // ── Keyboard (same keys as TrackBar) ──────────────────────
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            int small = Math.Max(1, _small), large = Math.Max(1, _large);
            switch (e.KeyCode)
            {
                case Keys.Left:     case Keys.Down: SetValueCore(_value - small, true); break;
                case Keys.Right:    case Keys.Up:   SetValueCore(_value + small, true); break;
                case Keys.PageDown: SetValueCore(_value - large, true); break;
                case Keys.PageUp:   SetValueCore(_value + large, true); break;
                case Keys.Home:     SetValueCore(_min, true); break;
                case Keys.End:      SetValueCore(_max, true); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
