using System;
using System.Windows.Forms;
using Aplat.UI;

namespace Aplat
{
    // The auto-rotate card: a compact floating control that appears while the scene spin is on, so it can be
    // paused, stopped (back to where it started) or switched off without opening the View menu. It is a
    // normal floating card — it docks, hides and remembers its place like the others. The menu and F9 keep
    // working as before and drive the same state.
    public partial class MainForm
    {
        private Panel spinPanel;
        private FlowLayoutPanel spinFlow;
        private HudReadout lblSpinState;
        private Button btnSpinPause, btnSpinStop, btnSpinOff, btnSpinCamera, btnSpinObject;
        private ModernSlider trkSpinSpeed;
        private bool _spinSyncing;   // the slider is being moved by the code, not by the hand
        private bool _spinStopped;   // paused by the Stop button (shown as "Stopped" until resumed)

        private Panel BuildSpinCard()
        {
            spinFlow = HudFlow(new Padding(8, 8, 8, 8));
            lblSpinState = new HudReadout(UiIcon.Rotate) { Width = 130, Height = 30, Margin = new Padding(0) };
            btnSpinPause = OverlayIconButton(UiIcon.Pause, "", btnSpinPause_Click);
            btnSpinStop  = OverlayIconButton(UiIcon.Stop, "", btnSpinStop_Click);
            btnSpinOff   = OverlayIconButton(UiIcon.Close, "", btnSpinOff_Click);
            btnSpinOff.Margin = new Padding(0);
            btnSpinCamera = OverlayIconButton(UiIcon.ViewPerspective, "", (s, e) => SetSpinMode(false));
            btnSpinObject = OverlayIconButton(UiIcon.Cube, "", (s, e) => SetSpinMode(true));
            trkSpinSpeed = new ModernSlider
            {
                Minimum = 6, Maximum = 120, SmallChange = 6, LargeChange = 12,
                Width = 96, Height = 30, Margin = new Padding(4, 0, 6, 0)
            };
            trkSpinSpeed.ValueChanged += trkSpinSpeed_ValueChanged;
            spinFlow.Controls.AddRange(new Control[] { lblSpinState, ToolDivider(30, 8, 8),
                btnSpinCamera, btnSpinObject, trkSpinSpeed, ToolDivider(30, 2, 8), btnSpinPause, btnSpinStop, btnSpinOff });

            if (panelTips != null)
            {
                panelTips.SetToolTip(lblSpinState, Loc.T("Автовращение: что крутится и с какой скоростью (скорость — в меню Вид → Автовращение)"));
                panelTips.SetToolTip(btnSpinCamera, Loc.T("Камера обходит объект"));
                panelTips.SetToolTip(btnSpinObject, Loc.T("Объект крутится вокруг своей оси (выбранный, а если ничего не выбрано — вся сцена)"));
                panelTips.SetToolTip(trkSpinSpeed, Loc.T("Скорость вращения"));
                panelTips.SetToolTip(btnSpinStop, Loc.T("Стоп — остановить и вернуть исходное положение"));
                panelTips.SetToolTip(btnSpinOff, Loc.T("Выключить автовращение (F9)"));
            }
            spinPanel = OverlayPanel(FlowWidth(spinFlow), 46);
            spinPanel.Controls.Add(spinFlow);
            UpdateSpinCard();
            return spinPanel;
        }

        private void trkSpinSpeed_ValueChanged(object sender, EventArgs e)
        {
            if (_spinSyncing || _scene == null) return;
            _scene.AutoRotateSpeed = trkSpinSpeed.Value;
            sceneView.AutoRotateSpeed = _scene.AutoRotateSpeed;
            _scene.IsModified = true;
            SyncAutoRotateUI();
        }

        private void btnSpinPause_Click(object sender, EventArgs e)
        {
            if (!_scene.AutoRotate) return;
            sceneView.AutoRotatePaused = !sceneView.AutoRotatePaused;
            _spinStopped = false;
            UpdateSpinCard();
        }

        private void btnSpinStop_Click(object sender, EventArgs e)
        {
            if (!_scene.AutoRotate) return;
            sceneView.AutoRotatePaused = true;
            sceneView.ResetAutoRotate();
            _spinStopped = true;
            _scene.IsModified = true;
            UpdateSpinCard();
            UpdateTitle();
        }

        private void btnSpinOff_Click(object sender, EventArgs e)
        {
            if (_scene.AutoRotate) ToggleAutoRotate();
        }

        // Brings the card's text and buttons in line with the scene's rotation state; called wherever
        // that state changes (SyncAutoRotateUI) and on every button.
        private void UpdateSpinCard()
        {
            if (spinPanel == null || _scene == null) return;
            if (!_scene.AutoRotate) _spinStopped = false;
            bool paused = sceneView != null && sceneView.AutoRotatePaused;

            lblSpinState.Text = paused
                ? (_spinStopped ? Loc.T("Остановлено") : Loc.T("Пауза"))
                : string.Format(Loc.T("{0} · {1:0}°/с"),
                    _scene.AutoRotateObjects ? Loc.T("Объект") : Loc.T("Камера"), _scene.AutoRotateSpeed);

            ((RoundedButton)btnSpinCamera).Active = !_scene.AutoRotateObjects;
            ((RoundedButton)btnSpinObject).Active = _scene.AutoRotateObjects;
            _spinSyncing = true;
            try { trkSpinSpeed.Value = Math.Max(trkSpinSpeed.Minimum, Math.Min(trkSpinSpeed.Maximum, (int)Math.Round(_scene.AutoRotateSpeed))); }
            finally { _spinSyncing = false; }

            var pause = (RoundedButton)btnSpinPause;
            pause.Icon = paused ? UiIcon.Play : UiIcon.Pause;
            pause.Active = paused;
            if (panelTips != null)
                panelTips.SetToolTip(btnSpinPause, paused ? Loc.T("Продолжить вращение") : Loc.T("Пауза"));
        }
    }
}
