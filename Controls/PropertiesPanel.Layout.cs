namespace Aplat.Controls
{
    partial class PropertiesPanel
    {
        private System.ComponentModel.IContainer components = null;

        private static System.Drawing.Color ClCard { get { return Aplat.UI.Theme.Card; } }
        private static System.Drawing.Color ClBorder { get { return Aplat.UI.Theme.Border; } }
        private static System.Drawing.Color ClText { get { return Aplat.UI.Theme.Text; } }
        private static System.Drawing.Color ClTextSoft { get { return Aplat.UI.Theme.TextSoft; } }
        private static System.Drawing.Color ClAccent { get { return Aplat.UI.Theme.Accent; } }
        private static System.Drawing.Color ClBtnFace { get { return Aplat.UI.Theme.BtnFace; } }
        private static readonly string FontName = Aplat.UI.Theme.FontFamily;

        private System.Windows.Forms.Label lblNoSel;
        private Aplat.UI.ScrollHost panelProps;   // thin themed scrollbar, never horizontal
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox txtName;
        // 3-D text (shown only for a "Text" object): words, font, height, depth.
        private System.Windows.Forms.Label lblText3D;
        private System.Windows.Forms.TextBox txtText3D;
        private System.Windows.Forms.ComboBox cmbText3DFont;
        private Aplat.UI.DragNumber numTextSize, numTextDepth;
        private System.Windows.Forms.Label lblPos;
        private Aplat.UI.DragNumber numPX, numPY, numPZ;
        private System.Windows.Forms.Label lblRot;
        private Aplat.UI.DragNumber numRX, numRY, numRZ;
        private System.Windows.Forms.Label lblScl;
        private Aplat.UI.DragNumber numSX, numSY, numSZ;
        private System.Windows.Forms.Label lblMat;
        private System.Windows.Forms.Button btnColor;
        private System.Windows.Forms.Button btnTexture, btnNoTexture;
        private System.Windows.Forms.Label lblTexInfo;
        private System.Windows.Forms.CheckBox chkNoTile;

        // ── PBR ──────────────────────────────────────────────────
        private System.Windows.Forms.Label lblPbr;
        private System.Windows.Forms.ComboBox cmbPreset;
        private System.Windows.Forms.Label lblMetal, lblRough, lblEmis, lblOpacity;
        private Aplat.UI.ModernSlider trkMetal, trkRough, trkEmis, trkOpacity;
        private System.Windows.Forms.Label valMetal, valRough, valEmis, valOpacity;
        private System.Windows.Forms.Button btnEmisColor, btnAo, btnNoAo;
        private System.Windows.Forms.Label lblAoInfo;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        // The WinForms designer only parses InitializeComponent. The hand-built layout
        // lives in BuildComponents() (called right after), so the designer no longer
        // chokes on the helper calls / TrackBars and the error list stays clean.
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.BackColor = ClCard;
            this.Name = "PropertiesPanel";
            this.Size = new System.Drawing.Size(224, 516);
        }

        private void BuildComponents()
        {
            this.lblNoSel = new System.Windows.Forms.Label();
            this.panelProps = new Aplat.UI.ScrollHost();
            this.lblName = new System.Windows.Forms.Label();
            this.txtName = new System.Windows.Forms.TextBox();
            this.lblText3D = new Aplat.UI.SectionHeader { Text = Loc.T("Текст"), AutoSize = false };
            this.txtText3D = new System.Windows.Forms.TextBox();
            this.cmbText3DFont = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                FlatStyle     = System.Windows.Forms.FlatStyle.Flat,
                MaxDropDownItems = 14,
            };
            foreach (var ff in System.Drawing.FontFamily.Families) this.cmbText3DFont.Items.Add(ff.Name);
            this.numTextSize  = TextParam(Loc.T("Высота"),  "px");
            this.numTextDepth = TextParam(Loc.T("Глубина"), "%");
            this.txtText3D.TextChanged += new System.EventHandler(this.txtText3D_Changed);
            this.txtText3D.Leave       += (s2, e2) => _text3DTyping = false;
            this.cmbText3DFont.SelectedIndexChanged += new System.EventHandler(this.cmbText3DFont_Changed);
            foreach (System.Windows.Forms.Control c in new System.Windows.Forms.Control[] {
                lblText3D, txtText3D, cmbText3DFont, numTextSize, numTextDepth })
                c.Visible = false;   // shown for 3-D text objects only (LoadText3D)
            this.lblPos = new Aplat.UI.SectionHeader();
            this.numPX = Field("X", AxisX, "px");
            this.numPY = Field("Y", AxisY, "px");
            this.numPZ = Field("Z", AxisZ, "px");
            this.lblRot = new Aplat.UI.SectionHeader();
            this.numRX = Field("X", AxisX, "°");
            this.numRY = Field("Y", AxisY, "°");
            this.numRZ = Field("Z", AxisZ, "°");
            this.lblScl = new Aplat.UI.SectionHeader();
            this.numSX = Field("X", AxisX, "%");
            this.numSY = Field("Y", AxisY, "%");
            this.numSZ = Field("Z", AxisZ, "%");
            this.lblMat = new Aplat.UI.SectionHeader();
            this.btnColor = new Aplat.UI.RoundedButton();
            this.btnTexture = new Aplat.UI.RoundedButton();
            this.btnNoTexture = new Aplat.UI.RoundedButton();
            this.lblTexInfo = new System.Windows.Forms.Label();
            this.chkNoTile = new Aplat.UI.ModernCheckBox();

            this.lblPbr    = new Aplat.UI.SectionHeader();
            this.cmbPreset = new System.Windows.Forms.ComboBox();
            this.lblMetal   = new System.Windows.Forms.Label();
            this.lblRough   = new System.Windows.Forms.Label();
            this.lblEmis    = new System.Windows.Forms.Label();
            this.lblOpacity = new System.Windows.Forms.Label();
            this.trkMetal   = Trk(0, 100, 0);
            this.trkRough   = Trk(0, 100, 0);
            this.trkEmis    = Trk(0, 100, 0);
            this.trkOpacity = Trk(0, 100, 0);
            this.valMetal   = new System.Windows.Forms.Label();
            this.valRough   = new System.Windows.Forms.Label();
            this.valEmis    = new System.Windows.Forms.Label();
            this.valOpacity = new System.Windows.Forms.Label();
            this.btnEmisColor = new Aplat.UI.RoundedButton();
            this.btnAo        = new Aplat.UI.RoundedButton();
            this.btnNoAo      = new Aplat.UI.RoundedButton();
            this.lblAoInfo    = new System.Windows.Forms.Label();

            this.lblPbr.Text     = Loc.T("Материал (PBR)");
            this.lblMetal.Text   = Loc.T("Металл");
            this.lblRough.Text   = Loc.T("Шерохов.");
            this.lblEmis.Text    = Loc.T("Свечение");
            this.lblOpacity.Text = Loc.T("Непрозр.");
            this.btnEmisColor.Text = Loc.T("Цвет свечения");
            this.btnAo.Text        = Loc.T("Карта затенения (AO)...");
            this.btnNoAo.Text      = Loc.T("Убрать карту AO");
            this.lblAoInfo.Text    = Loc.T("AO: нет");
            this.lblAoInfo.AutoSize = false;

            this.cmbPreset.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPreset.FlatStyle     = System.Windows.Forms.FlatStyle.Flat;
            this.cmbPreset.Items.Add(Loc.T("— свой материал —"));   // shown while the settings match no preset
            foreach (string preset in Aplat.Models.Material.PresetNames) this.cmbPreset.Items.Add(Loc.T(preset));
            this.cmbPreset.SelectedIndex = 0;
            this.cmbPreset.SelectedIndexChanged += new System.EventHandler(this.cmbPreset_Changed);

            this.trkMetal.Scroll   += new System.EventHandler(this.OnPbrTrack);
            this.trkRough.Scroll   += new System.EventHandler(this.OnPbrTrack);
            this.trkEmis.Scroll    += new System.EventHandler(this.OnPbrTrack);
            this.trkOpacity.Scroll += new System.EventHandler(this.OnPbrTrack);
            this.btnEmisColor.Click += new System.EventHandler(this.btnEmisColor_Click);
            this.btnAo.Click        += new System.EventHandler(this.btnAo_Click);
            this.btnNoAo.Click      += new System.EventHandler(this.btnNoAo_Click);

            this.panelProps.SuspendLayout();
            this.SuspendLayout();

            // lblNoSel
            this.lblNoSel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblNoSel.Text = Loc.T("Объект не выбран");
            this.lblNoSel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblNoSel.ForeColor = ClTextSoft;
            this.lblNoSel.Font = new System.Drawing.Font(FontName, 9.5F);

            // panelProps — a ScrollHost: scrolls vertically when the editor is taller
            // than the tab; its Content holds the absolutely-positioned rows.
            this.panelProps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelProps.BackColor = ClCard;
            this.panelProps.Resize += new System.EventHandler(this.panelProps_Resize);

            // Name
            this.lblName.Text = Loc.T("Имя");
            this.lblName.Location = new System.Drawing.Point(6, 9);
            this.lblName.Size = new System.Drawing.Size(40, 16);
            this.lblName.AutoSize = false;
            this.lblName.ForeColor = ClText;
            this.txtName.Location = new System.Drawing.Point(48, 6);
            this.txtName.Size = new System.Drawing.Size(152, 20);

            // Position
            this.lblPos.Text = Loc.T("Позиция");
            this.lblPos.Location = new System.Drawing.Point(6, 32);
            this.lblPos.Size = new System.Drawing.Size(196, 16);
            this.lblPos.AutoSize = false;
            this.lblPos.Font = new System.Drawing.Font(this.lblPos.Font, System.Drawing.FontStyle.Bold);
            this.lblPos.ForeColor = ClAccent;

            // Rotation
            this.lblRot.Text = Loc.T("Вращение");
            this.lblRot.Location = new System.Drawing.Point(6, 134);
            this.lblRot.Size = new System.Drawing.Size(196, 16);
            this.lblRot.AutoSize = false;
            this.lblRot.Font = new System.Drawing.Font(this.lblRot.Font, System.Drawing.FontStyle.Bold);
            this.lblRot.ForeColor = ClAccent;

            // Scale
            this.lblScl.Text = Loc.T("Масштаб");
            this.lblScl.Location = new System.Drawing.Point(6, 236);
            this.lblScl.Size = new System.Drawing.Size(196, 16);
            this.lblScl.AutoSize = false;
            this.lblScl.Font = new System.Drawing.Font(this.lblScl.Font, System.Drawing.FontStyle.Bold);
            this.lblScl.ForeColor = ClAccent;


            // Material
            this.lblMat.Text = Loc.T("Материал");
            this.lblMat.Location = new System.Drawing.Point(6, 338);
            this.lblMat.Size = new System.Drawing.Size(196, 16);
            this.lblMat.AutoSize = false;
            this.lblMat.Font = new System.Drawing.Font(this.lblMat.Font, System.Drawing.FontStyle.Bold);
            this.lblMat.ForeColor = ClAccent;
            this.btnColor.Location = new System.Drawing.Point(6, 356);
            this.btnColor.Size = new System.Drawing.Size(196, 34);
            this.btnColor.Text = Loc.T("Цвет материала");
            this.btnColor.Click += new System.EventHandler(this.btnColor_Click);

            // Texture (moved up: the "Type" row was removed)
            this.btnTexture.Location = new System.Drawing.Point(6, 398);
            this.btnTexture.Size = new System.Drawing.Size(196, 34);
            this.btnTexture.Text = Loc.T("Текстура");
            this.btnTexture.Click += new System.EventHandler(this.btnTexture_Click);
            this.btnNoTexture.Location = new System.Drawing.Point(6, 438);
            this.btnNoTexture.Size = new System.Drawing.Size(196, 34);
            this.btnNoTexture.Text = Loc.T("Убрать текстуру");
            this.btnNoTexture.Click += new System.EventHandler(this.btnNoTexture_Click);
            this.lblTexInfo.Text = Loc.T("Текстура: нет");
            this.lblTexInfo.Location = new System.Drawing.Point(6, 480);
            this.lblTexInfo.Size = new System.Drawing.Size(196, 16);
            this.lblTexInfo.AutoSize = false;
            this.lblTexInfo.ForeColor = ClTextSoft;
            this.chkNoTile.Text = Loc.T("Не размножать текстуру");
            this.chkNoTile.Location = new System.Drawing.Point(6, 502);
            this.chkNoTile.AutoSize = true;
            this.chkNoTile.CheckedChanged += new System.EventHandler(this.chkNoTile_Changed);

            this.txtName.Leave += new System.EventHandler(this.txtName_Leave);
            this.txtName.TextChanged += new System.EventHandler(this.txtName_Changed);

            ApplyModernChrome();
            ApplyObjectLayoutMetrics();

            this.panelProps.Content.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.lblName, this.txtName,
                this.lblText3D, this.txtText3D, this.cmbText3DFont, this.numTextSize, this.numTextDepth,
                this.lblPos, this.numPX, this.numPY, this.numPZ,
                this.lblRot, this.numRX, this.numRY, this.numRZ,
                this.lblScl, this.numSX, this.numSY, this.numSZ,
                this.lblMat, this.btnColor,
                this.btnTexture, this.btnNoTexture, this.lblTexInfo, this.chkNoTile,
                this.lblPbr, this.cmbPreset,
                this.lblMetal,   this.trkMetal,   this.valMetal,
                this.lblRough,   this.trkRough,   this.valRough,
                this.lblEmis,    this.trkEmis,    this.valEmis,
                this.lblOpacity, this.trkOpacity, this.valOpacity,
                this.btnEmisColor, this.btnAo, this.btnNoAo, this.lblAoInfo
            });
            this.panelProps.Visible = false;

            this.Controls.Add(this.lblNoSel);
            this.Controls.Add(this.panelProps);
            this.panelProps.ResumeLayout(false);
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = ClCard;
            this.Name = "PropertiesPanel";
            this.Size = new System.Drawing.Size(224, 516);
            this.ResumeLayout(false);
        }

        // Axis colours match the viewport gizmo and floor axes.
        private static readonly System.Drawing.Color AxisX = System.Drawing.Color.FromArgb(236, 72, 92);
        private static readonly System.Drawing.Color AxisY = System.Drawing.Color.FromArgb(128, 190, 52);
        private static readonly System.Drawing.Color AxisZ = System.Drawing.Color.FromArgb(72, 140, 240);

        private Aplat.UI.DragNumber Field(string axis, System.Drawing.Color color, string suffix)
        {
            var n = new Aplat.UI.DragNumber();
            n.Caption      = axis;
            n.CaptionColor = color;
            n.Suffix       = suffix;
            n.Font         = new System.Drawing.Font(FontName, 8.75F);
            n.ValueChanged += new System.EventHandler(this.OnNum);
            n.EditStarting += new System.EventHandler(this.OnFieldEditStarting);
            return n;
        }

        private Aplat.UI.DragNumber TextParam(string caption, string suffix)
        {
            var n = new Aplat.UI.DragNumber();
            n.Caption      = caption;
            n.Suffix       = suffix;
            n.Font         = new System.Drawing.Font(FontName, 8.75F);
            n.ValueChanged += new System.EventHandler(this.OnText3DParam);
            n.EditStarting += new System.EventHandler(this.OnFieldEditStarting);
            return n;
        }

        private Aplat.UI.ModernSlider Trk(int min, int max, int y)
        {
            var t = new Aplat.UI.ModernSlider();
            t.AutoSize    = false;
            t.Location    = new System.Drawing.Point(22, y);
            t.Size        = new System.Drawing.Size(110, 26);
            t.Minimum     = min;
            t.Maximum     = max;
            t.TickStyle   = System.Windows.Forms.TickStyle.None;
            t.BackColor   = ClCard;
            return t;
        }

        private void ApplyModernChrome()
        {
            this.Font = new System.Drawing.Font(FontName, 9F);
            StyleTextBox(this.txtName);
            StyleTextBox(this.txtText3D);
            this.cmbText3DFont.BackColor = Aplat.UI.Theme.Input;
            this.cmbText3DFont.ForeColor = ClText;
            this.cmbText3DFont.Font = new System.Drawing.Font(FontName, 8.75F);

            foreach (System.Windows.Forms.Label l in new System.Windows.Forms.Label[] {
                lblMetal, lblRough, lblEmis, lblOpacity })
            {
                l.ForeColor = ClTextSoft;
                l.Font = new System.Drawing.Font(FontName, 8.5F);
            }

            foreach (System.Windows.Forms.Label l in new System.Windows.Forms.Label[] {
                valMetal, valRough, valEmis, valOpacity })
            {
                l.ForeColor = ClText;
                l.Font = new System.Drawing.Font(FontName, 8.5F);
                l.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
                l.AutoSize = false;
            }

            this.cmbPreset.BackColor = Aplat.UI.Theme.Input;
            this.cmbPreset.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cmbPreset.ForeColor = ClText;
            this.cmbPreset.Font = new System.Drawing.Font(FontName, 8.75F);
            // Owner-drawn text: readable when disabled (locked object) on both themes.
            Aplat.UI.ThemeManager.ThemeCombo(this.cmbPreset);
            this.cmbPreset.DrawItem += this.cmbPreset_DrawItem;   // after the theme's own handler: it adds the ball on top
            Aplat.UI.ThemeManager.ThemeCombo(this.cmbText3DFont);
            this.lblAoInfo.ForeColor = ClTextSoft;
            this.lblAoInfo.Font = new System.Drawing.Font(FontName, 8.5F);

            // Section headers match the tool tabs: "POSITION ─────".
            foreach (System.Windows.Forms.Label l in new System.Windows.Forms.Label[] {
                lblText3D, lblPos, lblRot, lblScl, lblMat, lblPbr })
                l.Font = new System.Drawing.Font(FontName, 7.6F, System.Drawing.FontStyle.Bold);

            StyleActionButton(this.btnColor, Aplat.UI.UiIcon.Color);
            StyleActionButton(this.btnTexture, Aplat.UI.UiIcon.Texture);
            StyleActionButton(this.btnNoTexture, Aplat.UI.UiIcon.Clear);
            StyleActionButton(this.btnEmisColor, Aplat.UI.UiIcon.Color);
            StyleActionButton(this.btnAo, Aplat.UI.UiIcon.Texture);
            StyleActionButton(this.btnNoAo, Aplat.UI.UiIcon.Clear);
            this.chkNoTile.ForeColor = ClText;
            this.chkNoTile.Font = new System.Drawing.Font(FontName, 8.75F);
        }

        private void ApplyObjectLayoutMetrics()
        {
            // Same 16px insets on both sides as the tool tabs; the thin scrollbar
            // lives inside the right inset.
            const int x = 16;
            int contentW = this.panelProps != null ? this.panelProps.ViewportWidth - 2 * x : 244;
            if (contentW < 190) contentW = 190;

            int labelW = contentW;

            this.lblName.Location = new System.Drawing.Point(x, 12);
            this.lblName.Size = new System.Drawing.Size(42, 22);
            this.txtName.Location = new System.Drawing.Point(x + 46, 10);
            this.txtName.Size = new System.Drawing.Size(contentW - 46, 24);

            int y = 46;
            if (_showText3D)
            {
                // TEXT: the words, the font, then height · depth.
                LayoutSection(this.lblText3D, x, y, labelW);
                this.txtText3D.Location = new System.Drawing.Point(x, y + 26);
                this.txtText3D.Size = new System.Drawing.Size(contentW, 24);
                this.cmbText3DFont.Location = new System.Drawing.Point(x, y + 58);
                this.cmbText3DFont.Size = new System.Drawing.Size(contentW, 24);
                int half = (contentW - 6) / 2;
                this.numTextSize.SetBounds(x, y + 90, half, 28);
                this.numTextDepth.SetBounds(x + half + 6, y + 90, contentW - half - 6, 28);
                y += 90 + 28 + 14;
            }
            y = LayoutVectorSection(this.lblPos, this.numPX, this.numPY, this.numPZ, x, y, contentW);
            y = LayoutVectorSection(this.lblRot, this.numRX, this.numRY, this.numRZ, x, y, contentW);
            y = LayoutVectorSection(this.lblScl, this.numSX, this.numSY, this.numSZ, x, y, contentW);

            LayoutSection(this.lblMat, x, y, labelW);
            // Material and PBR keep their rhythm, shifted to follow the fields.
            int d = y - 392;
            this.btnColor.Location = new System.Drawing.Point(x, 418 + d);
            this.btnColor.Size = new System.Drawing.Size(contentW, 36);
            this.btnTexture.Location = new System.Drawing.Point(x, 462 + d);
            this.btnTexture.Size = new System.Drawing.Size(contentW, 36);
            this.btnNoTexture.Location = new System.Drawing.Point(x, 504 + d);
            this.btnNoTexture.Size = new System.Drawing.Size(contentW, 36);
            this.lblTexInfo.Location = new System.Drawing.Point(x, 548 + d);
            this.lblTexInfo.Size = new System.Drawing.Size(contentW, 18);
            this.chkNoTile.Location = new System.Drawing.Point(x, 574 + d);

            // ── PBR ──────────────────────────────────────────────
            LayoutSection(this.lblPbr, x, 606 + d, labelW);
            this.cmbPreset.Location = new System.Drawing.Point(x, 630 + d);
            this.cmbPreset.Size = new System.Drawing.Size(contentW, 24);

            int valW = 38;
            int pbrLabelW = 60;
            int pbrSliderX = x + pbrLabelW + 4;
            int pbrSliderW = contentW - pbrLabelW - valW - 8;
            if (pbrSliderW < 70) pbrSliderW = 70;
            int valX = pbrSliderX + pbrSliderW + 4;

            LayoutPbrRow(this.lblMetal,   this.trkMetal,   this.valMetal,   x, pbrLabelW, pbrSliderX, pbrSliderW, valX, valW, 662 + d);
            LayoutPbrRow(this.lblRough,   this.trkRough,   this.valRough,   x, pbrLabelW, pbrSliderX, pbrSliderW, valX, valW, 688 + d);
            LayoutPbrRow(this.lblEmis,    this.trkEmis,    this.valEmis,    x, pbrLabelW, pbrSliderX, pbrSliderW, valX, valW, 714 + d);
            LayoutPbrRow(this.lblOpacity, this.trkOpacity, this.valOpacity, x, pbrLabelW, pbrSliderX, pbrSliderW, valX, valW, 740 + d);

            this.btnEmisColor.Location = new System.Drawing.Point(x, 774 + d);
            this.btnEmisColor.Size = new System.Drawing.Size(contentW, 34);
            this.btnAo.Location = new System.Drawing.Point(x, 814 + d);
            this.btnAo.Size = new System.Drawing.Size(contentW, 34);
            this.btnNoAo.Location = new System.Drawing.Point(x, 854 + d);
            this.btnNoAo.Size = new System.Drawing.Size(contentW, 34);
            this.lblAoInfo.Location = new System.Drawing.Point(x, 894 + d);
            this.lblAoInfo.Size = new System.Drawing.Size(contentW, 18);

            this.panelProps.Content.Height = LayoutSurfaceControls(x, this.lblAoInfo.Bottom + 18, contentW);
        }

        private static void LayoutSection(System.Windows.Forms.Label label, int x, int y, int width)
        {
            label.Location = new System.Drawing.Point(x, y);
            label.Size = new System.Drawing.Size(width, 18);
        }

        private static void LayoutPbrRow(System.Windows.Forms.Label label,
            Aplat.UI.ModernSlider slider, System.Windows.Forms.Label value,
            int labelX, int labelW, int sliderX, int sliderW, int valX, int valW, int y)
        {
            label.AutoSize = false;
            label.Location = new System.Drawing.Point(labelX, y + 5);
            label.Size = new System.Drawing.Size(labelW, 16);
            slider.Location = new System.Drawing.Point(sliderX, y);
            slider.Size = new System.Drawing.Size(sliderW, 26);
            value.Location = new System.Drawing.Point(valX, y + 5);
            value.Size = new System.Drawing.Size(valW, 16);
        }

        // Section header + one row of three equal fields (X · Y · Z), Figma-style.
        private static int LayoutVectorSection(System.Windows.Forms.Label header,
            Aplat.UI.DragNumber fx, Aplat.UI.DragNumber fy, Aplat.UI.DragNumber fz,
            int x, int y, int width)
        {
            const int gap = 6, rowH = 28;
            LayoutSection(header, x, y, width);
            int w = (width - 2 * gap) / 3;
            int top = y + 26;
            fx.SetBounds(x, top, w, rowH);
            fy.SetBounds(x + w + gap, top, w, rowH);
            fz.SetBounds(x + 2 * (w + gap), top, width - 2 * (w + gap), rowH);
            return top + rowH + 14;
        }

        private void panelProps_Resize(object sender, System.EventArgs e)
        {
            ApplyObjectLayoutMetrics();
        }

        private static void StyleTextBox(System.Windows.Forms.TextBox t)
        {
            t.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            t.BackColor = Aplat.UI.Theme.Input;
            t.ForeColor = ClText;
            t.Font = new System.Drawing.Font(FontName, 9F);
        }

        private static void StyleActionButton(System.Windows.Forms.Button b, Aplat.UI.UiIcon icon)
        {
            b.BackColor = ClBtnFace;
            b.ForeColor = ClText;
            b.Font = new System.Drawing.Font(FontName, 8.9F);
            b.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            b.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            b.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);
            var rb = b as Aplat.UI.RoundedButton;
            if (rb != null)
            {
                rb.Radius = Aplat.UI.Theme.RadiusButton;
                rb.Icon = icon;
            }
        }
    }
}
