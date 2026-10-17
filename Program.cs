using System;
using System.Threading;
using System.Windows.Forms;

namespace Aplat
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(Loc.T("Произошла непредвиденная ошибка:\n\n") + e.Exception.Message,
                    "Aplat", MessageBoxButtons.OK, MessageBoxIcon.Error);

            // A project named on the command line (double click on a .aplat): if Aplat is already running it
            // opens there, otherwise this copy starts with it.
            string project = SingleInstance.FindProject(args);
            bool first;
            using (var mutex = new Mutex(true, SingleInstance.MutexName, out first))
            {
                if (!first && project != null && SingleInstance.Send(project)) return;

                // The theme is chosen before any window exists, so everything is built
                // in the right palette from the first frame.
                Loc.Init();
                Aplat.UI.Theme.SetMode(MainForm.LoadThemeMode());
                var form = project != null ? new MainForm(project) : new MainForm();
                if (first) SingleInstance.Listen(form);
                Application.Run(form);
            }
        }
    }
}
