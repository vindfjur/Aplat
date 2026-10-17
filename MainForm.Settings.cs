using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aplat
{
    // Lightweight user preferences in %AppData%\Aplat: the recent-files list,
    // the last window placement and the theme. Plain text, fail-safe — any IO
    // error is swallowed, preferences must never break the app.
    public partial class MainForm
    {
        private const int RecentMax = 8;
        private readonly List<string> _recentFiles = new List<string>();

        private static string SettingsDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aplat");
            }
        }

        private static string RecentPath { get { return Path.Combine(SettingsDir, "recent.txt"); } }
        private static string WindowPath { get { return Path.Combine(SettingsDir, "window.txt"); } }
        private static string ThemePath  { get { return Path.Combine(SettingsDir, "theme.txt"); } }
        private static string ToolbarsPath { get { return Path.Combine(SettingsDir, "toolbars.txt"); } }

        // ── Theme (View → Theme) ──────────────────────────────────
        // Read by Program before the window is built, so it opens in its theme.
        internal static Aplat.UI.ThemeMode LoadThemeMode()
        {
            try
            {
                if (!File.Exists(ThemePath)) return Aplat.UI.ThemeMode.Light;
                Aplat.UI.ThemeMode mode;
                string raw = File.ReadAllText(ThemePath).Trim();
                if (Enum.TryParse(raw, true, out mode) && Enum.IsDefined(typeof(Aplat.UI.ThemeMode), mode))
                    return mode;
            }
            catch { }
            return Aplat.UI.ThemeMode.Light;
        }

        private static void SaveThemeMode(Aplat.UI.ThemeMode mode)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(ThemePath, mode.ToString());
            }
            catch { }
        }

        // ── Recent files (File → Recent) ──────────────────────────
        private void LoadRecentFiles()
        {
            _recentFiles.Clear();
            try
            {
                if (!File.Exists(RecentPath)) return;
                foreach (var raw in File.ReadAllLines(RecentPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || _recentFiles.Contains(line)) continue;
                    _recentFiles.Add(line);
                    if (_recentFiles.Count >= RecentMax) break;
                }
            }
            catch { }
        }

        private void SaveRecentFiles()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllLines(RecentPath, _recentFiles.ToArray());
            }
            catch { }
        }

        private void AddRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _recentFiles.Insert(0, path);
            while (_recentFiles.Count > RecentMax) _recentFiles.RemoveAt(_recentFiles.Count - 1);
            SaveRecentFiles();
        }

        // Rebuild the submenu each time it opens, so it always shows the live list.
        private void menuRecent_DropDownOpening(object sender, EventArgs e)
        {
            menuRecent.DropDownItems.Clear();
            foreach (var p in _recentFiles)
            {
                string path = p;
                var item = new ToolStripMenuItem(Path.GetFileName(path))
                {
                    ToolTipText = path,
                    Enabled     = File.Exists(path)
                };
                item.Click += (s2, e2) => OpenRecent(path);
                menuRecent.DropDownItems.Add(item);
            }
            if (menuRecent.DropDownItems.Count == 0)
                menuRecent.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Список пуст")) { Enabled = false });
        }

        private void OpenRecent(string path)
        {
            if (!File.Exists(path))
            {
                statusLabel.Text = Loc.T("Файл не найден: ") + path;
                _recentFiles.Remove(path);
                SaveRecentFiles();
                return;
            }
            if (!ConfirmDiscard()) return;
            LoadSceneFromFile(path);
        }

        // ── Window placement (restored on the next launch) ────────
        private void LoadWindowPlacement()
        {
            try
            {
                if (!File.Exists(WindowPath)) return;
                var parts = File.ReadAllText(WindowPath).Split(';');
                if (parts.Length < 5) return;
                var r = new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]),
                                      int.Parse(parts[2]), int.Parse(parts[3]));
                if (r.Width < 400 || r.Height < 300) return;

                // Only restore a rect that still lands on a connected screen
                // (the monitor could have been unplugged since the last run).
                bool visible = false;
                foreach (var sc in Screen.AllScreens)
                    if (sc.WorkingArea.IntersectsWith(r)) { visible = true; break; }
                if (!visible) return;

                StartPosition = FormStartPosition.Manual;
                Bounds = r;
                if (parts[4] == "1") WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(WindowPath, string.Format("{0};{1};{2};{3};{4}",
                    r.X, r.Y, r.Width, r.Height,
                    WindowState == FormWindowState.Maximized ? "1" : "0"));
            }
            catch { }
        }
    }
}
