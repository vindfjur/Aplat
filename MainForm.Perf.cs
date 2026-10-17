using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aplat
{
    // View → Show FPS: a small plate in the top-left corner of the viewport with the frames drawn per second,
    // the share of the processor this program uses and its memory. It is off by default and the choice is
    // remembered in %AppData%\Aplat\perf.txt (first line "on" / "off", optional second line "x y": where the user
    // dragged the plate, in pixels from the viewport's top-left corner). At rest the view is not redrawn, so the
    // frame rate shows a dash. Double click on the plate puts it back in the corner.
    public partial class MainForm
    {
        private const int PerfIntervalMs = 500;
        private const int PerfHotCpuPercent = 60;

        private static string PerfPath { get { return Path.Combine(SettingsDir, "perf.txt"); } }

        private Aplat.UI.PerfHud _perfHud;
        private Timer _perfTimer;
        private bool _showPerf;
        private Point? _perfOffset;   // null = the default corner
        private static readonly Point PerfCorner = new Point(16, 16);
        private long _perfFrames;
        private TimeSpan _perfCpu;
        private DateTime _perfAt;
        private System.Windows.Forms.ToolStripMenuItem menuPerf;

        private void BuildPerfMenu()
        {
            menuPerf = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Показывать FPS"));
            menuPerf.CheckOnClick = false;
            menuPerf.Image = Aplat.UI.IconFactory.Create(Aplat.UI.UiIcon.Activity, Aplat.UI.Theme.Text, 18);
            menuPerf.Tag = Aplat.UI.UiIcon.Activity;
            menuPerf.Click += (s, e) => SetShowPerf(!_showPerf, true);
            LoadPerf();
            menuPerf.Checked = _showPerf;
            this.Shown += (s, e) => { if (_showPerf) SetShowPerf(true, false); };
            this.FormClosed += (s, e) => StopPerf();
        }

        private void LoadPerf()
        {
            try
            {
                if (!File.Exists(PerfPath)) return;
                string[] lines = File.ReadAllLines(PerfPath);
                _perfOffset = null;
                if (lines.Length > 0) _showPerf = lines[0].Trim() == "on";
                if (lines.Length > 1)
                {
                    string[] xy = lines[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x, y;
                    if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y)) _perfOffset = new Point(x, y);
                }
            }
            catch { }
        }

        private void SavePerf()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string text = _showPerf ? "on" : "off";
                if (_perfOffset.HasValue) text += "\n" + _perfOffset.Value.X + " " + _perfOffset.Value.Y;
                File.WriteAllText(PerfPath, text + "\n");
            }
            catch { }
        }

        private void SetShowPerf(bool on, bool save)
        {
            _showPerf = on;
            menuPerf.Checked = on;
            if (save) SavePerf();
            if (on) StartPerf(); else StopPerf();
        }

        private void StartPerf()
        {
            if (_perfHud != null || !IsHandleCreated) return;
            _perfHud = new Aplat.UI.PerfHud();
            _perfHud.Owner = this;
            _perfHud.AreaProvider = PerfArea;
            _perfHud.Dragged += (s, e) =>
            {
                Point origin = PerfArea().Location;
                _perfOffset = new Point(_perfHud.Left - origin.X, _perfHud.Top - origin.Y);
                SavePerf();
            };
            _perfHud.ResetRequested += (s, e) =>
            {
                _perfOffset = null;
                PositionPerfHud();
                SavePerf();
            };
            _perfFrames = sceneView.FrameCount;
            _perfCpu = Process.GetCurrentProcess().TotalProcessorTime;
            _perfAt = DateTime.UtcNow;
            _perfHud.SetValues(null, 0, MemoryMb(), false);
            _perfHud.Show();
            PositionPerfHud();
            _perfTimer = new Timer { Interval = PerfIntervalMs };
            _perfTimer.Tick += (s, e) => UpdatePerf();
            _perfTimer.Start();
        }

        private void StopPerf()
        {
            if (_perfTimer != null) { _perfTimer.Stop(); _perfTimer.Dispose(); _perfTimer = null; }
            if (_perfHud != null) { _perfHud.Close(); _perfHud.Dispose(); _perfHud = null; }
        }

        private static int MemoryMb()
        {
            using (var p = Process.GetCurrentProcess()) return (int)(p.WorkingSet64 / (1024 * 1024));
        }

        private void UpdatePerf()
        {
            if (_perfHud == null) return;
            DateTime now = DateTime.UtcNow;
            double seconds = (now - _perfAt).TotalSeconds;
            if (seconds < 0.05) return;
            long frames = sceneView.FrameCount;
            TimeSpan cpu;
            int mem;
            using (var p = Process.GetCurrentProcess()) { cpu = p.TotalProcessorTime; mem = (int)(p.WorkingSet64 / (1024 * 1024)); }

            int? fps = frames > _perfFrames ? (int?)(int)Math.Round((frames - _perfFrames) / seconds) : null;
            int load = (int)Math.Round((cpu - _perfCpu).TotalSeconds / (seconds * Environment.ProcessorCount) * 100);
            load = Math.Max(0, Math.Min(100, load));
            _perfHud.SetValues(fps, load, mem, load >= PerfHotCpuPercent);

            _perfFrames = frames; _perfCpu = cpu; _perfAt = now;
        }

        private Rectangle PerfArea()
        {
            return panelMain.RectangleToScreen(panelMain.ClientRectangle);
        }

        // The remembered spot inside the viewport, or its top-left corner (clear of the toolbar cards, which sit
        // top-centre). Always clamped, so a smaller window never leaves the plate outside.
        private void PositionPerfHud()
        {
            if (_perfHud == null || panelMain == null || !panelMain.IsHandleCreated) return;
            Rectangle area = PerfArea();
            Point off = _perfOffset ?? PerfCorner;
            _perfHud.Location = Aplat.UI.PerfHud.ClampTo(new Point(area.X + off.X, area.Y + off.Y), _perfHud.Size, area);
        }
    }
}
