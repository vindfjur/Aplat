using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // One renderer for every strip in the app — menu bar, toolbars, drop-down and
    // context menus, status bar. Everything is read from Theme at paint time, so a
    // theme switch needs nothing more than a repaint. Fills are flat: a gradient
    // over AffectedBounds shows up as per-item "boxes" when items repaint alone.
    public sealed class ModernToolStripRenderer : ToolStripProfessionalRenderer
    {
        public ModernToolStripRenderer()
            : base(new ModernColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Color bg = e.ToolStrip is ToolStripDropDown ? Theme.Card
                     : e.ToolStrip is StatusStrip        ? Theme.Bg
                     : Theme.Card;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var strip = e.ToolStrip;
            if (strip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.Border))
                    g.DrawRectangle(pen, 0, 0, strip.Width - 1, strip.Height - 1);
                return;
            }
            using (var pen = new Pen(Theme.BorderSoft))
            {
                // Status bar: hairline on top; toolbars: hairline underneath.
                int y = strip is StatusStrip ? 0 : strip.Height - 1;
                g.DrawLine(pen, 0, y, strip.Width, y);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            bool active = btn != null && btn.Checked;
            bool hot = e.Item.Selected || e.Item.Pressed;
            if (!active && !hot) return;

            Color face = e.Item.Pressed ? Theme.BtnDown : active ? Theme.AccentSoft : Theme.BtnHover;
            using (var brush = new SolidBrush(face)) e.Graphics.FillRectangle(brush, ButtonFace(e.Item));
        }

        // The highlight of a toolbar button: equal insets on all sides, so the label
        // (centred on the item) is centred on the highlight too.
        private static Rectangle ButtonFace(ToolStripItem item)
        {
            return new Rectangle(1, 3, item.Width - 2, item.Height - 6);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            using (var brush = new SolidBrush(e.Item.Pressed ? Theme.BtnDown : Theme.BtnHover))
                e.Graphics.FillRectangle(brush, ButtonFace(e.Item));
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            bool open = item.Pressed || (item is ToolStripMenuItem && ((ToolStripMenuItem)item).DropDown.Visible);
            if (!(item.Selected && item.Enabled) && !open) return;

            var rect = item.IsOnDropDown
                ? new Rectangle(4, 1, item.Width - 6, item.Height - 2)
                : new Rectangle(1, 2, item.Width - 2, item.Height - 4);
            using (var brush = new SolidBrush(open && !item.IsOnDropDown ? Theme.BtnDown : Theme.BtnHover))
                e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var btn  = e.Item as ToolStripButton;
            var menu = e.Item as ToolStripMenuItem;
            if (!e.Item.Enabled) e.TextColor = Theme.TextDisabled;
            else if (btn != null && btn.Checked) e.TextColor = Theme.Accent;
            else if (e.Item is ToolStripStatusLabel) e.TextColor = Theme.TextSoft;
            // A menu item paints its shortcut in a second pass: quieter than the label.
            else if (menu != null && menu.IsOnDropDown && e.Text != menu.Text) e.TextColor = Theme.TextMuted;
            else e.TextColor = Theme.Text;

            // Our own text pass (TextKit): the stock one measures and draws with
            // different metrics, which put labels 2–3px off centre in their highlight.
            string text = StripMnemonic(e.Text);
            if (string.IsNullOrEmpty(text)) return;
            Rectangle tr = e.TextRectangle;
            HAlign align = HAlign.Left;
            if ((e.TextFormat & TextFormatFlags.Right) != 0) align = HAlign.Right;
            bool textOnlyButton = !e.Item.IsOnDropDown && e.Item.DisplayStyle == ToolStripItemDisplayStyle.Text
                                  && (e.Item is ToolStripButton || e.Item is ToolStripMenuItem);
            if (textOnlyButton)
            {
                // Text-only toolbar buttons and top-level menu titles: centred in the item.
                tr = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                align = HAlign.Center;
            }
            else
            {
                // Keep the layout's horizontal slot (plus the GDI text inset the layout
                // reserved) and centre vertically on the whole item.
                tr = new Rectangle(tr.X + TextInset, 0, System.Math.Max(0, tr.Width - TextInset), e.Item.Height);
            }
            TextKit.Draw(e.Graphics, text, e.TextFont ?? e.Item.Font, e.TextColor, tr, align,
                         ellipsis: e.Item is ToolStripStatusLabel);
        }

        private const int TextInset = 6;

        private static string StripMnemonic(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
            return s.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item.Tag is UiIcon)
            {
                Color color = ItemColor(e.Item);
                var menu = e.Item as ToolStripMenuItem;
                if (menu != null && menu.Checked)
                {
                    // Checked item with an icon: tint the icon well instead of a tick.
                    var well = Rectangle.Inflate(e.ImageRectangle, 3, 3);
                    using (var b = new SolidBrush(Theme.AccentSoft)) e.Graphics.FillRectangle(b, well);
                    color = Theme.Accent;
                }
                IconFactory.Draw(e.Graphics, (UiIcon)e.Item.Tag, color, e.ImageRectangle);
                return;
            }
            base.OnRenderItemImage(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Icon items show their checked state in OnRenderItemImage.
            if (e.Item.Tag is UiIcon) return;
            var r = e.ImageRectangle;
            var mark = new RectangleF(r.X + (r.Width - 14) / 2f, r.Y + (r.Height - 14) / 2f, 14, 14);
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(mark.X + 2.5f, mark.Y + 7.5f),
                    new PointF(mark.X + 5.8f, mark.Y + 10.8f),
                    new PointF(mark.X + 11.5f, mark.Y + 3.8f)
                });
            g.SmoothingMode = old;
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? Theme.TextDisabled : Theme.TextSoft;
            base.OnRenderArrow(e);
        }

        // Drag handle of a movable toolbar: a column (or row) of small dots, like
        // Visual Studio's grip — visible, but quieter than any button.
        protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
        {
            if (e.GripStyle != ToolStripGripStyle.Visible) return;
            var r = e.GripBounds;
            bool vertical = e.GripDisplayStyle == ToolStripGripDisplayStyle.Vertical;
            using (var b = new SolidBrush(Theme.TextMuted))
            {
                // Two staggered lines of 2×2 dots along the grip.
                if (vertical)
                {
                    int x = r.Left + r.Width / 2 - 3;
                    for (int y = r.Top + 9, i = 0; y + 2 <= r.Bottom - 7; y += 3, i++)
                        e.Graphics.FillRectangle(b, x + (i % 2) * 4, y, 2, 2);
                }
                else
                {
                    int y = r.Top + r.Height / 2 - 3;
                    for (int x = r.Left + 9, i = 0; x + 2 <= r.Right - 7; x += 3, i++)
                        e.Graphics.FillRectangle(b, x, y + (i % 2) * 4, 2, 2);
                }
            }
        }

        protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Card))
                e.Graphics.FillRectangle(b, e.ToolStripPanel.ClientRectangle);
            e.Handled = true;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.BorderSoft))
            {
                if (e.Vertical)
                {
                    int x = e.Item.Width / 2;
                    e.Graphics.DrawLine(pen, x, 9, x, e.Item.Height - 9);
                }
                else
                {
                    int y = e.Item.Height / 2;
                    e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 4, y);
                }
            }
        }

        private static Color ItemColor(ToolStripItem item)
        {
            if (!item.Enabled) return Theme.TextDisabled;
            var btn = item as ToolStripButton;
            if (btn != null && btn.Checked) return Theme.Accent;
            return Theme.Text;
        }

        private sealed class ModernColorTable : ProfessionalColorTable
        {
            public ModernColorTable() { UseSystemColors = false; }

            public override Color ToolStripGradientBegin     { get { return Theme.Card; } }
            public override Color ToolStripGradientMiddle    { get { return Theme.Card; } }
            public override Color ToolStripGradientEnd       { get { return Theme.Card; } }
            public override Color ToolStripPanelGradientBegin { get { return Theme.Card; } }
            public override Color ToolStripPanelGradientEnd   { get { return Theme.Card; } }
            public override Color GripDark                   { get { return Theme.TextMuted; } }
            public override Color GripLight                  { get { return Theme.Card; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color MenuStripGradientBegin     { get { return Theme.Card; } }
            public override Color MenuStripGradientEnd       { get { return Theme.Card; } }
            public override Color StatusStripGradientBegin   { get { return Theme.Bg; } }
            public override Color StatusStripGradientEnd     { get { return Theme.Bg; } }
            public override Color ImageMarginGradientBegin   { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle  { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd     { get { return Theme.Card; } }
            public override Color MenuItemSelected           { get { return Theme.BtnHover; } }
            public override Color MenuItemBorder             { get { return Theme.BtnHover; } }
            public override Color MenuBorder                 { get { return Theme.Border; } }
            public override Color SeparatorDark              { get { return Theme.BorderSoft; } }
            public override Color SeparatorLight             { get { return Theme.Card; } }
            public override Color CheckBackground            { get { return Theme.AccentSoft; } }
            public override Color CheckSelectedBackground    { get { return Theme.AccentSoft; } }
            public override Color CheckPressedBackground     { get { return Theme.AccentSoft; } }
            public override Color ButtonSelectedHighlight    { get { return Theme.BtnHover; } }
            public override Color ButtonSelectedBorder       { get { return Theme.BtnHover; } }
        }
    }
}
