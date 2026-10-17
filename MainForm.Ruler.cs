using System;
using System.Windows.Forms;

namespace Aplat
{
    // The ruler toggle on the floating tool bar (R). It is a helper that stays on while any drawing
    // tool is in use, so it is a switch of its own, not one of the tool tiles.
    public partial class MainForm
    {
        private Button btnRuler;

        private void btnRuler_Click(object s, EventArgs e) { ToggleRuler(); }

        private bool ToggleRuler()
        {
            if (Control.MouseButtons != MouseButtons.None) return false;
            if (!sceneView.RulerVisible)
            {
                if (sceneView.CurrentTool != Aplat.Controls.ToolMode.Paint)
                    SetBrush(sceneView.PaintKind == Aplat.Controls.BrushKind.Select2D || sceneView.PaintKind == Aplat.Controls.BrushKind.Lasso
                             ? Aplat.Controls.BrushKind.Brush : sceneView.PaintKind);
                sceneView.RulerVisible = true;
                if (sceneView.RulerVisible)
                    statusLabel.Text = Loc.T("Линейка: ведите по её краю — линия выйдет ровной. Это обычный объект: Ctrl+корпус — двигать (по самой линейке не рисуется), край — менять длину, круглая ручка — поворот (Shift — 15°), двойной щелчок по концу — закрепить; в режиме 3D его можно двигать, вращать и масштабировать как фигуру. R — убрать");
            }
            else sceneView.RulerVisible = false;
            return true;
        }

        private void SyncRulerButton()
        {
            var rb = btnRuler as Aplat.UI.RoundedButton;
            if (rb != null) rb.Active = sceneView.RulerVisible;
        }
    }
}
