using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    public partial class PropertiesPanel : UserControl
    {
        private SceneObject _obj;
        private bool _lock;
        private bool _hasCanvasFrame;
        private float _canvasLeft;
        private float _canvasTop;
        private float _canvasWidthPx = 1024f;
        private float _canvasHeightPx = 768f;

        private const float PixelsPerUnit = 256f;

        public event EventHandler PropertiesChanged;
        // Raised once before each inspector gesture (a drag, a typed value, a burst
        // of wheel steps) so the host can record a single undo step for it.
        public event EventHandler EditStarting;
        // The 3-D text section changed (words, font, height or depth).
        public event EventHandler<Text3DEdit> Text3DEdited;

        private object _pbrSender;
        private int _pbrTick;
        private bool _pbrMouseGesture;

        private bool _text3DTyping;   // one undo step per typing session
        private bool _showText3D;     // the text section is part of the layout

        public PropertiesPanel()
        {
            InitializeComponent();
            BuildComponents();
            BuildSurfaceControls();
            foreach (var slider in new Aplat.UI.ModernSlider[] { trkMetal, trkRough, trkEmis, trkOpacity })
            {
                slider.MouseDown += (s, e) => { _pbrSender = null; _pbrMouseGesture = true; };
                slider.MouseUp += (s, e) => { _pbrMouseGesture = false; _pbrSender = null; };
            }
            ConfigureDisplayUnits();
            // These two show the material's own colours, not a theme role.
            Aplat.UI.ThemeManager.KeepColors(btnColor);
            Aplat.UI.ThemeManager.KeepColors(btnEmisColor);
        }

        public void SetObject(SceneObject obj)
        {
            _obj = obj;
            _pbrSender = null;
            Reload();
        }

        public void SetCanvasFrame(float leftWorld, float topWorld, float widthWorld, float heightWorld)
        {
            _hasCanvasFrame = true;
            _canvasLeft = leftWorld;
            _canvasTop = topWorld;
            _canvasWidthPx = Math.Max(1f, WorldToPx(widthWorld));
            _canvasHeightPx = Math.Max(1f, WorldToPx(heightWorld));
            ConfigurePositionRanges();
            Reload();
        }

        private void Reload()
        {
            _lock = true;
            try { ReloadCore(); }
            finally { _lock = false; }   // a failed load must never leave the panel deaf to edits
        }

        private void ReloadCore()
        {
            bool has = _obj != null;
            panelProps.Visible = has;
            lblNoSel.Visible   = !has;
            if (!has) return;

            txtName.Text = _obj.Name;
            LoadText3D();
            numPX.Value = ClampDecimal((decimal)WorldToDisplayX(_obj.Position.X), numPX.Minimum, numPX.Maximum);
            numPY.Value = ClampDecimal((decimal)WorldToDisplayY(_obj.Position.Y), numPY.Minimum, numPY.Maximum);
            numPZ.Value = (decimal)WorldToPx(Clamp(_obj.Position.Z));
            numRX.Value = (decimal)ClampAngle(_obj.Rotation.X);
            numRY.Value = (decimal)ClampAngle(_obj.Rotation.Y);
            numRZ.Value = (decimal)ClampAngle(_obj.Rotation.Z);
            numSX.Value = (decimal)ScaleToPercent(ClampScale(_obj.Scale.X));
            numSY.Value = (decimal)ScaleToPercent(ClampScale(_obj.Scale.Y));
            numSZ.Value = (decimal)ScaleToPercent(ClampScale(_obj.Scale.Z));

            var mat = ReprMaterial(_obj);
            btnColor.BackColor    = mat.Color;
            LoadPbr(mat);
            UpdateTextureInfo();
            SetEditable(!_obj.IsLocked);
            SetPaperLook(_obj.PrimitiveType == "Canvas");
        }

        // The sheet is paper: drawn unlit, so metal, gloss, glow and presets cannot show on it.
        // They are disabled (and the header says why) instead of silently doing nothing.
        private void SetPaperLook(bool paper)
        {
            lblPbr.Text = paper ? Loc.T("Материал (PBR) — не действует на холсте") : Loc.T("Материал (PBR)");
            if (!paper) return;
            foreach (Control c in new Control[] { cmbPreset, trkMetal, trkRough, trkEmis, btnEmisColor }) c.Enabled = false;
        }

        private static float Clamp(float v)      { return Math.Max(-100f, Math.Min(100f, v)); }
        private static float ClampAngle(float v) { return Math.Max(-360f, Math.Min(360f, v)); }
        private static float ClampScale(float v) { return Math.Max(0.01f, Math.Min(10f, v)); }
        private static float WorldToPx(float v)  { return (float)Math.Round(v * PixelsPerUnit); }
        private static float PxToWorld(decimal v) { return (float)v / PixelsPerUnit; }
        private float WorldToDisplayX(float worldX) { return _hasCanvasFrame ? WorldToPx(worldX - _canvasLeft) : WorldToPx(Clamp(worldX)); }
        private float WorldToDisplayY(float worldY) { return _hasCanvasFrame ? WorldToPx(_canvasTop - worldY) : WorldToPx(Clamp(worldY)); }
        private float DisplayXToWorld(decimal px) { return _hasCanvasFrame ? _canvasLeft + PxToWorld(px) : PxToWorld(px); }
        private float DisplayYToWorld(decimal px) { return _hasCanvasFrame ? _canvasTop - PxToWorld(px) : PxToWorld(px); }
        private static float ScaleToPercent(float v) { return (float)Math.Round(v * 100f); }
        private static float PercentToScale(decimal v) { return (float)v / 100f; }
        private static decimal ClampDecimal(decimal v, decimal min, decimal max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        // Only the field the user touched is written. The fields show rounded and clamped values
        // (whole pixels / percents, a position kept on the canvas), so pushing all nine would
        // quietly rewrite the object's real transform on any edit — even on leaving the name box.
        private void Push(object sender)
        {
            if (_lock || _obj == null) return;
            if (_obj.IsLocked) return;
            if (sender == txtName)
            {
                _obj.Name = txtName.Text;
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            Vec3 newPos = _obj.Position, newRot = _obj.Rotation, newScl = _obj.Scale;
            if (sender == numPX)      newPos = new Vec3(DisplayXToWorld(numPX.Value), newPos.Y, newPos.Z);
            else if (sender == numPY) newPos = new Vec3(newPos.X, DisplayYToWorld(numPY.Value), newPos.Z);
            else if (sender == numPZ) newPos = new Vec3(newPos.X, newPos.Y, PxToWorld(numPZ.Value));
            else if (sender == numRX) newRot = new Vec3((float)numRX.Value, newRot.Y, newRot.Z);
            else if (sender == numRY) newRot = new Vec3(newRot.X, (float)numRY.Value, newRot.Z);
            else if (sender == numRZ) newRot = new Vec3(newRot.X, newRot.Y, (float)numRZ.Value);
            else if (sender == numSX) newScl = new Vec3(PercentToScale(numSX.Value), newScl.Y, newScl.Z);
            else if (sender == numSY) newScl = new Vec3(newScl.X, PercentToScale(numSY.Value), newScl.Z);
            else if (sender == numSZ) newScl = new Vec3(newScl.X, newScl.Y, PercentToScale(numSZ.Value));
            else return;

            if (_obj.IsGroup) ApplyGroupDelta(newPos, newRot, newScl);
            else
            {
                _obj.Position = newPos;
                _obj.Rotation = newRot;
                _obj.Scale    = newScl;
            }
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        // For groups the numeric fields are absolute targets; distribute the
        // change to the leaf objects around the group's current centre.
        private void ApplyGroupDelta(Vec3 newPos, Vec3 newRot, Vec3 newScl)
        {
            Vec3 dPos = newPos - _obj.Position;
            if (dPos.Length > 1e-6f) SceneGeometry.Translate(_obj, dPos);

            Vec3 pivot = SceneGeometry.Center(_obj);

            for (int a = 0; a < 3; a++)
            {
                float d = newRot.Get(a) - _obj.Rotation.Get(a);
                if (Math.Abs(d) > 1e-4f) SceneGeometry.RotateAround(_obj, pivot, a, d);
            }

            for (int a = 0; a < 3; a++)
            {
                float cur = _obj.Scale.Get(a);
                float tgt = newScl.Get(a);
                if (cur > 1e-4f && Math.Abs(tgt - cur) > 1e-4f)
                    SceneGeometry.ScaleAround(_obj, pivot, a, tgt / cur);
            }
        }

        // Material edits apply to the object, or to every leaf when it is a group
        private void ApplyMaterial(Action<Material> apply)
        {
            if (_obj == null) return;
            if (_obj.IsLocked) return;
            if (_obj.IsGroup)
                foreach (var leaf in SceneGeometry.Leaves(_obj)) apply(leaf.Material ?? (leaf.Material = new Material()));
            else
                apply(_obj.Material ?? (_obj.Material = new Material()));
        }

        private static Material ReprMaterial(SceneObject o)
        {
            if (!o.IsGroup) return o.Material ?? (o.Material = new Material());
            foreach (var leaf in SceneGeometry.Leaves(o)) return leaf.Material ?? (leaf.Material = new Material());
            return o.Material ?? (o.Material = new Material());
        }

        private void btnColor_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            using (var d = new ColorDialog { Color = ReprMaterial(_obj).Color })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                ApplyMaterial(m => m.Color = d.Color);
                btnColor.BackColor = d.Color;
                SyncPresetCombo(ReprMaterial(_obj));
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void btnTexture_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            if (IsTextObject(_obj))
            {
                MessageBox.Show(Loc.T("Текстуру нельзя применить к объёмному тексту."),
                    Loc.T("Текстура"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var d = new OpenFileDialog
            {
                Filter = Loc.T("Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*")
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;

                // Verify the file actually decodes before committing it — otherwise the
                // path silently sets but the canvas never shows the image ("nothing
                // changes"). Surface the real reason instead of swallowing it.
                try
                {
                    using (var probe = Material.LoadBitmap(d.FileName)) { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Не удалось загрузить изображение:\n") + ex.Message,
                        Loc.T("Текстура"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                ApplyMaterial(m => m.TexturePath = d.FileName);
                UpdateTextureInfo();
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Text objects are made of many thin tessellated faces; texturing them is
        // both meaningless and unstable, so the feature is disabled for them.
        private static bool IsTextObject(SceneObject o)
        {
            if (o == null) return false;
            if (!o.IsGroup) return o.PrimitiveType == "Text";
            foreach (var leaf in SceneGeometry.Leaves(o))
                if (leaf.PrimitiveType == "Text") return true;
            return false;
        }

        private void btnNoTexture_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            ApplyMaterial(m => { m.TexturePath = ""; m.TextureBase64 = null; });
            UpdateTextureInfo();
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateTextureInfo()
        {
            var mat = _obj != null ? ReprMaterial(_obj) : null;
            bool has    = mat != null && mat.HasTexture;
            bool isText = IsTextObject(_obj);

            // Text objects can't be textured at all, so the whole texture block
            // (buttons + "do not tile" checkbox) is removed for them.
            btnTexture.Visible   = !isText;
            btnNoTexture.Visible = !isText;
            lblTexInfo.Visible   = !isText;
            chkNoTile.Visible    = !isText;

            if (isText) return;

            lblTexInfo.Text      = has ? Loc.T("Текстура: ") + Path.GetFileName(mat.TexturePath) : Loc.T("Текстура: нет");
            btnTexture.Enabled   = true;
            btnNoTexture.Enabled = has;
            chkNoTile.Checked    = mat != null && mat.NoTile;
            chkNoTile.Enabled    = has;
        }

        // ── PBR ──────────────────────────────────────────────────
        // The sliders edit metalness / roughness / glow / transparency of the selected
        // object (of every leaf, for a group). The renderer turns these values into
        // real light parameters, so the effect is visible at once.
        private void LoadPbr(Material mat)
        {
            trkMetal.Value   = Pct(mat.Metallic);
            trkRough.Value   = Pct(mat.Roughness);
            trkEmis.Value    = Pct(mat.EmissionStrength);
            trkOpacity.Value = Math.Max(trkOpacity.Minimum, Pct(mat.Opacity));
            btnEmisColor.BackColor = mat.EmissionStrength > 0.001f
                ? mat.EmissionColor : Aplat.UI.Theme.BtnFace;
            SyncPresetCombo(mat);
            UpdatePbrLabels();
            UpdateAoInfo();
            LoadSurface(mat);
        }

        private static int Pct(float v)
        {
            int p = (int)Math.Round(v * 100f);
            return p < 0 ? 0 : (p > 100 ? 100 : p);
        }

        private void UpdatePbrLabels()
        {
            valMetal.Text   = trkMetal.Value + "%";
            valRough.Text   = trkRough.Value + "%";
            valEmis.Text    = trkEmis.Value + "%";
            valOpacity.Text = trkOpacity.Value + "%";
        }

        private void OnPbrTrack(object s, EventArgs e)
        {
            if (_lock || _obj == null) return;
            if (_obj.IsLocked) return;
            int now = Environment.TickCount;
            if (_pbrSender != s || (!_pbrMouseGesture && unchecked(now - _pbrTick) > 700))
                EditStarting?.Invoke(this, EventArgs.Empty);
            _pbrSender = s;
            _pbrTick = now;
            float metal   = trkMetal.Value / 100f;
            float rough   = trkRough.Value / 100f;
            float emis    = trkEmis.Value / 100f;
            float opacity = trkOpacity.Value / 100f;
            // Only the slider that moved is written: in a group the sliders show the first leaf's
            // values, and writing all four would flatten the other leaves' own materials.
            bool all = s != trkMetal && s != trkRough && s != trkEmis && s != trkOpacity;
            ApplyMaterial(m =>
            {
                if (all || s == trkMetal)   m.Metallic  = metal;
                if (all || s == trkRough)   m.Roughness = rough;
                if (all || s == trkOpacity) m.Opacity   = opacity;
                if (all || s == trkEmis)
                {
                    m.EmissionStrength = emis;
                    // A glow without an explicit colour takes the material's own colour; otherwise
                    // the "Glow" slider would look broken (black light).
                    if (emis > 0.001f && m.EmissionArgb == System.Drawing.Color.Black.ToArgb())
                        m.EmissionColor = m.Color;
                }
            });
            UpdatePbrLabels();
            var mat = ReprMaterial(_obj);
            btnEmisColor.BackColor = mat.EmissionStrength > 0.001f
                ? mat.EmissionColor : Aplat.UI.Theme.BtnFace;
            SyncPresetCombo(mat);
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        // Shows which preset the material amounts to ("custom" when none): what was chosen stays
        // visible, and touching a slider or the colour turns it back into "custom".
        private void SyncPresetCombo(Material mat)
        {
            bool was = _lock;
            _lock = true;
            try
            {
                string name = mat != null ? mat.MatchPreset() : null;
                int i = name == null ? -1 : Array.IndexOf(Aplat.Models.Material.PresetNames, name);
                cmbPreset.SelectedIndex = i + 1;
                cmbPreset.Invalidate();
            }
            finally { _lock = was; }
        }

        // Each row carries a small ball in the material's look (the object's own colour, the preset's
        // metal / gloss / glow / glass), so the list previews what it will do.
        private void cmbPreset_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 1 || e.Index > Aplat.Models.Material.PresetNames.Length) return;
            bool field = (e.State & DrawItemState.ComboBoxEdit) != 0;
            var t = new Material { Color = _obj != null ? ReprMaterial(_obj).Color : System.Drawing.Color.FromArgb(120, 150, 200) };
            t.ApplyPreset(Aplat.Models.Material.PresetNames[e.Index - 1]);
            int d = Math.Min(e.Bounds.Height - 6, 16);
            var ball = new Rectangle(e.Bounds.Right - d - (field ? 22 : 8), e.Bounds.Y + (e.Bounds.Height - d) / 2, d, d);
            DrawMaterialBall(e.Graphics, ball, t);
        }

        internal static void DrawMaterialBall(Graphics g, Rectangle r, Material m)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color c = m.Color;
            float metal = m.Metallic, rough = m.Roughness;
            if (m.EmissionStrength > 0.001f)
            {
                // A glow: a soft halo in the glow colour round the ball.
                var halo = Rectangle.Inflate(r, 3, 3);
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(halo);
                    using (var pb = new System.Drawing.Drawing2D.PathGradientBrush(path))
                    {
                        pb.CenterColor = Color.FromArgb((int)(150 * m.EmissionStrength), m.EmissionColor);
                        pb.SurroundColors = new[] { Color.FromArgb(0, m.EmissionColor) };
                        g.FillEllipse(pb, halo);
                    }
                }
            }
            // Body: a metal is dark at the rim and bright at its reflection; a rough dielectric is even.
            Color lit  = Blend(c, Color.White, 0.25f * (1f - metal));
            Color dark = Blend(c, Color.Black, 0.35f + 0.25f * metal);
            if (m.EmissionStrength > 0.001f) lit = Blend(lit, m.EmissionColor, m.EmissionStrength);
            int alpha = (int)(255 * Math.Max(0.35f, m.Opacity));
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(r);
                using (var pb = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    pb.CenterColor = Color.FromArgb(alpha, lit);
                    pb.SurroundColors = new[] { Color.FromArgb(alpha, dark) };
                    pb.CenterPoint = new PointF(r.X + r.Width * 0.35f, r.Y + r.Height * 0.32f);
                    g.FillEllipse(pb, r);
                }
            }
            // Highlight: small and sharp when glossy, wide and faint when rough.
            float gloss = 1f - rough;
            float hs = r.Width * (0.18f + 0.30f * rough);
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var hr = new RectangleF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.16f, hs, hs);
                path.AddEllipse(hr);
                using (var pb = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    pb.CenterColor = Color.FromArgb((int)(235 * (0.25f + 0.75f * gloss * gloss)), Color.White);
                    pb.SurroundColors = new[] { Color.FromArgb(0, Color.White) };
                    g.FillEllipse(pb, hr);
                }
            }
            using (var pen = new Pen(Color.FromArgb(120, Aplat.UI.Theme.Border)))
                g.DrawEllipse(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            g.SmoothingMode = old;
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        private void cmbPreset_Changed(object s, EventArgs e)
        {
            if (_lock || _obj == null || cmbPreset.SelectedIndex <= 0) return;
            string name = Aplat.Models.Material.PresetNames[cmbPreset.SelectedIndex - 1];   // the stable (Russian) id, not the shown text
            EditStarting?.Invoke(this, EventArgs.Empty);
            ApplyMaterial(m => m.ApplyPreset(name));

            _lock = true;
            var mat = ReprMaterial(_obj);
            btnColor.BackColor = mat.Color;
            LoadPbr(mat);
            _lock = false;
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void btnEmisColor_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            var mat = ReprMaterial(_obj);
            using (var d = new ColorDialog { Color = mat.EmissionColor, FullOpen = true })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                ApplyMaterial(m =>
                {
                    m.EmissionColor = d.Color;
                    // A glow colour was picked while the strength is zero — switch it on,
                    // otherwise the click would have no visible result.
                    if (m.EmissionStrength < 0.001f) m.EmissionStrength = 0.5f;
                });
                _lock = true;
                LoadPbr(ReprMaterial(_obj));
                _lock = false;
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void btnAo_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            using (var d = new OpenFileDialog
            {
                Title  = Loc.T("Карта затенения (ambient occlusion)"),
                Filter = Loc.T("Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*")
            })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var probe = Material.LoadBitmap(d.FileName)) { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.T("Не удалось загрузить изображение:\n") + ex.Message,
                        Loc.T("Карта AO"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                ApplyMaterial(m => m.AoTexturePath = d.FileName);
                UpdateAoInfo();
                PropertiesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void btnNoAo_Click(object s, EventArgs e)
        {
            if (_obj == null) return;
            ApplyMaterial(m => m.AoTexturePath = "");
            UpdateAoInfo();
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateAoInfo()
        {
            var mat = _obj != null ? ReprMaterial(_obj) : null;
            bool has = mat != null && mat.HasAoTexture;
            lblAoInfo.Text  = has ? "AO: " + Path.GetFileName(mat.AoTexturePath) : Loc.T("AO: нет");
            btnNoAo.Enabled = has && _obj != null && !_obj.IsLocked;
        }

        private void chkNoTile_Changed(object s, EventArgs e)
        {
            if (_lock || _obj == null) return;
            ApplyMaterial(m => m.NoTile = chkNoTile.Checked);
            PropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnNum(object s, EventArgs e) { Push(s); }

        // ── 3-D text ──────────────────────────────────────────────
        private static bool IsText3D(SceneObject o) { return o != null && !o.IsGroup && o.PrimitiveType == "Text"; }

        private void LoadText3D()
        {
            bool show = Features.Text3D && IsText3D(_obj);
            bool changed = _showText3D != show;
            _showText3D = show;
            foreach (Control c in new Control[] { lblText3D, txtText3D, cmbText3DFont, numTextSize, numTextDepth })
                c.Visible = show;
            if (changed) ApplyObjectLayoutMetrics();
            if (!show) return;

            _text3DTyping = false;
            txtText3D.Text = !string.IsNullOrEmpty(_obj.TextContent) ? _obj.TextContent
                           : Loc.TextFromName(_obj.Name);
            cmbText3DFont.SelectedIndex = Aplat.Dialogs.TextInputDialog.FallbackFontIndex(
                cmbText3DFont.Items, _obj.TextFontName);
            float size = _obj.TextWorldSize > 0.01f ? _obj.TextWorldSize : 1.5f;
            numTextSize.Value  = (decimal)Math.Round(size * PixelsPerUnit);
            float depth = _obj.TextDepth > 0f ? _obj.TextDepth : Aplat.Primitives.TextMeshBuilder.DefaultDepthRatio;
            numTextDepth.Value = (decimal)Math.Round(depth * 100f);
        }

        private void txtText3D_Changed(object s, EventArgs e)
        {
            if (_lock || !IsText3D(_obj) || _obj.IsLocked) return;
            if (!_text3DTyping)
            {
                _text3DTyping = true;
                EditStarting?.Invoke(this, EventArgs.Empty);
            }
            RaiseText3D();
        }

        private void cmbText3DFont_Changed(object s, EventArgs e)
        {
            if (_lock || !IsText3D(_obj) || _obj.IsLocked) return;
            EditStarting?.Invoke(this, EventArgs.Empty);
            RaiseText3D();
        }

        private void OnText3DParam(object s, EventArgs e)
        {
            if (_lock || !IsText3D(_obj) || _obj.IsLocked) return;
            RaiseText3D();
        }

        private void RaiseText3D()
        {
            if (string.IsNullOrWhiteSpace(txtText3D.Text)) return;   // keep the last good text
            _lock = true;
            txtName.Text = Loc.TextPrefix + txtText3D.Text;               // the name follows the words
            _lock = false;
            Text3DEdited?.Invoke(this, new Text3DEdit
            {
                Text   = txtText3D.Text,
                Font   = cmbText3DFont.SelectedItem as string ?? "Segoe UI",
                Height = (float)numTextSize.Value / PixelsPerUnit,
                Depth  = (float)numTextDepth.Value / 100f,
            });
        }

        private void OnFieldEditStarting(object s, EventArgs e)
        {
            if (!_lock && _obj != null && !_obj.IsLocked) EditStarting?.Invoke(this, EventArgs.Empty);
        }
        private void txtName_Changed(object s, EventArgs e) { Push(txtName); }
        private void txtName_Leave(object s, EventArgs e)   { }

        private void ConfigureDisplayUnits()
        {
            // Drag: 1 unit per pixel; Ctrl snaps to 8 px / 15° / 10 %.
            ConfigurePositionRanges();
            ConfigureNumber(numPZ, -25600m, 25600m, 0, 8m, 8m);

            ConfigureNumber(numRX, -360m, 360m, 0, 1m, 15m);
            ConfigureNumber(numRY, -360m, 360m, 0, 1m, 15m);
            ConfigureNumber(numRZ, -360m, 360m, 0, 1m, 15m);

            ConfigureNumber(numSX, 1m, 1000m, 0, 1m, 10m);
            ConfigureNumber(numSY, 1m, 1000m, 0, 1m, 10m);
            ConfigureNumber(numSZ, 1m, 1000m, 0, 1m, 10m);

            ConfigureNumber(numTextSize, 16m, 4096m, 0, 8m, 32m);
            ConfigureNumber(numTextDepth, 2m, 300m, 0, 1m, 10m);
        }

        private void ConfigurePositionRanges()
        {
            decimal maxX = _hasCanvasFrame ? (decimal)Math.Ceiling(_canvasWidthPx) : 25600m;
            decimal maxY = _hasCanvasFrame ? (decimal)Math.Ceiling(_canvasHeightPx) : 25600m;
            ConfigureNumber(numPX, 0m, maxX, 0, 8m, 8m);
            ConfigureNumber(numPY, 0m, maxY, 0, 8m, 8m);
        }

        private void SetEditable(bool enabled)
        {
            SetSurfaceEditable(enabled);
            // Text boxes become read-only rather than disabled: Windows paints a
            // disabled box as a light-grey slab with GrayText, unreadable on Graphite.
            SetTextEditable(txtName, enabled);
            foreach (Control c in new Control[] {
                numPX, numPY, numPZ, numRX, numRY, numRZ, numSX, numSY, numSZ,
                txtText3D, cmbText3DFont, numTextSize, numTextDepth,
                btnColor,
                cmbPreset, trkMetal, trkRough, trkEmis, trkOpacity,
                btnEmisColor, btnAo, btnNoAo })
            {
                var tb = c as TextBox;
                if (tb != null) { SetTextEditable(tb, enabled); continue; }
                c.Enabled = enabled;
            }
            if (!enabled)
            {
                btnTexture.Enabled = false;
                btnNoTexture.Enabled = false;
                chkNoTile.Enabled = false;
                btnNoAo.Enabled = false;
            }
        }

        private static void SetTextEditable(TextBox tb, bool editable)
        {
            tb.ReadOnly  = !editable;
            tb.TabStop   = editable;
            tb.ForeColor = editable ? Aplat.UI.Theme.Text : Aplat.UI.Theme.TextDisabled;
        }

        private static void ConfigureNumber(Aplat.UI.DragNumber n, decimal min, decimal max,
                                            int decimals, decimal increment, decimal snap)
        {
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Increment = increment;
            n.DragStep = 1m;
            n.SnapStep = snap;
        }
    }

    // What the 3-D text section asks for; the host rebuilds the mesh.
    public sealed class Text3DEdit : EventArgs
    {
        public string Text;
        public string Font;
        public float  Height;   // world units
        public float  Depth;    // extrusion / height
    }
}
