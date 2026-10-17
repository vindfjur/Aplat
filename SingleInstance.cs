using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Aplat
{
    // Double-clicking a .aplat in Explorer starts "Aplat.exe <file>". The first copy of the program keeps a
    // named pipe open; a second launch with a file hands the path to it and exits, so a project opens in the
    // window you already have (after asking about unsaved changes) instead of in a second, empty window.
    // The pipe is local to this machine and this user's session, and it carries one path, nothing else.
    internal static class SingleInstance
    {
        internal static readonly string MutexName = @"Local\Aplat-SingleInstance";
        private static readonly string PipeName = "Aplat.Open." + Environment.UserName.Replace('\\', '_');
        private const int MaxPath = 32767;

        // The first command-line argument that is an existing .aplat file, as a full path; otherwise null.
        internal static string FindProject(string[] args)
        {
            if (args == null) return null;
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                try
                {
                    string full = Path.GetFullPath(a.Trim('"'));
                    if (string.Equals(Path.GetExtension(full), ".aplat", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                        return full;
                }
                catch (Exception) { /* a bad argument is simply not a project */ }
            }
            return null;
        }

        // Second launch: give the path to the running copy. False if nobody answers (then run on our own).
        internal static bool Send(string path)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(4000);
                    var bytes = Encoding.UTF8.GetBytes(path);
                    c.Write(bytes, 0, bytes.Length);
                    c.Flush();
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        // First launch: wait for paths from later launches and open them in the main window.
        internal static void Listen(MainForm form)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            s.WaitForConnection();
                            var buf = new byte[MaxPath * 4]; int n = 0, r;
                            while (n < buf.Length && (r = s.Read(buf, n, buf.Length - n)) > 0) n += r;
                            string path = FindProject(new[] { Encoding.UTF8.GetString(buf, 0, n) });
                            if (path != null && !form.IsDisposed)
                                form.BeginInvoke((Action)(() => form.OpenExternal(path)));
                        }
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (InvalidOperationException) { return; }      // the window is gone
                    catch (IOException) { Thread.Sleep(200); }
                }
            });
            t.IsBackground = true; t.Name = "Aplat open-file pipe"; t.Start();
        }
    }
}
