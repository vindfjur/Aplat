using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.UI
{
    // The hotkey reference as a table: sections, and in each a row per command —
    // key caps on the left (the same caps as the status bar and the command
    // palette), what they do on the right. Owner-drawn; its height is fixed by the
    // rows, so a ScrollHost scrolls it.
    //
    // Key spec: "Ctrl+Shift+S" is one chord (a cap per key); " / " separates
    // alternatives ("Ctrl+Y / Ctrl+Shift+Z"). A cap may be any word ("Drag .aplat").
    public sealed class HotkeyTable : Control
    {
        public sealed class Row
        {
            public string Keys, Action;
            public Row(string keys, string action) { Keys = keys; Action = action; }
        }

        public sealed class Section
        {
            public string Title;
            public Row[] Rows;
            public Section(string title, params Row[] rows) { Title = title; Rows = rows; }
        }

        private const int RowH = 32, SectionGap = 22, SectionH = 24, ColumnsH = 26;
        private const int CapH = 22, CapGap = 4, AltGap = 22, ColumnGap = 28;

        private readonly List<Section> _sections = new List<Section>();
        private int _hot = -1;                       // hovered row (flat index)
        private readonly List<int> _rowTop = new List<int>();   // flat row → y
        private int _keysWidth;

        public HotkeyTable(IEnumerable<Section> sections)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _sections.AddRange(sections);
            Font = Theme.Font(9.5f);
            Measure();
        }

        // Heights and the keys column are known from the data alone.
        private void Measure()
        {
            _rowTop.Clear();
            int y = ColumnsH, widest = 0;
            using (var capFont = Theme.Font(7.75f, FontStyle.Bold))
                for (int s = 0; s < _sections.Count; s++)
                {
                    y += (s == 0 ? 6 : SectionGap) + SectionH;
                    foreach (var r in _sections[s].Rows)
                    {
                        _rowTop.Add(y);
                        y += RowH;
                        widest = Math.Max(widest, KeysWidth(r.Keys, capFont));
                    }
                }
            _keysWidth = widest;
            Height = y + 16;
        }

        private static List<List<string>> Parse(string spec)
        {
            var alts = new List<List<string>>();
            foreach (string alt in spec.Split(new[] { " / " }, StringSplitOptions.None))
                alts.Add(new List<string>(alt.Split('+')));
            return alts;
        }

        private static int KeysWidth(string spec, Font capFont)
        {
            int w = 0, a = 0;
            foreach (var alt in Parse(spec))
            {
                if (a++ > 0) w += AltGap;
                for (int i = 0; i < alt.Count; i++)
                    w += KeyCap.WidthFor(alt[i], capFont, 24) + (i > 0 ? CapGap : 0);
            }
            return w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            int keysW = Math.Min(_keysWidth, (int)(Width * 0.55f));
            int actionX = keysW + ColumnGap;

            using (var small = Theme.Font(7.6f, FontStyle.Bold))
            using (var capFont = Theme.Font(7.75f, FontStyle.Bold))
            using (var action = Theme.Font(9.5f))
            {
                // Column captions, over a rule — it is a table.
                TextKit.Draw(g, Loc.T("КЛАВИШИ"), small, Theme.TextMuted, new Rectangle(0, 0, keysW, ColumnsH - 6));
                TextKit.Draw(g, Loc.T("ДЕЙСТВИЕ"), small, Theme.TextMuted, new Rectangle(actionX, 0, Width - actionX, ColumnsH - 6));
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, ColumnsH - 4, Width - 1, ColumnsH - 4);

                int flat = 0, y = ColumnsH;
                for (int s = 0; s < _sections.Count; s++)
                {
                    y += s == 0 ? 6 : SectionGap;
                    var title = _sections[s].Title.ToUpperInvariant();
                    var head = new Rectangle(0, y, Width, SectionH);
                    if (head.IntersectsWith(e.ClipRectangle))
                    {
                        TextKit.Draw(g, title, small, Theme.Accent, new Rectangle(0, y + 2, Width, SectionH - 2));
                        int x = TextKit.InkWidth(title, small) + 10;
                        if (x < Width)
                            using (var pen = new Pen(Theme.BorderSoft)) g.DrawLine(pen, x, y + SectionH / 2 + 1, Width - 1, y + SectionH / 2 + 1);
                    }
                    y += SectionH;

                    var rows = _sections[s].Rows;
                    for (int i = 0; i < rows.Length; i++, flat++)
                    {
                        var rowRect = new Rectangle(0, y, Width, RowH);
                        y += RowH;
                        if (!rowRect.IntersectsWith(e.ClipRectangle)) continue;

                        if (flat == _hot)
                            using (var b = new SolidBrush(Theme.BtnFace)) g.FillRectangle(b, rowRect);
                        if (i > 0)
                            using (var pen = new Pen(Theme.BorderSoft)) g.DrawLine(pen, 0, rowRect.Y, Width - 1, rowRect.Y);

                        DrawKeys(g, rows[i].Keys, capFont, small, rowRect, keysW);
                        TextKit.Draw(g, rows[i].Action, action, Theme.Text,
                            new Rectangle(actionX, rowRect.Y, Math.Max(10, Width - actionX - 4), RowH), HAlign.Left, ellipsis: true);
                    }
                }
            }
        }

        private static void DrawKeys(Graphics g, string spec, Font capFont, Font slashFont, Rectangle row, int max)
        {
            int x = 0, a = 0, cy = row.Y + (RowH - CapH) / 2;
            foreach (var alt in Parse(spec))
            {
                if (a++ > 0)
                {
                    TextKit.Draw(g, "/", slashFont, Theme.TextMuted, new Rectangle(x, row.Y, AltGap, RowH), HAlign.Center);
                    x += AltGap;
                }
                for (int i = 0; i < alt.Count; i++)
                {
                    if (i > 0) x += CapGap;
                    int w = KeyCap.WidthFor(alt[i], capFont, 24);
                    if (x + w > max + 40) return;   // never over the action column
                    KeyCap.Draw(g, alt[i], capFont, new Rectangle(x, cy, w, CapH));
                    x += w;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = -1;
            for (int i = 0; i < _rowTop.Count; i++)
                if (e.Y >= _rowTop[i] && e.Y < _rowTop[i] + RowH) { hot = i; break; }
            if (hot == _hot) return;
            _hot = hot;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot < 0) return;
            _hot = -1;
            Invalidate();
        }
    }
}
