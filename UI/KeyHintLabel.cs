using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Blender-style input hints for the status bar: each hint is a key cap followed
    // by what it does — [LMB] Draw  [[ ]] Size  [Alt+wheel] Size …
    // The set follows the active tool (MainForm.UpdateKeyHints).
    public sealed class KeyHintLabel : ToolStripStatusLabel
    {
        private const int CapPadX = 6, CapGap = 6, HintGap = 14;
        private readonly List<KeyValuePair<string, string>> _hints = new List<KeyValuePair<string, string>>();
        private Font _capFont;

        public KeyHintLabel()
        {
            AutoSize  = false;
            Spring    = false;
            Alignment = ToolStripItemAlignment.Right;
        }

        // Pairs of (keys, action).
        public void SetHints(params string[][] hints)
        {
            _hints.Clear();
            foreach (var h in hints)
                if (h != null && h.Length == 2) _hints.Add(new KeyValuePair<string, string>(h[0], h[1]));
            int w = MeasureWidth();
            if (Width != w)
            {
                // Right-aligned: a new width moves the item, so the strip must lay out
                // and repaint as a whole — otherwise it is clipped or leaves old ink.
                Width = w;
                if (Owner != null) { Owner.PerformLayout(); Owner.Invalidate(); }
            }
            Invalidate();
        }

        private Font CapFont
        {
            get { return _capFont ?? (_capFont = new Font(Font.FontFamily, Font.Size - 0.75f, FontStyle.Bold)); }
        }

        private int MeasureWidth()
        {
            int w = 0;
            foreach (var h in _hints)
            {
                w += TextKit.InkWidth(h.Key, CapFont) + CapPadX * 2 + CapGap;
                w += TextKit.InkWidth(h.Value, Font) + HintGap;
            }
            return w > 0 ? w - HintGap + 8 : 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int x = 4, h = Height;
            int capH = System.Math.Min(18, h - 4), capY = (h - capH) / 2;
            foreach (var hint in _hints)
            {
                var cap = new Rectangle(x, capY, TextKit.InkWidth(hint.Key, CapFont) + CapPadX * 2, capH);
                KeyCap.Draw(g, hint.Key, CapFont, cap);
                x = cap.Right + CapGap;

                int vw = TextKit.InkWidth(hint.Value, Font);
                // Same optical centre as the cap text, so both read on one line.
                TextKit.Draw(g, hint.Value, Font, Theme.TextMuted, new Rectangle(x, capY, vw, capH));
                x += vw + HintGap;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _capFont != null) { _capFont.Dispose(); _capFont = null; }
            base.Dispose(disposing);
        }
    }
}
