using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Section marker for the inspector: an upper-case overline followed by a
    // hairline rule to the right edge — "BRUSH ───────". Groups the panel into
    // clear blocks without boxes or background bands.
    public sealed class SectionHeader : Label
    {
        public SectionHeader() : this(string.Empty) { }

        public SectionHeader(string text)
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            Text     = text;
            AutoSize = false;
            Height   = 18;
            Font     = Theme.Font(7.6f, FontStyle.Bold);
        }

        // Always shown in capitals, whoever sets the text.
        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value == null ? null : value.ToUpperInvariant(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            TextKit.Draw(g, Text, Font, Theme.TextMuted, new Rectangle(0, 0, Width, Height));

            // The rule runs through the middle of the capitals.
            int x = TextKit.InkWidth(Text, Font) + 10, y = Height / 2;
            if (x < Width)
                using (var pen = new Pen(Theme.BorderSoft)) g.DrawLine(pen, x, y, Width - 1, y);
        }
    }
}
