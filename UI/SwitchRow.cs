using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // An on/off option as a whole row: a rounded card with a readable label and a switch on the right (accent
    // pill with the knob to the right when on, grey pill with the knob to the left when off). Still a CheckBox
    // for behaviour (click, Space, CheckedChanged); only the paint is ours. Unlike a bare check box, the state
    // reads at a glance and the whole row is the click target.
    public class SwitchRow : CheckBox
    {
        private const int SwitchW = 34, SwitchH = 18, PadX = 10, Gap = 10, Radius = 7;
        private bool _hover;

        public SwitchRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = 46;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e)  { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(card, Radius))
            {
                using (var b = new SolidBrush(_hover ? Theme.BtnHover : Theme.BtnFace)) g.FillPath(b, path);
                using (var p = new Pen(Checked ? Theme.Accent : Theme.Border)) g.DrawPath(p, path);
            }

            // The switch.
            var sw = new Rectangle(Width - PadX - SwitchW, (Height - SwitchH) / 2, SwitchW, SwitchH);
            using (var path = Theme.RoundRect(sw, SwitchH / 2))
            {
                using (var b = new SolidBrush(Checked ? Theme.Accent : Theme.SliderTrack)) g.FillPath(b, path);
                if (!Checked)
                    using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            int knob = SwitchH - 6;
            int kx = Checked ? sw.Right - knob - 3 : sw.X + 3;
            using (var b = new SolidBrush(Checked ? Theme.AccentText : Theme.TextSoft))
                g.FillEllipse(b, kx, sw.Y + 3, knob, knob);

            // The label: up to two lines, centred vertically, in the full text colour.
            int textX = PadX, textW = Math.Max(0, sw.X - Gap - textX);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.TextDisabled))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.DrawString(Text, Font, b, new RectangleF(textX, 2, textW, Height - 4), sf);
            }

            if (Focused && ShowFocusCues)
                using (var ring = Theme.RoundRect(Rectangle.Inflate(card, -2, -2), Radius - 1))
                using (var p = new Pen(Color.FromArgb(150, Theme.Accent), 1.5f))
                    g.DrawPath(p, ring);
        }
    }
}
