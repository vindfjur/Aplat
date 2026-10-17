using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Blender / Figma style number field: [X  512 px]
    //   drag left/right   change the value (Shift = fine, Ctrl = snap to SnapStep)
    //   click             type a value — simple arithmetic works: 512+32, 90/2
    //   wheel, ↑ ↓        step by Increment while the field has focus
    // Enter commits, Esc cancels. The API mirrors the bits of NumericUpDown the
    // inspector uses (Value/Minimum/Maximum/DecimalPlaces/Increment/ValueChanged),
    // so it drops in without a slider beside it.
    public sealed class DragNumber : Control
    {
        private const int DragThreshold = 3;
        private const int Pad = 8;
        private const int GestureIdleMs = 700;

        private decimal _value, _min = -100m, _max = 100m, _increment = 1m;
        private int _decimals;
        private string _caption = "", _suffix = "";
        private Color _captionColor = Color.Empty;

        private bool _pressed, _dragging, _hover;
        private Point _pressAt;
        private decimal _dragOrigin;
        private TextBox _editor;
        private DateTime _lastStep = DateTime.MinValue;

        public event EventHandler ValueChanged;
        // Raised once at the start of each user gesture (drag, typed commit, a burst
        // of wheel/arrow steps) — the host records one undo step per gesture.
        public event EventHandler EditStarting;

        public DragNumber()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable, true);
            Cursor = Cursors.SizeWE;
            Size = new Size(80, 26);
            DragStep = 1m;
        }

        // ── NumericUpDown-compatible surface ──────────────────────────
        public decimal Minimum { get { return _min; } set { _min = value; if (_max < _min) _max = _min; Value = _value; } }
        public decimal Maximum { get { return _max; } set { _max = value; if (_min > _max) _min = _max; Value = _value; } }
        public decimal Increment { get { return _increment; } set { _increment = value > 0 ? value : 1m; } }
        public int DecimalPlaces { get { return _decimals; } set { _decimals = Math.Max(0, Math.Min(6, value)); Value = _value; Invalidate(); } }

        public decimal Value
        {
            get { return _value; }
            set
            {
                decimal v = Math.Round(Clamp(value), _decimals, MidpointRounding.AwayFromZero);
                if (v == _value) return;
                _value = v;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Look & feel ───────────────────────────────────────────────
        public string Caption { get { return _caption; } set { _caption = value ?? ""; Invalidate(); } }
        public Color CaptionColor { get { return _captionColor; } set { _captionColor = value; Invalidate(); } }
        public string Suffix { get { return _suffix; } set { _suffix = value ?? ""; Invalidate(); } }
        public decimal DragStep { get; set; }   // value per pixel of drag
        public decimal SnapStep { get; set; }   // Ctrl-drag grid; 0 = Increment

        private decimal Clamp(decimal v) { return v < _min ? _min : (v > _max ? _max : v); }

        private string Format(decimal v)
        {
            return v.ToString("F" + _decimals, CultureInfo.CurrentCulture);
        }

        // ── Paint ─────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card))
                g.FillRectangle(bg, ClientRectangle);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool active = _dragging || Focused || _editor != null;
            Color fill = !Enabled ? Theme.BtnFace : (_hover && !active ? Theme.Shade(Theme.Input, Theme.IsDark ? 8 : -4) : Theme.Input);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            using (var p = new Pen(active ? Theme.Accent : (_hover ? Theme.Border : Theme.BorderSoft)))
                g.DrawRectangle(p, r);

            // Caption and value share one optical centre line; the 8px side insets are
            // measured to the ink, so "px", "°" and "%" end at the same place.
            int left = Pad;
            if (_caption.Length > 0)
            {
                using (var cf = new Font(Font, FontStyle.Bold))
                {
                    Color cc = !Enabled ? Theme.TextDisabled : (_captionColor.IsEmpty ? Theme.TextSoft : _captionColor);
                    int cw = TextKit.InkWidth(_caption, cf);
                    TextKit.Draw(g, _caption, cf, cc, new Rectangle(left, 0, cw, Height));
                    left += cw + 6;
                }
            }
            if (_editor != null) return;

            // Value right-aligned, unit in a quieter tone after it.
            int right = Width - Pad;
            if (_suffix.Length > 0)
            {
                int sw = TextKit.InkWidth(_suffix, Font);
                right -= sw;
                TextKit.Draw(g, _suffix, Font, Enabled ? Theme.TextMuted : Theme.TextDisabled, new Rectangle(right, 0, sw, Height));
                // "512 px", "100 %" — a word space; "90°" — set tight, as typeset.
                right -= _suffix[0] == '°' ? 1 : 5;
            }
            TextKit.Draw(g, Format(_value), Font, Enabled ? Theme.Text : Theme.TextDisabled,
                new Rectangle(left, 0, Math.Max(0, right - left), Height), HAlign.Right, ellipsis: true);
        }

        // ── Drag / click ──────────────────────────────────────────────
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _editor != null) return;
            Focus();
            _pressed = true;
            _dragging = false;
            _pressAt = e.Location;
            _dragOrigin = _value;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressed) return;
            int dx = e.X - _pressAt.X;
            if (!_dragging)
            {
                if (Math.Abs(dx) < DragThreshold) return;
                _dragging = true;
                EditStarting?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }

            decimal step = DragStep > 0 ? DragStep : _increment;
            if ((ModifierKeys & Keys.Shift) != 0) step /= 10m;
            decimal v = _dragOrigin + dx * step;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                decimal snap = SnapStep > 0 ? SnapStep : _increment;
                v = Math.Round(v / snap, MidpointRounding.AwayFromZero) * snap;
            }
            Value = v;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_pressed) return;
            bool wasDrag = _dragging;
            _pressed = _dragging = false;
            Invalidate();
            if (!wasDrag && e.Button == MouseButtons.Left) BeginEdit();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _pressed) { _pressed = _dragging = false; Invalidate(); }
        }

        // ── Wheel / keys (only with focus, so scrolling the panel never edits) ──
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Focused || _editor != null) { base.OnMouseWheel(e); return; }
            Step(e.Delta > 0 ? 1 : -1);
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        // Keys a focused field handles itself; hosts with global shortcuts let them through.
        public static bool OwnsKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) != 0) return false;
            return k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.F2
                || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9)
                || k == Keys.OemMinus || k == Keys.Subtract;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up)   { Step(+1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F2) { BeginEdit(); e.Handled = true; }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            // Typing a digit on a focused field starts editing with that digit.
            if (char.IsDigit(e.KeyChar) || e.KeyChar == '-')
            {
                BeginEdit(e.KeyChar.ToString());
                e.Handled = true;
            }
        }

        private void Step(int dir)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastStep).TotalMilliseconds > GestureIdleMs)
                EditStarting?.Invoke(this, EventArgs.Empty);
            _lastStep = now;
            decimal inc = _increment;
            if ((ModifierKeys & Keys.Shift) != 0) inc /= 10m;
            Value = _value + dir * inc;
        }

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) EndEdit(false);
            Invalidate();
        }

        // ── Inline text entry ─────────────────────────────────────────
        private void BeginEdit(string initial = null)
        {
            if (_editor != null || !Enabled) return;
            int left = Pad;
            if (_caption.Length > 0)
                using (var cf = new Font(Font, FontStyle.Bold))
                    left += TextKit.InkWidth(_caption, cf) + 6;

            _editor = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor   = Theme.Input,
                ForeColor   = Theme.Text,
                Font        = Font,
                TextAlign   = HorizontalAlignment.Right,
                Text        = initial ?? Format(_value),
            };
            int h = _editor.PreferredHeight;
            _editor.SetBounds(left, (Height - h) / 2, Math.Max(20, Width - left - Pad), h);
            _editor.KeyDown   += Editor_KeyDown;
            _editor.LostFocus += Editor_LostFocus;
            Controls.Add(_editor);
            _editor.Focus();
            if (initial == null) _editor.SelectAll();
            else _editor.Select(_editor.Text.Length, 0);
            Cursor = Cursors.IBeam;
            Invalidate();
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)  { e.SuppressKeyPress = true; EndEdit(true);  Focus(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(false); Focus(); }
        }

        private void Editor_LostFocus(object sender, EventArgs e)
        {
            // Deferred: removing a control inside its own focus change confuses WinForms.
            if (IsHandleCreated) BeginInvoke(new Action(() => EndEdit(true)));
        }

        private void EndEdit(bool commit)
        {
            var ed = _editor;
            if (ed == null) return;
            _editor = null;
            string text = ed.Text;
            ed.KeyDown   -= Editor_KeyDown;
            ed.LostFocus -= Editor_LostFocus;
            Controls.Remove(ed);
            ed.Dispose();
            Cursor = Cursors.SizeWE;

            decimal parsed;
            if (commit && Expression.TryEvaluate(text, out parsed))
            {
                decimal next = Math.Round(Clamp(parsed), _decimals, MidpointRounding.AwayFromZero);
                if (next != _value)
                {
                    EditStarting?.Invoke(this, EventArgs.Empty);
                    Value = next;
                }
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) EndEdit(false);
            base.Dispose(disposing);
        }

        // Tiny arithmetic evaluator: numbers (',' or '.' as the decimal mark),
        // + - * / and parentheses. No identifiers, no allocation-heavy parsing.
        private static class Expression
        {
            public static bool TryEvaluate(string text, out decimal result)
            {
                result = 0m;
                if (string.IsNullOrWhiteSpace(text)) return false;
                int pos = 0;
                string s = text.Replace(" ", "").Replace('−', '-').Replace(',', '.');
                try
                {
                    decimal v;
                    if (!Sum(s, ref pos, out v) || pos != s.Length) return false;
                    result = v;
                    return true;
                }
                catch (DivideByZeroException) { return false; }
                catch (OverflowException) { return false; }
            }

            private static bool Sum(string s, ref int pos, out decimal v)
            {
                if (!Product(s, ref pos, out v)) return false;
                while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
                {
                    char op = s[pos++];
                    decimal rhs;
                    if (!Product(s, ref pos, out rhs)) return false;
                    v = op == '+' ? v + rhs : v - rhs;
                }
                return true;
            }

            private static bool Product(string s, ref int pos, out decimal v)
            {
                if (!Unary(s, ref pos, out v)) return false;
                while (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
                {
                    char op = s[pos++];
                    decimal rhs;
                    if (!Unary(s, ref pos, out rhs)) return false;
                    v = op == '*' ? v * rhs : v / rhs;
                }
                return true;
            }

            private static bool Unary(string s, ref int pos, out decimal v)
            {
                if (pos < s.Length && (s[pos] == '-' || s[pos] == '+'))
                {
                    bool neg = s[pos++] == '-';
                    if (!Unary(s, ref pos, out v)) return false;
                    if (neg) v = -v;
                    return true;
                }
                if (pos < s.Length && s[pos] == '(')
                {
                    pos++;
                    if (!Sum(s, ref pos, out v) || pos >= s.Length || s[pos] != ')') return false;
                    pos++;
                    return true;
                }
                int start = pos;
                while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
                return decimal.TryParse(s.Substring(start, pos - start), NumberStyles.AllowDecimalPoint,
                                        CultureInfo.InvariantCulture, out v);
            }
        }
    }
}
