using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using Aplat.Controls;
using Aplat.Dialogs;
using Aplat.IO;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

namespace Aplat
{
    public partial class MainForm : Form
    {
        // ── Form ──────────────────────────────────────────────────

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _autosaveTimer.Stop();
            // A scene that already has a file is autosaved silently (as before);
            // ConfirmDiscard then only prompts for never-saved scenes.
            SaveModifiedSceneNow();
            if (!ConfirmDiscard()) { _autosaveTimer.Start(); e.Cancel = true; }
            else
            {
                SaveWindowPlacement();
                SavePanelLayout();   // toolbars (docked / floating) and the floating cards
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Control focused = FindFocused(Form.ActiveForm ?? this);
            if (TryHandleEditorShortcut(keyData, focused)) return true;
            if (IsEditingText(focused)) return false;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool TryHandleEditorShortcut(Keys keyData, Control focused)
        {
            if (keyData == (Keys.Control | Keys.Q)) { ShowCommandPalette(); return true; }

            if (keyData == (Keys.Control | Keys.N)) { NewScene(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenScene(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveScene(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveScene(true); return true; }

            // Typing on the canvas: every other key belongs to the text — letters
            // don't switch tools, Delete doesn't delete objects, arrows don't nudge.
            // (Only while the viewport has focus: panel fields keep their keyboard.)
            if (sceneView.IsEditingText && sceneView.OwnsFocus(focused))
                return sceneView.TextKeyDown(keyData);

            // A focused inspector number field owns ↑ ↓, digits, Enter — they step or
            // start typing a value instead of nudging objects / switching views.
            var field = focused as Aplat.UI.DragNumber;
            if (field != null && Aplat.UI.DragNumber.OwnsKey(keyData))
                return false;

            Keys code = keyData & Keys.KeyCode;
            bool navigation = code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down ||
                code == Keys.Home || code == Keys.End || code == Keys.PageUp || code == Keys.PageDown;
            var combo = focused as ComboBox;
            bool plainInput = (keyData & (Keys.Control | Keys.Alt)) == Keys.None;
            if (combo != null && (combo.DroppedDown || (plainInput && (navigation || code == Keys.Enter ||
                code == Keys.Space || (code >= Keys.A && code <= Keys.Z) ||
                (code >= Keys.D0 && code <= Keys.D9) || (code >= Keys.NumPad0 && code <= Keys.NumPad9))))) return false;
            if (focused is Aplat.UI.ModernSlider && navigation) return false;

            if (IsEditingText(focused))
            {
                // While typing in a textbox/numeric field, the standard editing keys
                // must edit text (Ctrl+C copies text, Ctrl+Z undoes typing) — not the
                // scene. Returning false bypasses the menu shortcuts and lets the
                // focused control process the key natively.
                if (keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.X) ||
                    keyData == (Keys.Control | Keys.V) || keyData == (Keys.Control | Keys.A) ||
                    keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y))
                    return false;
            }
            else
            {
                // Ctrl+C/X/V work on a real object clipboard, Ctrl+D is a quick in-place
                // duplicate. Ctrl+C and Ctrl+D used to do the same thing, which made
                // "copy here, paste there" impossible.
                if (keyData == (Keys.Control | Keys.C))               { CopyToClipboard(); return true; }
                if (keyData == (Keys.Control | Keys.X))               { CutToClipboard();  return true; }
                if (keyData == (Keys.Control | Keys.V))               { PasteClipboard();  return true; }
                if (keyData == (Keys.Control | Keys.D))               { CopySelected();  return true; }
                if (keyData == (Keys.Control | Keys.A))               { SelectAll();     return true; }
                if (keyData == (Keys.Control | Keys.G))               { GroupSelected(); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.G))  { UngroupSelected(); return true; }
                if (keyData == (Keys.Control | Keys.Z))               { Undo(); return true; }
                if (keyData == (Keys.Control | Keys.Y))               { Redo(); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.Z))  { Redo(); return true; }
            }

            if (!IsEditingText(focused))
            {
                // Arrow keys nudge the selected figure(s) a tidy grid step; with no
                // selection they fall through to the viewport camera fly. Shift = fine.
                Keys kc = keyData & Keys.KeyCode;
                if (kc == Keys.Left || kc == Keys.Right || kc == Keys.Up || kc == Keys.Down)
                {
                    if (sceneView.HasLiveItem)
                    {
                        // A live shape / text moves by a layer pixel (Shift — ten).
                        sceneView.NudgeLive(kc == Keys.Left ? -1 : kc == Keys.Right ? 1 : 0,
                                            kc == Keys.Up   ? -1 : kc == Keys.Down  ? 1 : 0,
                                            (keyData & Keys.Shift) == Keys.Shift);
                        return true;
                    }
                    if (sceneView.SelectedObjects.Count > 0)
                    {
                        int sx = kc == Keys.Left ? -1 : kc == Keys.Right ? 1 : 0;
                        int sy = kc == Keys.Up   ?  1 : kc == Keys.Down  ? -1 : 0;
                        NudgeSelected(sx, sy, (keyData & Keys.Shift) == Keys.Shift);
                        return true;
                    }
                }

                switch (keyData)
                {
                    case Keys.Delete:
                        if (sceneView.HasLiveGroup) { sceneView.DeleteLive(); return true; }   // a lasso selection: gone for good
                        if (sceneView.CancelLive()) return true;   // removes the live element
                        DeleteSelected();
                        return true;
                    case Keys.Enter:
                        if (!sceneView.HasLiveItem) break;
                        if (sceneView.EditLiveText()) return true;   // text: back into typing (Figma)
                        sceneView.CommitLive();
                        return true;
                    case Keys.F:       sceneView.FocusSelection();   return true;

                    // Esc: a live shape / text / cut-out first, then selection, then Select.
                    case Keys.Escape:
                        // Live text: Esc means "done", never "delete what I typed".
                        if (sceneView.Live != null && sceneView.Live.IsText) { sceneView.CommitLive(); return true; }
                        if (sceneView.CancelLive()) return true;
                        if (sceneView.SelectedObjects.Count > 0) Select(null);
                        else SetTool(ToolMode.Select);
                        return true;

                    case Keys.F1:      SetTool(ToolMode.Select);     return true;
                    case Keys.F2:      SetTool(ToolMode.Move);       return true;
                    case Keys.F3:      SetTool(ToolMode.Rotate);     return true;
                    case Keys.F4:      SetTool(ToolMode.Scale);      return true;
                    case Keys.V:       SetTool(ToolMode.EditShape);  return true;
                    case Keys.B:       SetBrush(BrushKind.Brush);    return true;
                    case Keys.E:       if (ToggleEraser()) return true; break;
                    case Keys.R:       if (ToggleRuler()) return true; break;
                    case Keys.F5:      tbWireframe_Click(null, EventArgs.Empty); return true;
                    case Keys.F6:      tbVertices_Click(null, EventArgs.Empty);  return true;
                    case Keys.F7:      menuLight_Click(null, EventArgs.Empty); return true;
                    case Keys.F9:      ToggleAutoRotate(); return true;
                    case Keys.F8:      ToggleGrid();                 return true;

                    // Views on both the numpad and the top row (laptops without numpad).
                    case Keys.NumPad0: case Keys.D0: ApplyView(ViewMode.Perspective); return true;
                    case Keys.NumPad1: case Keys.D1: ApplyView(ViewMode.Front);       return true;
                    case Keys.NumPad3: case Keys.D3: ApplyView(ViewMode.Side);        return true;
                    case Keys.NumPad7: case Keys.D7: ApplyView(ViewMode.Top);         return true;

                    // [ / ] — brush size down / up (slider and label stay in sync).
                    case Keys.OemOpenBrackets:  AdjustBrushSize(-4); return true;
                    case Keys.OemCloseBrackets: AdjustBrushSize(+4); return true;

                    // Home — show the whole scene regardless of the selection.
                    case Keys.Home:
                        sceneView.FocusAll();
                        CaptureViewZoomBase();
                        UpdateViewZoomDisplay();
                        return true;

                    // PgUp / PgDn — raise / lower the selection along world Y
                    // (arrows move on the screen plane; this is the missing axis).
                    case Keys.PageUp:                  NudgeVertical(+1, false); return true;
                    case Keys.PageDown:                NudgeVertical(-1, false); return true;
                    case Keys.PageUp   | Keys.Shift:   NudgeVertical(+1, true);  return true;
                    case Keys.PageDown | Keys.Shift:   NudgeVertical(-1, true);  return true;
                }
            }
            return false;
        }

        private static bool IsEditingText(Control c)
        {
            return c is TextBoxBase || c is UpDownBase || (c != null && c.Parent is UpDownBase) ||
                (c is ComboBox && ((ComboBox)c).DropDownStyle != ComboBoxStyle.DropDownList);
        }

        private static Control FindFocused(Control root)
        {
            if (root.Focused) return root;
            foreach (Control child in root.Controls)
            {
                var f = FindFocused(child);
                if (f != null) return f;
            }
            return null;
        }

        private EditorShortcutFilter _shortcutFilter;

        private void InstallEditorShortcuts()
        {
            _shortcutFilter = new EditorShortcutFilter(this);
            Application.AddMessageFilter(_shortcutFilter);
            // A filter is thread-wide: release it even when the form is disposed directly.
            Disposed += (sender, args) => Application.RemoveMessageFilter(_shortcutFilter);
            FormClosed += (sender, args) => Application.RemoveMessageFilter(_shortcutFilter);
        }

        private bool IsEditorWindow(Form window)
        {
            return window == this || (window != null && window.Owner == this &&
                (window is Aplat.UI.OverlayWindow || (toolbarDock != null && toolbarDock.OwnsWindow(window))));
        }

        private sealed class EditorShortcutFilter : IMessageFilter
        {
            private readonly MainForm _owner;
            public EditorShortcutFilter(MainForm owner) { _owner = owner; }

            public bool PreFilterMessage(ref Message message)
            {
                const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
                if (message.Msg != WM_KEYDOWN && message.Msg != WM_SYSKEYDOWN) return false;
                if (_owner.IsDisposed || !_owner.Enabled || !_owner.Visible) return false;
                // Owned dialogs (including the command palette) retain their own keyboard.
                if (!_owner.IsEditorWindow(Form.ActiveForm)) return false;
                Control target = Control.FromChildHandle(message.HWnd);
                Form window = target != null ? target.FindForm() : null;
                if (!_owner.IsEditorWindow(window)) return false;
                Keys keys = (Keys)message.WParam.ToInt32() | Control.ModifierKeys;
                return _owner.TryHandleEditorShortcut(keys, FindFocused(window) ?? target);
            }
        }

        // ── 2D text on canvas ─────────────────────────────────────

        private void OnText2DClicked(object sender, Aplat.Controls.Text2DEventArgs e)
        {
            if (!ShowText2DDialog(out string text2d, out Font font2d, out Color color2d)) return;
            // The text becomes a live element: move, resize, rotate, double-click to
            // edit; Enter or a click outside merges it into the canvas, Esc removes it.
            sceneView.BeginLiveText(e.Canvas, e.Uv, text2d, font2d, color2d);
            _scene.IsModified = true;
            UpdateTitle();
        }

        private bool ShowText2DDialog(out string text, out Font font, out Color color,
                                      string initText = null, Font initFont = null, Color? initColor = null)
        {
            text  = null;
            font  = initFont != null ? (Font)initFont.Clone() : new Font("Segoe UI", 24F);
            color = initColor ?? sceneView.BrushColor;

            var fontHolder  = new Font[]  { font  };
            var colorHolder = new Color[] { color };

            using (var dlg = new Form())
            {
                dlg.Text            = Loc.T("Текст на холсте");
                dlg.ClientSize      = new Size(360, 196);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition   = FormStartPosition.CenterParent;
                dlg.MaximizeBox     = false;
                dlg.MinimizeBox     = false;
                Aplat.UI.ThemeManager.StyleDialog(dlg);

                var lblText  = new Label { Text = Loc.T("Текст:"), Location = new Point(12, 14), AutoSize = true };
                var txtText  = new TextBox { Location = new Point(12, 34), Width = 332 };

                if (initText != null) txtText.Text = initText;
                var btnFontPick = new Button
                {
                    Text      = Loc.T("Шрифт: ") + font.Name + " " + (int)font.Size + "pt",
                    Location  = new Point(12, 76),
                    Width     = 200, Height = 30,
                    FlatStyle = FlatStyle.System
                };
                var btnColorPick = new Button
                {
                    Text      = Loc.T("Цвет"),
                    Location  = new Point(224, 76),
                    Width     = 120, Height = 30,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = colorHolder[0],
                    ForeColor = Aplat.UI.Theme.ContrastText(colorHolder[0])
                };
                btnColorPick.FlatAppearance.BorderColor = Aplat.UI.Theme.Border;
                Aplat.UI.ThemeManager.KeepColors(btnColorPick);   // shows the text colour

                btnFontPick.Click += (s, ev) =>
                {
                    using (var fd = new FontDialog { Font = fontHolder[0], ShowEffects = false })
                    {
                        if (fd.ShowDialog(dlg) == DialogResult.OK)
                        {
                            fontHolder[0]   = fd.Font;
                            btnFontPick.Text = Loc.T("Шрифт: ") + fontHolder[0].Name +
                                               " " + (int)fontHolder[0].Size + "pt";
                        }
                    }
                };
                btnColorPick.Click += (s, ev) =>
                {
                    using (var cd = new ColorDialog { Color = colorHolder[0], FullOpen = true })
                    {
                        if (cd.ShowDialog(dlg) == DialogResult.OK)
                        {
                            colorHolder[0]        = cd.Color;
                            btnColorPick.BackColor = cd.Color;
                            btnColorPick.ForeColor = Aplat.UI.Theme.ContrastText(cd.Color);
                        }
                    }
                };

                var btnOk     = new Button { Text = Loc.T("ОК"),     DialogResult = DialogResult.OK,
                    Location = new Point(196, 152), Width = 72,  FlatStyle = FlatStyle.System };
                var btnCancel = new Button { Text = Loc.T("Отмена"), DialogResult = DialogResult.Cancel,
                    Location = new Point(280, 152), Width = 74,  FlatStyle = FlatStyle.System };

                dlg.Controls.AddRange(new Control[] {
                    lblText, txtText, btnFontPick, btnColorPick, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                txtText.SelectAll();

                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                text  = txtText.Text;
                font  = fontHolder[0];
                color = colorHolder[0];
                return !string.IsNullOrWhiteSpace(text);
            }
        }
    }
}
