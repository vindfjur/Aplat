namespace Aplat.Dialogs
{
    partial class PanelSettingsDialog : System.Windows.Forms.Form
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel _rows;
        private System.Windows.Forms.Label _opacityHeader;
        private Aplat.UI.ModernSlider _opacity;
        private System.Windows.Forms.Label _opacityVal;
        private System.Windows.Forms.Button _resetButton;
        private System.Windows.Forms.Button _closeButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._rows = new System.Windows.Forms.TableLayoutPanel();
            this._opacityHeader = new System.Windows.Forms.Label();
            this._opacity = new Aplat.UI.ModernSlider();
            this._opacityVal = new System.Windows.Forms.Label();
            this._resetButton = new System.Windows.Forms.Button();
            this._closeButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // _rows: one line per panel (shown / hidden + where it sits), grouped under small headers
            //
            this._rows.AutoSize = true;
            this._rows.BackColor = Aplat.UI.Theme.Card;
            this._rows.ColumnCount = 2;
            this._rows.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 250F));
            this._rows.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 186F));
            this._rows.Location = new System.Drawing.Point(16, 14);
            this._rows.Name = "_rows";
            this._rows.Width = 436;
            //
            // _opacityHeader
            //
            this._opacityHeader.AutoSize = true;
            this._opacityHeader.Font = new System.Drawing.Font(Aplat.UI.Theme.FontFamily, 8.5F, System.Drawing.FontStyle.Bold);
            this._opacityHeader.ForeColor = Aplat.UI.Theme.TextSoft;
            this._opacityHeader.Location = new System.Drawing.Point(16, 0);
            this._opacityHeader.Name = "opacityHeader";
            this._opacityHeader.Text = Loc.T("Прозрачность плавающих панелей");
            //
            // _opacity
            //
            this._opacity.AutoSize = false;
            this._opacity.BackColor = Aplat.UI.Theme.Card;
            this._opacity.Height = 36;
            this._opacity.Location = new System.Drawing.Point(14, 120);
            this._opacity.Maximum = 100;
            this._opacity.Minimum = 50;
            this._opacity.Name = "_opacity";
            this._opacity.TickStyle = System.Windows.Forms.TickStyle.None;
            this._opacity.Value = 100;
            this._opacity.Width = 366;
            //
            // _opacityVal
            //
            this._opacityVal.AutoSize = false;
            this._opacityVal.ForeColor = Aplat.UI.Theme.TextSoft;
            this._opacityVal.Height = 24;
            this._opacityVal.Location = new System.Drawing.Point(386, 126);
            this._opacityVal.Name = "_opacityVal";
            this._opacityVal.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this._opacityVal.Width = 50;
            //
            // _resetButton
            //
            this._resetButton.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this._resetButton.Location = new System.Drawing.Point(16, 208);
            this._resetButton.Name = "_resetButton";
            this._resetButton.Text = Loc.T("Сбросить расположение");
            this._resetButton.Width = 270;
            //
            // _closeButton
            //
            this._closeButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this._closeButton.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this._closeButton.Location = new System.Drawing.Point(246, 208);
            this._closeButton.Name = "_closeButton";
            this._closeButton.Text = Loc.T("Закрыть");
            this._closeButton.Width = 90;
            //
            // PanelSettingsDialog
            //
            this.AcceptButton = this._closeButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = Aplat.UI.Theme.Card;
            this.CancelButton = this._closeButton;
            this.ClientSize = new System.Drawing.Size(468, 250);
            this.Controls.Add(this._rows);
            this.Controls.Add(this._opacityHeader);
            this.Controls.Add(this._opacity);
            this.Controls.Add(this._opacityVal);
            this.Controls.Add(this._resetButton);
            this.Controls.Add(this._closeButton);
            this.Font = new System.Drawing.Font(Aplat.UI.Theme.FontFamily, 9F);
            this.ForeColor = Aplat.UI.Theme.Text;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PanelSettingsDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = Loc.T("Панели");
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
