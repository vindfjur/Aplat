using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A segmented control (macOS / iOS style): one track, equal segments, the chosen
    // one raised as a pill. Segments are drawn by the host — a picture says more
    // than a word here (e.g. outline / solid / tinted shape swatches). Tooltips name
    // each segment; ← → change the choice when focused.
    public sealed class SegmentedControl : Control
    {
        private sealed class Segment
        {
            public string Tip;
            public Action<Graphics, Rectangle, bool> Paint;   // (g, content box, selected)
        }

        private const int Inset = 3;   // pill inset inside the track

        private readonly List<Segment> _segments = new List<Segment>();
        private int _selected, _hover = -1;
        private ToolTip _tip;

        public event EventHandler SelectedIndexChanged;   // user changes only

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable, true);
            TabStop = true;
            Height  = 36;
            Cursor  = Cursors.Hand;
        }

        public void AddSegment(string tip, Action<Graphics, Rectangle, bool> paint)
        {
            _segments.Add(new Segment { Tip = tip, Paint = paint });
            Invalidate();
        }

        // For the command palette: what the segments are called, and a way to press one.
        public int SegmentCount { get { return _segments.Count; } }
        public string TipAt(int i) { return _segments[i].Tip; }
        public void Pick(int i) { Choose(i); }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = Math.Max(0, Math.Min(_segments.Count - 1, value));
                if (v == _selected) return;
                _selected = v;
                Invalidate();
            }
        }

        private Rectangle SegmentBounds(int i)
        {
            int n = Math.Max(1, _segments.Count);
            int inner = Width - 2 * Inset;
            int x0 = Inset + inner * i / n, x1 = Inset + inner * (i + 1) / n;
            return new Rectangle(x0, Inset, x1 - x0, Height - 2 * Inset);
        }

        private int SegmentAt(Point p)
        {
            for (int i = 0; i < _segments.Count; i++)
                if (SegmentBounds(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);

            Color track = Theme.IsDark ? Theme.BtnFace : Theme.BtnHover;
            Color pill  = Theme.IsDark ? Theme.Shade(Theme.BtnHover, 10) : Theme.Card;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(track)) g.FillRectangle(b, r);
            using (var p = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.BorderSoft)) g.DrawRectangle(p, r);

            for (int i = 0; i < _segments.Count; i++)
            {
                Rectangle s = SegmentBounds(i);
                bool sel = i == _selected;
                if (sel)
                {
                    using (var b = new SolidBrush(pill)) g.FillRectangle(b, s);
                    using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, s.X, s.Y, s.Width - 1, s.Height - 1);
                }
                else if (i == _hover)
                    using (var b = new SolidBrush(Theme.IsDark ? Theme.BtnHover : Theme.Shade(track, -6))) g.FillRectangle(b, s);

                // Hairline dividers between two unselected neighbours only.
                if (i > 0 && !sel && i - 1 != _selected)
                    using (var p = new Pen(Theme.Border))
                        g.DrawLine(p, s.X, s.Y + 7, s.X, s.Bottom - 8);

                _segments[i].Paint?.Invoke(g, s, sel);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = SegmentAt(e.Location);
            if (h == _hover) return;
            _hover = h;
            if (h >= 0)
            {
                if (_tip == null) { _tip = new ToolTip(); ThemeManager.Style(_tip); }
                _tip.SetToolTip(this, _segments[h].Tip);
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = SegmentAt(e.Location);
            if (i >= 0) Choose(i);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left)  { Choose(_selected - 1); e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Choose(_selected + 1); e.Handled = true; }
        }

        private void Choose(int i)
        {
            i = Math.Max(0, Math.Min(_segments.Count - 1, i));
            if (i == _selected) return;
            _selected = i;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnGotFocus(EventArgs e)  { base.OnGotFocus(e);  Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip?.Dispose();
            base.Dispose(disposing);
        }
    }
}
