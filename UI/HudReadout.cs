using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // A HUD value readout: a small glyph and a value, drawn as one centred group
    // ("⌕ 100%", "◔ 1.0x"). Replaces Label + Image, which pinned the icon to one
    // edge and the text to the other, so the gap between them changed with the value.
    public sealed class HudReadout : Label
    {
        private UiIcon _icon;

        public HudReadout(UiIcon icon)
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            _icon    = icon;
            AutoSize = false;
            Font     = Theme.Font(8.5f, FontStyle.Bold);
        }

        public UiIcon Icon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        protected override void OnTextChanged(System.EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);

            const int iconSize = 14, gap = 5;
            string text = Text ?? string.Empty;
            int tw = TextKit.InkWidth(text, Font);
            int x = (Width - (iconSize + gap + tw)) / 2;

            IconFactory.Draw(g, _icon, Theme.TextMuted, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize));
            TextKit.Draw(g, text, Font, Theme.TextSoft, new Rectangle(x + iconSize + gap, 0, tw, Height));
        }
    }
}
