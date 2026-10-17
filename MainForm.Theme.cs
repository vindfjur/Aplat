using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Aplat.UI;

namespace Aplat
{
    // Light / Graphite theme for the main window: the View → Theme menu, persisting
    // the choice, and the scene-dependent bits a generic re-colour can't know about.
    public partial class MainForm
    {
        private readonly Dictionary<ToolStripMenuItem, ThemeMode> _themeItems =
            new Dictionary<ToolStripMenuItem, ThemeMode>();

        private void SetThemeMode(ThemeMode mode)
        {
            Theme.SetMode(mode);
            SaveThemeMode(mode);
            SyncThemeMenu();
        }

        private void SyncThemeMenu()
        {
            foreach (var kv in _themeItems) kv.Key.Checked = kv.Value == Theme.Mode;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            if (IsDisposed) return;

            // A viewport still on the old theme's default follows the theme; a colour
            // the user picked (View → Canvas and grid) is theirs and stays.
            if (_scene != null && _scene.BackgroundColor.ToArgb() == e.Previous.SceneBackground.ToArgb())
            {
                _scene.BackgroundColor   = e.Current.SceneBackground;
                sceneView.BackgroundColor = e.Current.SceneBackground;
            }

            // Buttons that display data colours are exempt from the re-colour —
            // restore their contrast text and refresh the property editor.
            SetBrushSwatch(sceneView.BrushColor);
            propPanel.SetObject(_selected);
            listObjects.Invalidate();
            SyncThemeMenu();
            sceneView.Refresh3D();
        }

        // The language is saved at once and applies after a restart.
        private void ChooseLanguage(Lang lang)
        {
            if (lang == Loc.Current) return;
            Loc.Save(lang);
            var ask = MessageBox.Show(Loc.T("Язык интерфейса изменится после перезапуска. Перезапустить сейчас?\n(Несохранённые изменения будут предложены к сохранению.)"),
                "Aplat", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;
            bool closed = false;
            FormClosed += (s, e) => closed = true;
            Close();                       // asks about unsaved changes; the user may cancel
            if (closed)
            {
                try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Theme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }
}
