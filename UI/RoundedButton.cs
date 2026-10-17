using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Owner-drawn flat button with rounded corners, smooth hover/press feedback and
    // an optional "active" (accent-filled) state. Inherits Button so it is a drop-in
    // replacement anywhere a System.Windows.Forms.Button is expected — the existing
    // field declarations and click handlers keep working unchanged.
    public sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _down;
        private bool _active;
        private int  _radius = Theme.RadiusButton;
        private bool _accent;          // permanent accent style (e.g. primary action)
        private bool _flat;            // HUD style: no idle fill, the parent shows through
        private bool _iconOnly;        // draw the icon alone; Text stays for accessibility
        private UiIcon _icon = UiIcon.None;
        private int  _iconSize = 18;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle  = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor  = Theme.BtnFace;
            ForeColor  = Theme.Text;
            Cursor     = Cursors.Hand;
            UseVisualStyleBackColor = false;
            ImageAlign = ContentAlignment.MiddleCenter;
            TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        // A helper button (a floating bar's): a click does not move the keyboard to it.
        public bool NeverFocus
        {
            get { return !GetStyle(ControlStyles.Selectable); }
            set { SetStyle(ControlStyles.Selectable, !value); TabStop = !value; }
        }

        // Corner radius in pixels.
        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        // Selected / active tool tile — painted with the accent colour.
        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        // Primary-action styling (always accent), independent of selection.
        public bool Accent
        {
            get { return _accent; }
            set { if (_accent != value) { _accent = value; Invalidate(); } }
        }

        // Borderless HUD button: transparent until hovered, soft idle glyph colour.
        // Used on the floating overlay cards, where a chip per button reads as noise.
        public bool Flat
        {
            get { return _flat; }
            set { if (_flat != value) { _flat = value; Invalidate(); } }
        }

        // Paint only the icon, centred. Text is kept (screen readers, AccessibleName)
        // and the tooltip carries the label — a compact, instrument-panel look.
        public bool IconOnly
        {
            get { return _iconOnly; }
            set { if (_iconOnly != value) { _iconOnly = value; Invalidate(); } }
        }

        // Pixel size of the generated Icon glyph (ignored for a plain Image).
        public int IconSize
        {
            get { return _iconSize; }
            set { if (_iconSize != value) { _iconSize = value; Invalidate(); } }
        }

        public UiIcon Icon
        {
            get { return _icon; }
            set { if (_icon != value) { _icon = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true;  Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)   { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            // On a theme surface the text is the theme's; on a data colour (brush,
            // material) it is whichever of white / ink reads on that colour.
            if (!_accent && !_active)
                ForeColor = IsDataColour() ? Theme.ContrastText(BackColor) : Theme.Text;
            Invalidate();
        }

        private static bool Near(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 48;
        }

        private Color FadedFace(Color c)
        {
            return Mix(c, Parent != null ? Parent.BackColor : Theme.Card, 0.35);
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R * t + b.R * (1 - t)),
                (int)Math.Round(a.G * t + b.G * (1 - t)),
                (int)Math.Round(a.B * t + b.B * (1 - t)));
        }

        // A swatch button (marked with ThemeManager.KeepColors) shows a data colour —
        // even white on a white card is a real light colour, so the value alone can't
        // tell; only its "no colour set" face (BtnFace) counts as a theme surface.
        private bool IsDataColour()
        {
            if (ThemeManager.IsKept(this)) return BackColor.ToArgb() != Theme.BtnFace.ToArgb();
            return !IsThemeSurface(BackColor);
        }

        private static bool IsThemeSurface(Color c)
        {
            int argb = c.ToArgb();
            return argb == Theme.BtnFace.ToArgb() || argb == Theme.Card.ToArgb()
                || argb == Theme.Surface.ToArgb() || argb == Theme.Bg.ToArgb();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.None;   // sharp 1px rectangle edges
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Parent shows through the rounded corners (and the whole face when Flat).
            Color parent = Parent != null ? Parent.BackColor : Theme.Card;
            g.Clear(parent);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color face;
            if (_accent)      face = _down || _hover ? Theme.AccentHover : Theme.Accent;
            else if (_active) face = _down ? Theme.BtnDown : Theme.AccentSoft;
            else if (_flat)   face = _down ? Theme.BtnDown : _hover ? Theme.BtnHover : parent;
            else if (IsDataColour())
                // A data colour (brush, material) stays itself; hover / press only darken it.
                face = _down ? Theme.Shade(BackColor, -26) : _hover ? Theme.Shade(BackColor, -14) : BackColor;
            else              face = _down ? Theme.BtnDown : _hover ? Theme.BtnHover : BackColor;

            // Flat fills, no idle borders: a row of boxed chips read as clutter. Only
            // the active tool gets a hairline in the accent, so selection is readable
            // even where the soft tint is close to the surface behind it.
            using (var path = Theme.RoundRect(rect, _radius))
            {
                // Switched off: a data colour fades into the panel (35%), so a white
                // light swatch no longer reads as a lit, clickable slab.
                if (!Enabled && !_flat && (_accent || IsDataColour())) face = FadedFace(face);
                using (var b = new SolidBrush(face)) g.FillPath(b, path);
                // A data colour that matches the panel (white light on a white card,
                // black brush on Graphite) would leave a bare label: give it a hairline.
                if (!_flat && !_accent && !_active && IsDataColour() && Near(face, parent))
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                // HUD (flat) buttons mark "active" by the tint alone — a frame on one
                // button of an otherwise frameless row makes it look larger than its peers.
                if (_active && !_accent && !_flat)
                    using (var pen = new Pen(Color.FromArgb(110, Theme.Accent))) g.DrawPath(pen, path);
            }

            Color fg;
            if (!Enabled)
                // Switched off: theme surfaces get the readable disabled tone; a data
                // colour (light / brush swatch) keeps its contrast text, faded with it.
                fg = !IsDataColour() || _flat || _active
                   ? Theme.TextDisabled
                   : Mix(Theme.ContrastText(BackColor), FadedFace(BackColor), 0.55);
            else if (_accent) fg = Theme.AccentText;
            else if (_active) fg = Theme.Accent;
            else if (_flat)   fg = _hover || _down ? Theme.Text : Theme.TextSoft;
            else              fg = ForeColor;
            DrawContent(g, rect, fg);
        }

        private void DrawContent(Graphics g, Rectangle rect, Color fg)
        {
            bool disposeImage;
            Image image = GetPaintImage(fg, out disposeImage);
            bool hasImage = image != null;
            bool hasText = !_iconOnly && !string.IsNullOrEmpty(Text);
            if (!hasImage && !hasText) return;

            try
            {
                if (hasImage && TextImageRelation == TextImageRelation.ImageAboveText)
                {
                    // Icon + label centred as one group, both axes: the group runs from
                    // the icon top to the label's baseline (descenders hang below).
                    const int gap = 8;
                    int iw = image.Width, ih = image.Height;
                    int th = hasText ? TextKit.CapHeight(Font) : 0;
                    int groupH = ih + (hasText ? gap + th : 0);
                    int ix = (Width - iw) / 2;
                    int iy = (Height - groupH) / 2;
                    g.DrawImage(image, ix, iy, iw, ih);

                    if (hasText)
                    {
                        var tr = new Rectangle(4, iy + ih + gap, Width - 8, th);
                        TextKit.Draw(g, Text, Font, fg, tr, HAlign.Center, ellipsis: true);
                    }
                    return;
                }

                if (hasImage)
                {
                    int iw = image.Width, ih = image.Height;
                    int textW = hasText ? TextKit.InkWidth(Text, Font) : 0;
                    int gap = hasText ? 8 : 0;
                    int groupW = iw + gap + textW;
                    // Centred content is centred exactly (an icon-only 30px button used to
                    // get a 10px floor here and sat 4px right of centre); left-aligned
                    // rows keep their 14px inset.
                    int ix = TextAlign == ContentAlignment.MiddleCenter
                           ? (hasText ? Math.Max(10, (Width - groupW) / 2) : (Width - iw) / 2)
                           : 14;
                    int iy = (Height - ih) / 2;
                    g.DrawImage(image, ix, iy, iw, ih);

                    if (hasText)
                    {
                        var tr = new Rectangle(ix + iw + gap, 0, Width - ix - iw - gap - 8, Height);
                        TextKit.Draw(g, Text, Font, fg, tr, HAlign.Left, ellipsis: true);
                    }
                    return;
                }
            }
            finally
            {
                if (disposeImage && image != null) image.Dispose();
            }

            var pad = Padding;
            var textRect = Rectangle.FromLTRB(rect.Left + pad.Left + 4, rect.Top,
                                              rect.Right - pad.Right - 4, rect.Bottom);
            TextKit.Draw(g, Text, Font, fg, textRect, ToHAlign(TextAlign), ellipsis: true);
        }

        private static HAlign ToHAlign(ContentAlignment a)
        {
            switch (a)
            {
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.TopCenter:
                case ContentAlignment.BottomCenter:
                    return HAlign.Center;
                case ContentAlignment.MiddleRight:
                case ContentAlignment.TopRight:
                case ContentAlignment.BottomRight:
                    return HAlign.Right;
                default:
                    return HAlign.Left;
            }
        }

        private Image GetPaintImage(Color color, out bool dispose)
        {
            if (_icon != UiIcon.None)
            {
                dispose = true;
                return IconFactory.Create(_icon, color, Image != null ? Image.Width : _iconSize);
            }

            dispose = false;
            return Image;
        }
    }
}
