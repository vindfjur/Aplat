using System;
using System.IO;
using System.Windows.Forms;
using Aplat.Controls;

namespace Aplat
{
    // The eyedropper's option "return to the previous tool after picking a colour".
    public partial class MainForm
    {
        private static string EyedropperPath { get { return Path.Combine(SettingsDir, "eyedropper.txt"); } }
        private System.Windows.Forms.CheckBox chkEyedropperReturn;
        private bool      _eyedropperReturn;
        private BrushKind _kindBeforeEyedropper = BrushKind.Brush;
        private ToolMode  _modeBeforeEyedropper = ToolMode.Paint;
        private bool      _haveToolBeforeEyedropper;

        private void BuildEyedropperRow()
        {
            try { _eyedropperReturn = File.Exists(EyedropperPath) && File.ReadAllText(EyedropperPath).Trim() == "on"; }
            catch (Exception) { }
            // A switch row rather than a bare check box: the option was easy to miss in the dark panel.
            chkEyedropperReturn = new Aplat.UI.SwitchRow
            {
                Text    = Loc.T("Вернуть прошлый инструмент после выбора цвета"),
                Checked = _eyedropperReturn,
                Font    = Aplat.UI.Theme.Font(9f),
                Height  = 46,
                Visible = false,
            };
            chkEyedropperReturn.CheckedChanged += (s, e) =>
            {
                _eyedropperReturn = chkEyedropperReturn.Checked;
                try
                {
                    Directory.CreateDirectory(SettingsDir);
                    File.WriteAllText(EyedropperPath, _eyedropperReturn ? "on" : "off");
                }
                catch (Exception) { }
            };
        }

        // Called from SetBrush before the tool changes: what to come back to.
        private void RememberToolBeforeEyedropper(BrushKind next)
        {
            if (next != BrushKind.Eyedropper) return;
            if (sceneView.CurrentTool == ToolMode.Paint && sceneView.PaintKind == BrushKind.Eyedropper) return;   // already in it
            _kindBeforeEyedropper = sceneView.PaintKind;
            _modeBeforeEyedropper = sceneView.CurrentTool;
            _haveToolBeforeEyedropper = true;
        }

        // The colour was picked: go back to the tool that was active before the eyedropper (if the option is on).
        private void AfterEyedropperPick()
        {
            if (!_eyedropperReturn || !_haveToolBeforeEyedropper) return;
            _haveToolBeforeEyedropper = false;
            if (_modeBeforeEyedropper == ToolMode.Paint)
            {
                if (_kindBeforeEyedropper != BrushKind.Eyedropper) SetBrush(_kindBeforeEyedropper);
            }
            else SetTool(_modeBeforeEyedropper);
        }
    }
}
