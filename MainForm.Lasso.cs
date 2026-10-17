using System;
using System.Drawing;
using System.Windows.Forms;
using Aplat.Controls;

namespace Aplat
{
    // The selection tools share one tile: "Area" cuts a rectangle of pixels out, "Lasso" picks
    // up the objects a loop touches (strokes, shapes, text). Like the eraser's two kinds, the one
    // used last is the one the tile brings back, and a switch in the panel flips between them.
    // A lasso selection gets a small bar over its frame — colour, copy, delete — as on the iPhone.
    public partial class MainForm
    {
        private Aplat.UI.SegmentedControl segSelectKind;
        private Label lblSelectSection;
        private BrushKind _selectKind = BrushKind.Select2D;

        private void BuildSelectSection()
        {
            lblSelectSection = SectionLabel(Loc.T("Выделение"));
            segSelectKind = new Aplat.UI.SegmentedControl { Height = 34, Margin = new Padding(0) };
            segSelectKind.AddSegment(Loc.T("Область — вырезает прямоугольник пикселей"),
                (g2, r2, sel) => PaintEraserSegment(g2, r2, sel, Aplat.UI.UiIcon.SelectRect, Loc.T("Область")));
            segSelectKind.AddSegment(Loc.T("Лассо — обведите штрихи, фигуры и текст: двигать, масштабировать, вращать"),
                (g2, r2, sel) => PaintEraserSegment(g2, r2, sel, Aplat.UI.UiIcon.Lasso, Loc.T("Лассо")));
            segSelectKind.SelectedIndexChanged += (s2, e2) =>
                SetBrush(segSelectKind.SelectedIndex == 1 ? BrushKind.Lasso : BrushKind.Select2D);
            lblSelectSection.Visible = segSelectKind.Visible = false;   // with the selection tools (UpdateShapeStyleRow)
        }

        private static bool IsSelectKind(BrushKind k) { return k == BrushKind.Select2D || k == BrushKind.Lasso; }

        private void SyncSelectKind(BrushKind kind)
        {
            if (!IsSelectKind(kind)) return;
            _selectKind = kind;
            var rb = btnSelect2D as Aplat.UI.RoundedButton;
            if (rb != null) rb.Icon = kind == BrushKind.Lasso ? Aplat.UI.UiIcon.Lasso : Aplat.UI.UiIcon.SelectRect;
            if (panelTips != null)
                panelTips.SetToolTip(btnSelect2D, kind == BrushKind.Lasso
                    ? Loc.T("Лассо — обведите штрихи, фигуры и текст: двигать, масштабировать, вращать")
                    : Loc.T("Выделить область — двигать, растягивать, вращать"));
            if (segSelectKind != null) segSelectKind.SelectedIndex = kind == BrushKind.Lasso ? 1 : 0;
        }

        // ── The bar over a lasso selection ────────────────────────────
        private Aplat.UI.OverlayWindow _selBar;

        private void EnsureSelectionBar()
        {
            if (_selBar != null) return;
            var panel = new Panel { Width = 3 * 40 + 8, Height = 44, BackColor = Aplat.UI.Theme.Card };
            AddBarButton(panel, 0, Aplat.UI.UiIcon.Palette, Loc.T("Покрасить в цвет кисти"),
                () => sceneView.RecolorLive(sceneView.BrushColor));
            AddBarButton(panel, 1, Aplat.UI.UiIcon.Duplicate, Loc.T("Дублировать выделенное"),
                () => sceneView.DuplicateLive());
            AddBarButton(panel, 2, Aplat.UI.UiIcon.Clear, Loc.T("Удалить выделенное (Delete)"),
                () => sceneView.DeleteLive());
            _selBar = new Aplat.UI.OverlayWindow(new Size(panel.Width + 2, panel.Height + 2));
            _selBar.SetContent(panel);
            _selBar.Owner = this;
            _selBar.Opacity = 0.97;
            Aplat.UI.ThemeManager.Attach(_selBar);
        }

        private void AddBarButton(Panel host, int index, Aplat.UI.UiIcon icon, string tip, Action run)
        {
            var b = new Aplat.UI.RoundedButton
            {
                NeverFocus = true,
                Left = 4 + index * 40, Top = 2, Width = 40, Height = 40,
                Icon = icon, IconSize = 20, IconOnly = true, Flat = true,
                Radius = Aplat.UI.Theme.RadiusButton,
                BackColor = Aplat.UI.Theme.Card, ForeColor = icon == Aplat.UI.UiIcon.Clear ? Color.FromArgb(0xE5, 0x48, 0x4D) : Aplat.UI.Theme.Text,
                Text = tip,
            };
            b.Click += (s, e) => run();
            host.Controls.Add(b);
            if (panelTips != null) panelTips.SetToolTip(b, tip);
        }

        // Shown while a lasso selection is live and still; follows its frame, above it (or below
        // when there is no room above), hidden while the selection is being dragged.
        private void UpdateSelectionBar()
        {
            Point top, bottom;
            bool show = sceneView != null && sceneView.HasLiveGroup && sceneView.LiveBarAnchor(out top, out bottom);
            if (!show) { if (_selBar != null && _selBar.Visible) _selBar.Hide(); return; }
            sceneView.LiveBarAnchor(out top, out bottom);
            EnsureSelectionBar();
            Point t = sceneView.PointToScreen(top), b = sceneView.PointToScreen(bottom);
            var area = sceneView.RectangleToScreen(sceneView.ClientRectangle);
            int x = Math.Max(area.Left + 8, Math.Min(area.Right - _selBar.Width - 8, t.X - _selBar.Width / 2));
            int y = t.Y - _selBar.Height - 44;   // clear of the rotate knob above the frame
            if (y < area.Top + 8) y = b.Y + 14;
            y = Math.Max(area.Top + 8, Math.Min(area.Bottom - _selBar.Height - 8, y));
            _selBar.Location = new Point(x, y);
            if (!_selBar.Visible)
            {
                _selBar.Show(this);
                // The bar is a helper: the keyboard stays with the editor (after the window is mapped).
                BeginInvoke((Action)(() => { if (!IsDisposed) { Activate(); sceneView.FocusView(); } }));
            }
        }


        private void HideSelectionBar()
        {
            if (_selBar != null && _selBar.Visible) _selBar.Hide();
        }
    }
}
