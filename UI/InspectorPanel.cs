using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Aplat.UI
{
    // The side panel's layout: a vertical stack of full-width rows and N-column
    // grids, with symmetric padding. Deterministic by construction — every child
    // gets an exact width from the panel's own width, so nothing can overflow
    // sideways, wrap into a lone column or leave a ragged right edge (the failure
    // modes of the FlowLayoutPanel sizing it replaces).
    //
    // A grid's incomplete last row stretches its cells across the full width.
    //
    // Vertical rhythm comes from each child's Margin (Top / Bottom); horizontal
    // positions ignore margins. Hidden children take no space; the panel sets its
    // own Height to fit, which is what ScrollHost scrolls.
    public sealed class InspectorPanel : Panel
    {
        private sealed class Row
        {
            public Control[] Cells;
            public int Columns;
        }

        private readonly List<Row> _rows = new List<Row>();
        private bool _inLayout;

        public InspectorPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            Padding   = new Padding(16, 12, 16, 16);
        }

        public int ColumnGap { get; set; } = 8;
        public int RowGap    { get; set; } = 8;

        // A control spanning the full content width.
        public void AddRow(Control c)
        {
            AddCells(1, new[] { c });
        }

        // Cells placed left→right in `columns` equal columns, wrapping into rows.
        public void AddGrid(int columns, params Control[] cells)
        {
            AddCells(Math.Max(1, columns), cells);
        }

        private void AddCells(int columns, Control[] cells)
        {
            if (cells == null || cells.Length == 0) return;
            _rows.Add(new Row { Cells = cells, Columns = columns });
            foreach (var c in cells)
            {
                Controls.Add(c);
                c.VisibleChanged += Child_Changed;
                c.SizeChanged    += Child_Changed;
            }
            PerformLayout();
        }

        private void Child_Changed(object sender, EventArgs e)
        {
            if (!_inLayout) PerformLayout();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // While the panel is hidden (an unselected tab) every child reports
            // Visible = false; laying out then would collapse it. Wait until shown.
            if (!Visible || _rows.Count == 0 || _inLayout) return;

            _inLayout = true;
            try
            {
                int x0 = Padding.Left;
                int w  = Math.Max(20, ClientSize.Width - Padding.Horizontal);
                int y  = Padding.Top;
                bool first = true;

                foreach (var row in _rows)
                {
                    var shown = new List<Control>();
                    foreach (var c in row.Cells) if (c.Visible) shown.Add(c);
                    if (shown.Count == 0) continue;

                    // The first visible row doesn't add its top margin: Padding.Top
                    // already is the panel's inset, so the top matches the sides.
                    if (!first) y += shown[0].Margin.Top;
                    first = false;

                    if (row.Columns == 1)
                    {
                        var c = shown[0];
                        c.SetBounds(x0, y, w, c.Height);
                        y += c.Height + c.Margin.Bottom;
                        continue;
                    }

                    int cols = row.Columns;
                    int rowH = 0;
                    // A short last row (7 tiles in 2 columns) shares the full width, so
                    // the grid never ends with a lone tile beside an empty hole.
                    int lastStart = shown.Count - shown.Count % cols;
                    for (int i = 0; i < shown.Count; i++)
                    {
                        int col = i % cols;
                        if (col == 0 && i > 0) { y += rowH + RowGap; rowH = 0; }
                        int n = i >= lastStart ? shown.Count - lastStart : cols;
                        int cellW = (w - ColumnGap * (n - 1)) / n;
                        int x = x0 + col * (cellW + ColumnGap);
                        // Last column absorbs the rounding remainder: flush right edge.
                        int cw = col == n - 1 ? x0 + w - x : cellW;
                        var c = shown[i];
                        c.SetBounds(x, y, cw, c.Height);
                        rowH = Math.Max(rowH, c.Height);
                    }
                    y += rowH + shown[shown.Count - 1].Margin.Bottom;
                }

                int h = y + Padding.Bottom;
                if (Height != h) Height = h;
            }
            finally { _inLayout = false; }
        }
    }
}
