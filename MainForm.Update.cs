using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aplat
{
    // Help → Check for updates, and the quiet check at start-up (at most once a day, can be turned off).
    // Nothing is downloaded or installed: a newer release only offers to open its page in the browser.
    public partial class MainForm
    {
        private static string UpdatesPath { get { return Path.Combine(SettingsDir, "updates.txt"); } }
        private bool _autoUpdate = true;
        private System.Windows.Forms.ToolStripMenuItem menuUpdates, menuAutoUpdate;

        // updates.txt: first line "on" or "off", second line the date of the last check (yyyy-MM-dd).
        private string LoadUpdateSettings()
        {
            string last = "";
            try
            {
                if (File.Exists(UpdatesPath))
                {
                    var lines = File.ReadAllLines(UpdatesPath);
                    if (lines.Length > 0) _autoUpdate = lines[0].Trim() != "off";
                    if (lines.Length > 1) last = lines[1].Trim();
                }
            }
            catch { }
            return last;
        }

        private void SaveUpdateSettings(string last)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(UpdatesPath, (_autoUpdate ? "on" : "off") + "\n" + last + "\n");
            }
            catch { }
        }

        private void BuildUpdateMenu()
        {
            menuUpdates = MI(Loc.T("Проверить обновления…"), (s, e) => CheckForUpdates(true),
                System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Refresh);
            menuAutoUpdate = MI(Loc.T("Проверять обновления при запуске"), (s, e) =>
            {
                // Read the saved date first: loading also resets the flag from the file, which would undo the click.
                bool next = !_autoUpdate;
                string last = LoadUpdateSettings();
                _autoUpdate = next;
                ShowAutoUpdateState();
                SaveUpdateSettings(last);
            }, System.Windows.Forms.Keys.None, Aplat.UI.UiIcon.Bell);
            LoadUpdateSettings();
            ShowAutoUpdateState();
            menuHelp.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            menuHelp.DropDownItems.Add(menuUpdates);
            menuHelp.DropDownItems.Add(menuAutoUpdate);
        }

        // ON: a bell lit in the accent well (the renderer's checked look); OFF: a plain crossed-out bell. The
        // state must read without a tick that looks the same either way, in both themes.
        private void ShowAutoUpdateState()
        {
            menuAutoUpdate.Checked = _autoUpdate;
            var icon = _autoUpdate ? Aplat.UI.UiIcon.Bell : Aplat.UI.UiIcon.BellOff;
            menuAutoUpdate.Tag = icon;
            menuAutoUpdate.Image = Aplat.UI.IconFactory.Create(icon, Aplat.UI.Theme.Text, 18);
        }

        // Quiet check at start-up: only a newer version is reported, once a day at most.
        private void StartUpdateCheck()
        {
            string last = LoadUpdateSettings();
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_autoUpdate || last == today) return;
            SaveUpdateSettings(today);
            CheckForUpdates(false);
        }

        private void CheckForUpdates(bool manual)
        {
            if (manual && menuUpdates != null) menuUpdates.Enabled = false;
            Task.Run(() => UpdateCheck.Fetch()).ContinueWith(t =>
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        if (menuUpdates != null) menuUpdates.Enabled = true;
                        ShowUpdateResult(t.Status == TaskStatus.RanToCompletion ? t.Result : new UpdateCheck.Result(), manual);
                    }));
                }
                catch (InvalidOperationException) { }    // the window was closed meanwhile
            });
        }

        private void ShowUpdateResult(UpdateCheck.Result r, bool manual)
        {
            if (r.NoReleases)
            {
                if (manual)
                    MessageBox.Show(this, Loc.T("На GitHub пока нет опубликованных версий Aplat."), Loc.T("Обновления"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!r.Ok)
            {
                if (manual)
                    MessageBox.Show(this, Loc.T("Не удалось проверить обновления. Проверьте подключение к интернету."), Loc.T("Обновления"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!r.Newer)
            {
                if (manual)
                    MessageBox.Show(this, string.Format(Loc.T("У вас последняя версия Aplat ({0})."), UpdateCheck.Current), Loc.T("Обновления"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var ask = MessageBox.Show(this, string.Format(Loc.T("Вышла новая версия Aplat: {0} (у вас {1}). Открыть страницу загрузки?"), r.Tag, UpdateCheck.Current),
                Loc.T("Обновления"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (ask == DialogResult.Yes)
                try { System.Diagnostics.Process.Start(UpdateCheck.ReleasePage); } catch { }
        }
    }
}
