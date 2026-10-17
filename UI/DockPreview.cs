using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // The translucent outline shown while a card is dragged near an edge: it marks where the card would
    // dock when released. Click-through and never activated, so it cannot disturb the drag.
    public sealed class DockPreview : Form
    {
        public DockPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            AutoScaleMode   = AutoScaleMode.None;
            Opacity         = 0.5;
            BackColor       = Theme.Accent;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TRANSPARENT = 0x00000020, WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // Shows the preview over a screen rectangle (rounded like the cards).
        public void ShowAt(Rectangle screenBounds)
        {
            BackColor = Theme.Accent;
            if (Bounds != screenBounds) Bounds = screenBounds;
            using (var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusCard))
                Region = new Region(path);
            if (!Visible) Show();
        }
    }
}
