using System;
using System.Drawing;
using System.Windows.Forms;
using Aplat.Models;
using Aplat.UI;

namespace Aplat.Controls
{
    public partial class PropertiesPanel
    {
        private Label _surfaceTitle, _lightTitle;
        private ComboBox _alphaMode;
        private CheckBox _pixelTexture, _transmitLight, _pointLight, _flicker;
        private DragNumber _cutoff, _power, _range;
        private Button _lightColor;
        private Color _lightSwatch = Color.White;

        private void BuildSurfaceControls()
        {
            _surfaceTitle = SurfaceLabel(Loc.T("ПРОЗРАЧНОСТЬ ТЕКСТУРЫ"));
            _lightTitle = SurfaceLabel(Loc.T("ЛОКАЛЬНЫЙ СВЕТ"));
            _alphaMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Input, ForeColor = Theme.Text,
                Font = new Font(FontName, 8.75f) };
            _alphaMode.Items.AddRange(new object[] { Loc.T("Альфа PNG · полупрозрачность"), Loc.T("Непрозрачная текстура"), Loc.T("Вырезы PNG · по порогу") });
            _alphaMode.SelectedIndexChanged += SurfaceChanged;
            ThemeManager.ThemeCombo(_alphaMode);
            _cutoff = SurfaceNumber(Loc.T("Порог выреза"), "%", 0m, 100m, 0);
            _pixelTexture = SurfaceCheck(Loc.T("Чёткие пиксели текстуры"));
            _transmitLight = SurfaceCheck(Loc.T("Пропускать свет через прозрачность"));
            _pointLight = SurfaceCheck(Loc.T("Освещать соседние объекты"));
            _power = SurfaceNumber(Loc.T("Мощность"), "", 0m, 8m, 2);
            _range = SurfaceNumber(Loc.T("Радиус"), Loc.T(" ед."), 0.1m, 30m, 1);
            _flicker = SurfaceCheck(Loc.T("Колебания яркости"));
            _lightColor = new RoundedButton { Text = Loc.T("Цвет источника света"), Font = new Font(FontName, 8.75f) };
            _lightColor.Click += ChooseLightColor;
            ThemeManager.KeepColors(_lightColor);
            panelProps.Content.Controls.AddRange(new Control[] { _surfaceTitle, _alphaMode,
                _cutoff, _pixelTexture, _transmitLight, _lightTitle, _pointLight, _lightColor, _power, _range, _flicker });
            ApplyObjectLayoutMetrics();
        }

        private Label SurfaceLabel(string text)
        {
            return new SectionHeader { Text = text, ForeColor = Theme.TextMuted,
                Font = new Font(FontName, 8f, FontStyle.Bold) };
        }
        private CheckBox SurfaceCheck(string text)
        {
            var c = new ModernCheckBox { Text = text, ForeColor = Theme.Text, Font = new Font(FontName, 8.75f) };
            c.CheckedChanged += SurfaceChanged;
            return c;
        }
        private DragNumber SurfaceNumber(string caption, string suffix, decimal min, decimal max, int decimals)
        {
            var n = new DragNumber { Caption = caption, Suffix = suffix, Font = new Font(FontName, 8.75f) };
            ConfigureNumber(n, min, max, decimals, decimals == 0 ? 1m : 0.1m, 1m);
            n.DragStep = decimals == 0 ? 1m : 0.01m;
            n.EditStarting += OnFieldEditStarting;
            n.ValueChanged += SurfaceChanged;
            return n;
        }
        private int LayoutSurfaceControls(int x, int y, int width)
        {
            if (_surfaceTitle == null) return y + 2;
            _surfaceTitle.SetBounds(x, y, width, 18); y += 26;
            _alphaMode.SetBounds(x, y, width, 26); y += 32;
            _cutoff.SetBounds(x, y, width, 28); y += 34;
            _pixelTexture.SetBounds(x, y, width, 26); y += 28;
            _transmitLight.SetBounds(x, y, width, 38); y += 48;
            _lightTitle.SetBounds(x, y, width, 18); y += 26;
            _pointLight.SetBounds(x, y, width, 28); y += 34;
            _lightColor.SetBounds(x, y, width, 32); y += 38;
            _power.SetBounds(x, y, width, 28); y += 34;
            _range.SetBounds(x, y, width, 28); y += 34;
            _flicker.SetBounds(x, y, width, 26); y += 28;
            y += 14;
            return y;
        }
        private void LoadSurface(Material m)
        {
            if (_alphaMode == null) return;
            _alphaMode.SelectedIndex = Math.Max(0, Math.Min(2, (int)m.AlphaMode));
            _cutoff.Value = Pct(m.AlphaCutoff);
            _pixelTexture.Checked = m.PixelTexture;
            _transmitLight.Checked = m.TransmitLight;
            m = RepresentativeLight(m);
            _pointLight.Checked = m.LightEnabled;
            _power.Value = ClampDecimal((decimal)m.LightPower, 0m, 8m);
            _range.Value = ClampDecimal((decimal)m.LightRange, 0.1m, 30m);
            _flicker.Checked = m.LightFlicker;
            _lightSwatch = m.LightColor;
            UpdateLightButtonLook();
        }

        // The light-colour button is a swatch while the light is on; while it is off (or the object
        // is locked) it takes the ordinary disabled look — a bright swatch in a dark panel looks live.
        private void UpdateLightButtonLook()
        {
            if (_lightColor == null) return;
            if (_lightColor.Enabled)
            {
                _lightColor.BackColor = _lightSwatch;
                _lightColor.ForeColor = Theme.ContrastText(_lightSwatch);
            }
            else
            {
                _lightColor.BackColor = Theme.BtnFace;
                _lightColor.ForeColor = Theme.TextDisabled;
            }
        }
        private void SurfaceChanged(object sender, EventArgs e)
        {
            if (_lock || _obj == null || _obj.IsLocked) return;
            if (!(sender is DragNumber)) EditStarting?.Invoke(this, EventArgs.Empty);
            if (sender == _alphaMode) ApplyMaterial(m => m.AlphaMode = (TextureAlphaMode)Math.Max(0, _alphaMode.SelectedIndex));
            else if (sender == _cutoff) ApplyMaterial(m => m.AlphaCutoff = (float)_cutoff.Value / 100f);
            else if (sender == _pixelTexture) ApplyMaterial(m => m.PixelTexture = _pixelTexture.Checked);
            else if (sender == _transmitLight) ApplyMaterial(m => m.TransmitLight = _transmitLight.Checked);
            else
            {
                var targets = new System.Collections.Generic.List<Material>();
                foreach (var leaf in Aplat.Rendering.SceneGeometry.Leaves(_obj))
                    if (leaf.Material.LightEnabled) targets.Add(leaf.Material);
                if (targets.Count == 0)
                    foreach (var leaf in Aplat.Rendering.SceneGeometry.Leaves(_obj)) targets.Add(leaf.Material);
                foreach (var m in targets)
                {
                    if (sender == _pointLight)
                    {
                        if (!m.LocalLightArgb.HasValue) m.LightColor = m.LightColor;
                        m.LightEnabled = _pointLight.Checked;
                    }
                    else if (sender == _power) m.LightPower = (float)_power.Value;
                    else if (sender == _range) m.LightRange = (float)_range.Value;
                    else if (sender == _flicker) m.LightFlicker = _flicker.Checked;
                }
            }
            SetSurfaceEditable(true);
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }
        private void ChooseLightColor(object sender, EventArgs e)
        {
            if (_obj == null || _obj.IsLocked) return;
            using (var dialog = new ColorDialog { Color = RepresentativeLight(ReprMaterial(_obj)).LightColor, FullOpen = true })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                EditStarting?.Invoke(this, EventArgs.Empty);
                bool hasSources = false;
                foreach (var leaf in Aplat.Rendering.SceneGeometry.Leaves(_obj))
                    hasSources |= leaf.Material.LightEnabled;
                foreach (var leaf in Aplat.Rendering.SceneGeometry.Leaves(_obj))
                    if (!hasSources || leaf.Material.LightEnabled) leaf.Material.LightColor = dialog.Color;
                _lightSwatch = dialog.Color;
                UpdateLightButtonLook();
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        private Material RepresentativeLight(Material fallback)
        {
            if (_obj != null)
                foreach (var leaf in Aplat.Rendering.SceneGeometry.Leaves(_obj))
                    if (leaf.Material.LightEnabled) return leaf.Material;
            return fallback;
        }
        private void SetSurfaceEditable(bool enabled)
        {
            if (_alphaMode == null) return;
            foreach (Control c in new Control[] { _alphaMode, _cutoff, _pixelTexture, _transmitLight,
                _pointLight, _lightColor, _power, _range, _flicker }) c.Enabled = enabled;
            _cutoff.Enabled = enabled && _alphaMode.SelectedIndex == 2;
            _power.Enabled = _range.Enabled = _flicker.Enabled = _lightColor.Enabled = enabled && _pointLight.Checked;
            UpdateLightButtonLook();
        }
    }
}
