using System;
using System.IO;
using System.Windows.Forms;

namespace Aplat
{
    // Opening a project that came from outside: a double click in Explorer, or "Open with".
    public partial class MainForm
    {
        private string _startupProject;

        // At start-up: the project named on the command line opens as soon as the window is shown.
        public MainForm(string startupProject) : this()
        {
            _startupProject = startupProject;
            Shown += (s, e) =>
            {
                string p = _startupProject; _startupProject = null;
                if (p != null) LoadSceneFromFile(p);
            };
        }

        // A project handed over by a second launch of the program (see SingleInstance). With unsaved changes
        // the user chooses: save the current scene and open, open without saving, or cancel.
        internal void OpenExternal(string path)
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (!Enabled) return;                 // a modal dialog is open: do not stack another one on it
            if (_scene.IsModified)
            {
                var r = MessageBox.Show(this,
                    string.Format(Loc.T("Открыть проект «{0}»?\n\nТекущая сцена изменена. «Да» — сохранить её и открыть, «Нет» — открыть без сохранения, «Отмена» — остаться."),
                        Path.GetFileNameWithoutExtension(path)),
                    "Aplat", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) { SaveScene(); if (_scene.IsModified) return; }
            }
            LoadSceneFromFile(path);
        }
    }
}
