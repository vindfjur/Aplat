using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Segmented tab header over a borderless page area. Tabs split the full width
    // equally (no dead strip on the right), and the page area is the whole client
    // below the header — the native control's 2–4px page insets are overridden so
    // content sits flush and symmetric.
    public sealed class ModernTabControl : TabControl
    {
        private const int HeaderHeight = 40;

        public ModernTabControl()
        {
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(80, HeaderHeight);
            Padding = new Point(0, 0);
            BackColor = Theme.Card;
        }

        public override Rectangle DisplayRectangle
        {
            get { return new Rectangle(0, HeaderHeight + 1, Width, System.Math.Max(0, Height - HeaderHeight - 1)); }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            UpdateItemSize();
        }

        protected override void OnResize(System.EventArgs e)
        {
            base.OnResize(e);
            UpdateItemSize();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;   // crisp, sharp edges
            using (var bg = new SolidBrush(Theme.Card))
                g.FillRectangle(bg, ClientRectangle);

            using (var pen = new Pen(Theme.BorderSoft))
                g.DrawLine(pen, 0, HeaderHeight, Width, HeaderHeight);

            for (int i = 0; i < TabCount; i++) DrawTab(g, i);
        }

        // Our own equal split (the native tab rects start 2px in and don't fill).
        private Rectangle TabBounds(int index)
        {
            int n = System.Math.Max(1, TabCount);
            int x0 = Width * index / n, x1 = Width * (index + 1) / n;
            return new Rectangle(x0, 0, x1 - x0, HeaderHeight);
        }

        private void DrawTab(Graphics g, int index)
        {
            Rectangle r = TabBounds(index);
            bool active = index == SelectedIndex;

            // Active tab: accent label on a 2px accent underline sitting on the divider —
            // the segmented-control look of pro tools, without a filled pill.
            if (active)
                using (var brush = new SolidBrush(Theme.Accent))
                    g.FillRectangle(brush, r.Left + 12, HeaderHeight - 2, r.Width - 24, 2);

            using (var font = active ? new Font(Font, FontStyle.Bold) : null)
                TextKit.Draw(g, TabPages[index].Text, font ?? Font, active ? Theme.Accent : Theme.TextSoft,
                    Rectangle.Inflate(r, -6, 0), HAlign.Center, ellipsis: true);
        }

        // Clicks in the header follow the painted tabs, not the native hit rectangles
        // (those start 2px in and are narrower): the press is handled here and not
        // passed on, so the native control can't pick a neighbouring tab.
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                long lp = m.LParam.ToInt64();
                var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                if (pt.Y >= 0 && pt.Y < HeaderHeight)
                {
                    for (int i = 0; i < TabCount; i++)
                        if (TabBounds(i).Contains(pt)) { if (SelectedIndex != i) SelectedIndex = i; break; }
                    if (CanFocus) Focus();
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // Re-entrancy guard: on Windows, setting ItemSize makes TabControl nudge its
        // own Size by +1px and back (TabControl.UpdateSize) to force a repaint, which
        // raises OnResize again. Without the guard the width can oscillate between
        // two values forever → StackOverflowException in AdjustWindowRectEx.
        private bool _sizingItems;

        private void UpdateItemSize()
        {
            if (_sizingItems) return;
            int n = TabCount > 0 ? TabCount : 3;
            int width = System.Math.Max(40, (Width - 4) / n);
            var next = new Size(width, HeaderHeight);
            if (ItemSize == next) return;
            _sizingItems = true;
            try { ItemSize = next; }
            finally { _sizingItems = false; }
        }
    }
}
