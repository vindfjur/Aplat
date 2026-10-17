using System;
using System.Drawing;
using System.Windows.Forms;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Dialogs
{
    // Unified "Canvas and grid" dialog: edits the drawing sheet (size in pixels +
    // colour) AND the grid / background settings, in one place. Canvas size is
    // stored in abstract world units but shown in real pixels via PixelsPerUnit;
    // a "keep proportions" toggle keeps the aspect ratio while resizing.
    public class CanvasSettingsDialog : Form
    {
        private const float PixelsPerUnit = 256f;
        private const int   MinPx = 64, MaxPx = 8192;

        // Canvas
        private readonly Aplat.UI.DragNumber _cw       = new Aplat.UI.DragNumber();
        private readonly Aplat.UI.DragNumber _ch       = new Aplat.UI.DragNumber();
        private readonly CheckBox      _keepRatio = new Aplat.UI.ModernCheckBox();
        private readonly Panel         _swCanvas = new Panel();
        // Grid / background
        private readonly CheckBox      _chkGrid    = new Aplat.UI.ModernCheckBox();
        private readonly CheckBox      _chkFixedGrid = new Aplat.UI.ModernCheckBox();
        private readonly Aplat.UI.DragNumber _numExtent  = new Aplat.UI.DragNumber();
        private readonly Aplat.UI.DragNumber _numSpacing = new Aplat.UI.DragNumber();
        private readonly Panel         _swGrid     = new Panel();
        private readonly Panel         _swBack     = new Panel();

        private float _ratio;
        private bool  _syncing;

        // Canvas results (world units / colour).
        public float CanvasWidth  { get; private set; }
        public float CanvasHeight { get; private set; }
        public Color CanvasColor  { get; private set; }
        // Grid / background results.
        public bool  ShowGrid        { get; private set; }
        public bool  FixedGrid       { get; private set; }
        public float GridExtent      { get; private set; }
        public float GridSpacing     { get; private set; }
        public Color BackgroundColor { get; private set; }
        public Color GridColor       { get; private set; }

        public CanvasSettingsDialog(float canvasW, float canvasH, Color canvasColor,
                                    bool showGrid, float extent, float spacing,
                                    Color background, Color gridColor, bool fixedGrid)
        {
            CanvasWidth = canvasW; CanvasHeight = canvasH; CanvasColor = canvasColor;
            ShowGrid = showGrid; GridExtent = extent; GridSpacing = spacing;
            BackgroundColor = background; GridColor = gridColor; FixedGrid = fixedGrid;

            int pw = ToPx(canvasW), ph = ToPx(canvasH);
            _ratio = ph > 0 ? (float)pw / ph : 1f;

            Text            = Loc.T("Холст и сетка");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ClientSize      = new Size(340, 486);
            Aplat.UI.ThemeManager.StyleDialog(this);

            int x1 = 18, x2 = 170, y = 14, row = 38;

            Section(Loc.T("Холст"), x1, ref y);

            AddLabel(Loc.T("Ширина (px):"), x1, y + 3);
            SetupNum(_cw, pw, x2, y); _cw.ValueChanged += OnWidthChanged;
            Controls.Add(_cw); y += row;

            AddLabel(Loc.T("Высота (px):"), x1, y + 3);
            SetupNum(_ch, ph, x2, y); _ch.ValueChanged += OnHeightChanged;
            Controls.Add(_ch); y += row;

            _keepRatio.Text = Loc.T("Сохранить пропорции");
            _keepRatio.Location = new Point(x1, y + 2);
            _keepRatio.AutoSize = true;
            _keepRatio.Checked  = true;
            _keepRatio.CheckedChanged += (s, e) =>
            { if (_keepRatio.Checked && _ch.Value > 0) _ratio = (float)_cw.Value / (float)_ch.Value; };
            Controls.Add(_keepRatio); y += row;

            AddLabel(Loc.T("Цвет холста:"), x1, y + 3);
            Swatch(_swCanvas, CanvasColor, x2, y, (s, e) => Pick(_swCanvas, ref _c));
            y += row + 6;

            Section(Loc.T("Сетка и фон"), x1, ref y);

            _chkGrid.Text = Loc.T("Показывать сетку");
            _chkGrid.Location = new Point(x1, y + 2);
            _chkGrid.AutoSize = true;
            _chkGrid.Checked  = ShowGrid;
            _chkGrid.CheckedChanged += (s, e) => SyncEnabled();
            Controls.Add(_chkGrid); y += row;

            _chkFixedGrid.Text = Loc.T("Фиксировать сетку на Y = 0");
            _chkFixedGrid.Location = new Point(x1, y + 2);
            _chkFixedGrid.AutoSize = true;
            _chkFixedGrid.Checked = FixedGrid;
            Controls.Add(_chkFixedGrid); y += row;

            AddLabel(Loc.T("Размер сетки:"), x1, y + 3);
            SetupDec(_numExtent, 1, 0.5m, 1m, 100m, Clamp((decimal)GridExtent, 1m, 100m), x2, y);
            Controls.Add(_numExtent); y += row;

            AddLabel(Loc.T("Шаг сетки:"), x1, y + 3);
            SetupDec(_numSpacing, 2, 0.1m, 0.05m, 20m, Clamp((decimal)GridSpacing, 0.05m, 20m), x2, y);
            Controls.Add(_numSpacing); y += row;

            AddLabel(Loc.T("Цвет сетки:"), x1, y + 3);
            Swatch(_swGrid, GridColor, x2, y, (s, e) => Pick(_swGrid, ref _g));
            y += row;

            AddLabel(Loc.T("Цвет фона:"), x1, y + 3);
            Swatch(_swBack, BackgroundColor, x2, y, (s, e) => Pick(_swBack, ref _b));
            y += row + 4;

            var btnReset = new Button { Text = Loc.T("Сброс"), Location = new Point(x1, y), Width = 80, FlatStyle = FlatStyle.System };
            btnReset.Click += OnReset;
            Controls.Add(btnReset);

            var ok = new Button { Text = Loc.T("ОК"), DialogResult = DialogResult.OK,
                Location = new Point(x2 + 38, y), Width = 54, FlatStyle = FlatStyle.System };
            ok.Click += OnOk;
            Controls.Add(ok);

            var cancel = new Button { Text = Loc.T("Отмена"), DialogResult = DialogResult.Cancel,
                Location = new Point(x2 + 96, y), Width = 72, FlatStyle = FlatStyle.System };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            SyncEnabled();
        }

        // Scratch colours edited by the pickers (committed on OK).
        private Color _c, _g, _b;

        private void OnOk(object sender, EventArgs e)
        {
            CanvasWidth     = (float)_cw.Value / PixelsPerUnit;
            CanvasHeight    = (float)_ch.Value / PixelsPerUnit;
            CanvasColor     = _swCanvas.BackColor;
            ShowGrid        = _chkGrid.Checked;
            FixedGrid       = _chkFixedGrid.Checked;
            GridExtent      = (float)_numExtent.Value;
            GridSpacing     = (float)_numSpacing.Value;
            GridColor       = _swGrid.BackColor;
            BackgroundColor = _swBack.BackColor;
        }

        private void OnReset(object sender, EventArgs e)
        {
            _chkGrid.Checked  = true;
            _chkFixedGrid.Checked = false;
            _numExtent.Value  = 5m;
            _numSpacing.Value = 0.5m;
            _swGrid.BackColor = SceneGeometry.DefaultGridColor;
            _swBack.BackColor = Aplat.UI.Theme.SceneBackground;   // the current theme's default
            SyncEnabled();
        }

        private void SyncEnabled()
        {
            bool on = _chkGrid.Checked;
            _numExtent.Enabled  = on;
            _numSpacing.Enabled = on;
            _swGrid.Enabled     = on;
            _chkFixedGrid.Enabled = on;
        }

        private void OnWidthChanged(object sender, EventArgs e)
        {
            if (_syncing || !_keepRatio.Checked) return;
            _syncing = true;
            _ch.Value = ClampPx((decimal)Math.Round((float)_cw.Value / _ratio));
            _syncing = false;
        }

        private void OnHeightChanged(object sender, EventArgs e)
        {
            if (_syncing || !_keepRatio.Checked) return;
            _syncing = true;
            _cw.Value = ClampPx((decimal)Math.Round((float)_ch.Value * _ratio));
            _syncing = false;
        }

        // ── helpers ───────────────────────────────────────────────
        private static int ToPx(float u)
        {
            int px = (int)Math.Round(u * PixelsPerUnit);
            return px < MinPx ? MinPx : (px > MaxPx ? MaxPx : px);
        }
        private static decimal ClampPx(decimal v) { return v < MinPx ? MinPx : (v > MaxPx ? MaxPx : v); }
        private static decimal Clamp(decimal v, decimal lo, decimal hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // Drag fields (as in the inspector): drag to change, click to type, wheel / ↑↓
        // when focused. Unlike NumericUpDown they stay readable when disabled on the
        // dark theme, and an out-of-range Value is clamped instead of throwing.
        private static void SetupNum(Aplat.UI.DragNumber n, int px, int x, int y)
        {
            n.DecimalPlaces = 0; n.Increment = 16m; n.Minimum = MinPx; n.Maximum = MaxPx;
            n.DragStep = 4m; n.SnapStep = 64m;
            n.Value = px; n.SetBounds(x, y - 1, 150, 28);
        }
        private static void SetupDec(Aplat.UI.DragNumber n, int dp, decimal inc, decimal lo, decimal hi,
                                     decimal val, int x, int y)
        {
            n.DecimalPlaces = dp; n.Increment = inc; n.Minimum = lo; n.Maximum = hi;
            n.DragStep = inc / 5m; n.SnapStep = inc;
            n.Value = val; n.SetBounds(x, y - 1, 150, 28);
        }

        private void Section(string text, int x, ref int y)
        {
            // Same "SECTION ─────" marker as the side panel.
            Controls.Add(new Aplat.UI.SectionHeader(text)
            {
                Location = new Point(x, y),
                Width    = ClientSize.Width - 2 * x,
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });
            y += 26;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true,
                ForeColor = Aplat.UI.Theme.Text });
        }

        private void Swatch(Panel sw, Color col, int x, int y, EventHandler onClick)
        {
            sw.Location = new Point(x, y); sw.Size = new Size(150, 24);
            sw.BackColor = col; sw.BorderStyle = BorderStyle.FixedSingle; sw.Cursor = Cursors.Hand;
            Aplat.UI.ThemeManager.KeepColors(sw);   // the colour is the setting itself
            sw.Click += onClick;
            Controls.Add(sw);
        }

        private void Pick(Panel sw, ref Color slot)
        {
            using (var d = new ColorDialog { Color = sw.BackColor, FullOpen = true })
                if (d.ShowDialog(this) == DialogResult.OK) { sw.BackColor = d.Color; slot = d.Color; }
        }
    }

    // Dialog for the real light source (the "sun"): direction, strength, colour,
    // highlights and the floor shadow. Changes apply immediately — the caller
    // passes onPreview so the scene is redrawn on every slider move; "Cancel"
    // restores the original values.
    //
    // Kept in the same file as CanvasSettingsDialog because the project is built
    // from an explicit <Compile> list in the .csproj.
    public class LightSettingsDialog : Form
    {
        private readonly LightSettings _light;      // edited in place
        private readonly LightSettings _backup;     // for "Cancel"
        private readonly Action _preview;

        private readonly CheckBox _chkEnabled  = new Aplat.UI.ModernCheckBox();
        private readonly CheckBox _chkSpecular = new Aplat.UI.ModernCheckBox();
        private readonly CheckBox _chkShadows  = new Aplat.UI.ModernCheckBox();
        private readonly CheckBox _chkShowSun  = new Aplat.UI.ModernCheckBox();

        private readonly Aplat.UI.ModernSlider _trkTime      = new Aplat.UI.ModernSlider();
        private readonly Aplat.UI.ModernSlider _trkAzimuth   = new Aplat.UI.ModernSlider();
        private readonly Aplat.UI.ModernSlider _trkElevation = new Aplat.UI.ModernSlider();
        private readonly Aplat.UI.ModernSlider _trkIntensity = new Aplat.UI.ModernSlider();
        private readonly Aplat.UI.ModernSlider _trkAmbient   = new Aplat.UI.ModernSlider();
        private readonly Aplat.UI.ModernSlider _trkShadow    = new Aplat.UI.ModernSlider();

        private readonly Label _lblTime = new Label();
        private readonly Label _lblAz   = new Label();
        private readonly Label _lblEl   = new Label();
        private readonly Label _lblInt  = new Label();
        private readonly Label _lblAmb  = new Label();
        private readonly Label _lblSh   = new Label();

        private readonly Panel _swSun     = new Panel();
        private readonly Panel _swAmbient = new Panel();

        private bool _loading;
        private readonly bool _shadowsPossible = !Aplat.Rendering.ShadowSupport.Known || Aplat.Rendering.ShadowSupport.Available;

        public LightSettingsDialog(LightSettings light, Action onPreview)
        {
            _light   = light ?? new LightSettings();
            _backup  = _light.Clone();
            _preview = onPreview;

            Text            = Loc.T("Освещение и солнце");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ClientSize      = new Size(392, 566);
            Aplat.UI.ThemeManager.StyleDialog(this);

            int x1 = 18, x2 = 150, y = 14, row = 34;

            _chkEnabled.Text = Loc.T("Включить источник света");
            _chkEnabled.Location = new Point(x1, y);
            _chkEnabled.AutoSize = true;
            _chkEnabled.Checked = _light.Enabled;
            _chkEnabled.CheckedChanged += (s, e) => { _light.Enabled = _chkEnabled.Checked; SyncEnabled(); Apply(); };
            Controls.Add(_chkEnabled); y += row;

            Section(Loc.T("Положение солнца"), x1, ref y);

            AddLabel(Loc.T("Время суток:"), x1, y + 4);
            Slider(_trkTime, 0, 240, (int)Math.Round(GuessHours() * 10f), x2, y);
            _trkTime.Scroll += (s, e) =>
            {
                if (_loading) return;
                _light.ApplyTimeOfDay(_trkTime.Value / 10f);
                LoadFromLight(false);
                Apply();
            };
            Value(_lblTime, y); y += row;

            AddLabel(Loc.T("Азимут:"), x1, y + 4);
            Slider(_trkAzimuth, 0, 360, (int)Norm360(_light.Azimuth), x2, y);
            _trkAzimuth.Scroll += (s, e) => { if (_loading) return; _light.Azimuth = _trkAzimuth.Value; Labels(); Apply(); };
            Value(_lblAz, y); y += row;

            AddLabel(Loc.T("Высота:"), x1, y + 4);
            Slider(_trkElevation, -10, 90, (int)Math.Round(_light.Elevation), x2, y);
            _trkElevation.Scroll += (s, e) => { if (_loading) return; _light.Elevation = _trkElevation.Value; Labels(); Apply(); };
            Value(_lblEl, y); y += row + 6;

            Section(Loc.T("Сила и цвет"), x1, ref y);

            AddLabel(Loc.T("Яркость:"), x1, y + 4);
            Slider(_trkIntensity, 0, 200, (int)Math.Round(_light.Intensity * 100f), x2, y);
            _trkIntensity.Scroll += (s, e) => { if (_loading) return; _light.Intensity = _trkIntensity.Value / 100f; Labels(); Apply(); };
            Value(_lblInt, y); y += row;

            AddLabel(Loc.T("Рассеянный свет:"), x1, y + 4);
            Slider(_trkAmbient, 0, 100, (int)Math.Round(_light.Ambient * 100f), x2, y);
            _trkAmbient.Scroll += (s, e) => { if (_loading) return; _light.Ambient = _trkAmbient.Value / 100f; Labels(); Apply(); };
            Value(_lblAmb, y); y += row;

            AddLabel(Loc.T("Цвет солнца:"), x1, y + 4);
            SunSwatch(_swSun, _light.Color, x2, y, true);
            y += row;

            AddLabel(Loc.T("Цвет заполнения:"), x1, y + 4);
            SunSwatch(_swAmbient, _light.AmbientColor, x2, y, false);
            y += row + 6;

            Section(Loc.T("Блики и тень"), x1, ref y);

            _chkSpecular.Text = Loc.T("Блики на поверхности объектов");
            _chkSpecular.Location = new Point(x1, y);
            _chkSpecular.AutoSize = true;
            _chkSpecular.Checked = _light.Specular;
            _chkSpecular.CheckedChanged += (s, e) => { _light.Specular = _chkSpecular.Checked; Apply(); };
            Controls.Add(_chkSpecular); y += 28;

            _chkShadows.Text = Loc.T("Тени");
            _chkShadows.Location = new Point(x1, y);
            _chkShadows.AutoSize = true;
            _chkShadows.Checked = _light.Shadows;
            _chkShadows.CheckedChanged += (s, e) => { _light.Shadows = _chkShadows.Checked; SyncEnabled(); Apply(); };
            Controls.Add(_chkShadows); y += 30;

            // No framebuffers / GLSL in this OpenGL: the switch stays visible but cannot be used.
            if (!_shadowsPossible)
            {
                int w = ClientSize.Width - 2 * x1;
                var note = new Label
                {
                    Text = string.Format(Loc.T("Тени: несовместимо с вашей версией OpenGL ({0})"), Aplat.Rendering.ShadowSupport.Version),
                    AutoSize = false, Font = Font, ForeColor = Aplat.UI.Theme.TextSoft
                };
                int h = note.GetPreferredSize(new Size(w, 0)).Height;   // wraps to as many lines as it needs
                note.SetBounds(x1, y - 4, w, h);
                Controls.Add(note);
                y += h + 6;
            }

            AddLabel(Loc.T("Плотность тени:"), x1, y + 4);
            Slider(_trkShadow, 0, 100, (int)Math.Round(_light.ShadowStrength * 100f), x2, y);
            _trkShadow.Scroll += (s, e) => { if (_loading) return; _light.ShadowStrength = _trkShadow.Value / 100f; Labels(); Apply(); };
            Value(_lblSh, y); y += row;

            _chkShowSun.Text = Loc.T("Показывать метку источника (для настройки)");
            _chkShowSun.Location = new Point(x1, y);
            _chkShowSun.AutoSize = true;
            _chkShowSun.Checked = _light.ShowSun;
            _chkShowSun.CheckedChanged += (s, e) => { _light.ShowSun = _chkShowSun.Checked; Apply(); };
            Controls.Add(_chkShowSun); y += 38;

            var reset = new Button { Text = Loc.T("Сброс"), Location = new Point(x1, y), Width = 80, FlatStyle = FlatStyle.System };
            reset.Click += (s, e) => { CopyInto(new LightSettings(), _light); LoadFromLight(true); SyncEnabled(); Apply(); };
            Controls.Add(reset);

            var ok = new Button { Text = Loc.T("ОК"), DialogResult = DialogResult.OK,
                Location = new Point(212, y), Width = 74, FlatStyle = FlatStyle.System };
            Controls.Add(ok);

            var cancel = new Button { Text = Loc.T("Отмена"), DialogResult = DialogResult.Cancel,
                Location = new Point(294, y), Width = 80, FlatStyle = FlatStyle.System };
            cancel.Click += (s, e) => { CopyInto(_backup, _light); Apply(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, ok.Bottom + 36);   // the message above may add a row
            Labels();
            SyncEnabled();
        }

        private void Apply() { if (_preview != null) _preview(); }

        private void SyncEnabled()
        {
            bool on = _chkEnabled.Checked;
            foreach (Control c in new Control[] {
                _trkTime, _trkAzimuth, _trkElevation, _trkIntensity, _trkAmbient,
                _swSun, _swAmbient, _chkSpecular, _chkShadows, _chkShowSun })
                c.Enabled = on;
            _chkShadows.Enabled = on && _shadowsPossible;
            _trkShadow.Enabled = on && _shadowsPossible && _chkShadows.Checked;
        }

        // Re-read the sliders from the settings (after "time of day" or a reset).
        private void LoadFromLight(bool all)
        {
            _loading = true;
            _trkAzimuth.Value   = (int)Norm360(_light.Azimuth);
            _trkElevation.Value = Clamp((int)Math.Round(_light.Elevation), -10, 90);
            _trkAmbient.Value   = Clamp((int)Math.Round(_light.Ambient * 100f), 0, 100);
            _swSun.BackColor    = _light.Color;
            if (all)
            {
                _chkEnabled.Checked  = _light.Enabled;
                _chkSpecular.Checked = _light.Specular;
                _chkShadows.Checked  = _light.Shadows;
                _chkShowSun.Checked  = _light.ShowSun;
                _trkIntensity.Value  = Clamp((int)Math.Round(_light.Intensity * 100f), 0, 200);
                _trkShadow.Value     = Clamp((int)Math.Round(_light.ShadowStrength * 100f), 0, 100);
                _swAmbient.BackColor = _light.AmbientColor;
            }
            _loading = false;
            Labels();
        }

        private void Labels()
        {
            _lblTime.Text = (_trkTime.Value / 10f).ToString("0.0") + Loc.T(" ч");
            _lblAz.Text   = _trkAzimuth.Value + "°";
            _lblEl.Text   = _trkElevation.Value + "°";
            _lblInt.Text  = _trkIntensity.Value + " %";
            _lblAmb.Text  = _trkAmbient.Value + " %";
            _lblSh.Text   = _trkShadow.Value + " %";
        }

        // Inverse of the "time of day" mapping, from the current azimuth — the time
        // slider should open at a meaningful position, not always at noon.
        private float GuessHours()
        {
            float t = (Norm360(_light.Azimuth) - 90f) / 180f;
            float h = 6f + t * 12f;
            return h < 0f ? 0f : (h > 24f ? 24f : h);
        }

        private static float Norm360(float a)
        {
            while (a < 0f) a += 360f;
            while (a > 360f) a -= 360f;
            return a;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static void CopyInto(LightSettings from, LightSettings to)
        {
            to.Enabled = from.Enabled; to.Azimuth = from.Azimuth; to.Elevation = from.Elevation;
            to.Intensity = from.Intensity; to.Ambient = from.Ambient;
            to.ColorArgb = from.ColorArgb; to.AmbientColorArgb = from.AmbientColorArgb;
            to.Specular = from.Specular; to.Shadows = from.Shadows;
            to.ShadowStrength = from.ShadowStrength; to.ShowSun = from.ShowSun;
        }

        // ── small builders ────────────────────────────────────────
        private void Section(string text, int x, ref int y)
        {
            // Same "SECTION ─────" marker as the side panel.
            Controls.Add(new Aplat.UI.SectionHeader(text)
            {
                Location = new Point(x, y),
                Width    = ClientSize.Width - 2 * x,
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });
            y += 26;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true,
                ForeColor = Aplat.UI.Theme.Text });
        }

        private void Slider(Aplat.UI.ModernSlider t, int min, int max, int val, int x, int y)
        {
            t.Minimum = min; t.Maximum = max;
            t.Value = val < min ? min : (val > max ? max : val);
            t.TickStyle = TickStyle.None;
            t.AutoSize = false;
            t.Location = new Point(x, y - 2);
            t.Size = new Size(180, 26);
            t.BackColor = BackColor;
            Controls.Add(t);
        }

        private void Value(Label l, int y)
        {
            l.Location = new Point(336, y + 4);
            l.AutoSize = true;
            l.ForeColor = Aplat.UI.Theme.TextSoft;
            Controls.Add(l);
        }

        private void SunSwatch(Panel sw, Color col, int x, int y, bool sunColor)
        {
            sw.Location = new Point(x, y); sw.Size = new Size(180, 24);
            sw.BackColor = col; sw.BorderStyle = BorderStyle.FixedSingle; sw.Cursor = Cursors.Hand;
            Aplat.UI.ThemeManager.KeepColors(sw);   // the colour is the setting itself
            sw.Click += (s, e) =>
            {
                using (var d = new ColorDialog { Color = sw.BackColor, FullOpen = true })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    sw.BackColor = d.Color;
                    if (sunColor) _light.Color = d.Color; else _light.AmbientColor = d.Color;
                    Apply();
                }
            };
            Controls.Add(sw);
        }
    }
}
