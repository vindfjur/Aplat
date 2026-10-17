using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A "name ··· value" readout for slider captions: text of the form
    // "Brush size: 28px" is drawn as a muted name on the left and the value
    // right-aligned in the primary colour, like an instrument panel. Callers keep
    // assigning the plain "name: value" string, so no update code has to change;
    // text without ": " falls back to a normal left-aligned label.
    public sealed class ValueLabel : Label
    {
        public ValueLabel()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(System.EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor.A == 255 ? BackColor : (Parent != null ? Parent.BackColor : Theme.Card));

            string text = Text ?? string.Empty;
            int split = text.LastIndexOf(": ", System.StringComparison.Ordinal);
            var rect = new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height);
            if (split < 0)
            {
                TextKit.Draw(g, text, Font, ForeColor, rect, HAlign.Left, ellipsis: true);
                return;
            }

            string name  = text.Substring(0, split);
            string value = text.Substring(split + 2);
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                int vw = TextKit.InkWidth(value, bold);
                var nameRect = new Rectangle(rect.X, rect.Y, System.Math.Max(0, rect.Width - vw - 8), rect.Height);
                TextKit.Draw(g, name, Font, ForeColor, nameRect, HAlign.Left, ellipsis: true);
                TextKit.Draw(g, value, bold, Enabled ? Theme.Text : Theme.TextMuted, rect, HAlign.Right);
            }
        }
    }
}
