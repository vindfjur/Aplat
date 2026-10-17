using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A check box drawn entirely in theme colours, in every state.
    //
    // The native control draws a *disabled* check box's label with the system
    // GrayText no matter what ForeColor says — on the dark theme that is grey on
    // near-black, practically invisible ("Brightness flicker", "Do not tile the
    // texture"). Owner-drawing it keeps the label readable (TextDisabled) and
    // makes the box itself match the inspector: a rounded well, accent fill with
    // a check mark when on. Behaviour (click, Space, CheckedChanged) is the base
    // CheckBox's — only the paint is ours.
    public class ModernCheckBox : CheckBox
    {
        private const int Box = 16, Gap = 8;
        private bool _hover;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        // Box + gap + the label on one line (AutoSize uses this).
        public override Size GetPreferredSize(Size proposed)
        {
            int textW = string.IsNullOrEmpty(Text) ? 0 : TextKit.InkWidth(Text, Font) + 2;
            int h = Math.Max(Box + 4, TextKit.CapHeight(Font) + 12);
            return new Size(Box + (textW > 0 ? Gap + textW : 0) + 2, h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e)  { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            bool on = Enabled;

            // Label first, to know whether it wraps; the box aligns to its first line.
            int textX = Box + Gap;
            int textW = Math.Max(0, Width - textX - 2);
            Color fg = on ? Theme.Text : Theme.TextDisabled;
            bool oneLine = string.IsNullOrEmpty(Text) || TextKit.InkWidth(Text, Font) <= textW;
            int lineH = TextKit.CapHeight(Font) + 10;
            int boxY;

            if (oneLine)
            {
                boxY = (Height - Box) / 2;
                TextKit.Draw(g, Text, Font, fg, new Rectangle(textX, 0, textW, Height));
            }
            else
            {
                // Two or more lines: word-wrap, block centred vertically; the box sits
                // level with the first line (as in macOS / Windows settings pages).
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                using (var b = new SolidBrush(fg))
                {
                    var state = g.Save();
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    SizeF sz = g.MeasureString(Text, Font, textW, sf);
                    float top = Math.Max(0f, (Height - sz.Height) / 2f);
                    g.DrawString(Text, Font, b, new RectangleF(textX, top, textW, Height - top), sf);
                    g.Restore(state);
                    boxY = (int)Math.Round(top + Font.GetHeight(g) / 2f - Box / 2f);
                }
            }
            boxY = Math.Max(0, Math.Min(Height - Box, boxY));
            var box = new Rectangle(0, boxY, Box, Box);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new Rectangle(box.X, box.Y, Box - 1, Box - 1), 3))
            {
                if (Checked)
                {
                    // Accent fill + check mark; muted when switched off.
                    Color fill = on ? (_hover ? Theme.AccentHover : Theme.Accent) : Blend(Theme.Accent, Theme.BtnFace, 0.45);
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    IconFactory.Draw(g, UiIcon.Check, on ? Theme.AccentText : Blend(Theme.AccentText, fill, 0.55),
                                     new RectangleF(box.X + 2, box.Y + 2, Box - 4, Box - 4));
                }
                else
                {
                    using (var b = new SolidBrush(on ? Theme.Input : Theme.BtnFace)) g.FillPath(b, path);
                    using (var p = new Pen(on ? (_hover ? Theme.Accent : Theme.Border) : Theme.BorderSoft))
                        g.DrawPath(p, path);
                }
            }

            // Keyboard focus: a ring around the box.
            if (Focused && ShowFocusCues)
                using (var ring = Theme.RoundRect(new Rectangle(box.X - 2, box.Y - 2, Box + 3, Box + 3), 5))
                using (var p = new Pen(Color.FromArgb(150, Theme.Accent), 1.5f))
                    g.DrawPath(p, ring);
        }

        private static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R * t + b.R * (1 - t)),
                (int)(a.G * t + b.G * (1 - t)),
                (int)(a.B * t + b.B * (1 - t)));
        }
    }
}
