using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Round palette swatch. The colour is the button's BackColor (so existing click
    // handlers that read it keep working); the selected swatch gets an accent ring
    // with a gap, the way colour pickers in design tools mark the current colour.
    // With IsCustom set it renders a hue wheel instead — the "any colour" entry.
    public sealed class ColorSwatch : Button, IThemeExempt
    {
        private int  _diameter;
        private bool _selected;
        private bool _custom;
        private bool _hover;

        public ColorSwatch()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor  = Cursors.Hand;
            TabStop = false;
            UseVisualStyleBackColor = false;
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Invalidate(); } }
        }

        // Circle size in px (ring included); 0 = fill the control. With a fixed
        // diameter the swatch centres itself in whatever cell a grid gives it.
        public int Diameter
        {
            get { return _diameter; }
            set { if (_diameter != value) { _diameter = value; Invalidate(); } }
        }

        public bool IsCustom
        {
            get { return _custom; }
            set { if (_custom != value) { _custom = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float size = Math.Min(Width, Height) - 1f;
            if (_diameter > 0) size = Math.Min(size, _diameter);
            var outer = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            // Ring (selected / hover) sits on the outer edge; the dot is inset so the
            // gap between them shows the surface — reads cleanly on any colour.
            var dot = RectangleF.Inflate(outer, -3.5f, -3.5f);

            if (_custom) FillHueWheel(g, dot);
            else using (var b = new SolidBrush(BackColor)) g.FillEllipse(b, dot);

            // Hairline keeps white / very light swatches visible on the white card.
            using (var pen = new Pen(Color.FromArgb(Theme.IsDark ? 70 : 40, Theme.IsDark ? Color.White : Theme.Shadow), 1f))
                g.DrawEllipse(pen, dot);

            if (_selected || _hover)
            {
                var ring = RectangleF.Inflate(outer, -0.9f, -0.9f);
                Color c = _selected ? Theme.Accent : Theme.Border;
                using (var pen = new Pen(c, _selected ? 1.8f : 1.2f)) g.DrawEllipse(pen, ring);
            }
        }

        // A hue wheel: the spectrum as thin pie sectors. GDI+ has no conic gradient,
        // and PathGradientBrush renders inconsistently across GDI+ builds; 48 flat
        // sectors look continuous at swatch size and draw the same everywhere.
        private static void FillHueWheel(Graphics g, RectangleF r)
        {
            const int steps = 48;
            const float sweep = 360f / steps;
            for (int i = 0; i < steps; i++)
                using (var b = new SolidBrush(FromHue(i * 360.0 / steps)))
                    g.FillPie(b, r.X, r.Y, r.Width, r.Height, -90f + i * sweep, sweep + 0.8f);
        }

        private static Color FromHue(double h)
        {
            double x = 1 - Math.Abs(h / 60 % 2 - 1);
            double r, gr, b;
            if      (h < 60)  { r = 1; gr = x; b = 0; }
            else if (h < 120) { r = x; gr = 1; b = 0; }
            else if (h < 180) { r = 0; gr = 1; b = x; }
            else if (h < 240) { r = 0; gr = x; b = 1; }
            else if (h < 300) { r = x; gr = 0; b = 1; }
            else              { r = 1; gr = 0; b = x; }
            return Color.FromArgb((int)(r * 235 + 10), (int)(gr * 235 + 10), (int)(b * 235 + 10));
        }
    }
}
