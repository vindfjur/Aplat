using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A row of independent toggles in the segmented-control look (B I U): one
    // track, each "on" segment raised as a pill with an accent glyph. Unlike
    // SegmentedControl, any number of segments can be on at once.
    public sealed class ToggleStrip : Control
    {
        private sealed class Toggle { public UiIcon Icon; public string Tip; public bool On; }

        private const int Inset = 3;
        private readonly List<Toggle> _items = new List<Toggle>();
        private int _hover = -1;
        private ToolTip _tip;

        public event EventHandler Toggled;   // user changes only

        public ToggleStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);   // keeps keyboard focus where it was
            Height = 28;
            Cursor = Cursors.Hand;
        }

        public void Add(UiIcon icon, string tip)
        {
            _items.Add(new Toggle { Icon = icon, Tip = tip });
            Invalidate();
        }

        // For the command palette: what the toggles are called, and a way to press one.
        public int Count { get { return _items.Count; } }
        public string TipAt(int i) { return _items[i].Tip; }
        public void Flip(int i)
        {
            _items[i].On = !_items[i].On;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        public bool this[int i]
        {
            get { return _items[i].On; }
            set { if (_items[i].On != value) { _items[i].On = value; Invalidate(); } }
        }

        private Rectangle Bounds(int i)
        {
            int n = Math.Max(1, _items.Count);
            int inner = Width - 2 * Inset;
            int x0 = Inset + inner * i / n, x1 = Inset + inner * (i + 1) / n;
            return new Rectangle(x0, Inset, x1 - x0, Height - 2 * Inset);
        }

        private int At(Point p)
        {
            for (int i = 0; i < _items.Count; i++) if (Bounds(i).Contains(p)) return i;
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
            using (var p = new Pen(Theme.BorderSoft)) g.DrawRectangle(p, r);

            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                Rectangle s = Bounds(i);
                if (it.On)
                {
                    using (var b = new SolidBrush(pill)) g.FillRectangle(b, s);
                    using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, s.X, s.Y, s.Width - 1, s.Height - 1);
                }
                else if (i == _hover)
                    using (var b = new SolidBrush(Theme.IsDark ? Theme.BtnHover : Theme.Shade(track, -6))) g.FillRectangle(b, s);

                Color c = !Enabled ? Theme.TextDisabled : it.On ? Theme.Accent : Theme.TextSoft;
                const int icon = 16;
                IconFactory.Draw(g, it.Icon, c, new RectangleF(s.X + (s.Width - icon) / 2f, s.Y + (s.Height - icon) / 2f, icon, icon));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = At(e.Location);
            if (h == _hover) return;
            _hover = h;
            if (h >= 0)
            {
                if (_tip == null) { _tip = new ToolTip(); ThemeManager.Style(_tip); }
                _tip.SetToolTip(this, _items[h].Tip);
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = At(e.Location);
            if (i < 0) return;
            _items[i].On = !_items[i].On;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip?.Dispose();
            base.Dispose(disposing);
        }
    }
}
