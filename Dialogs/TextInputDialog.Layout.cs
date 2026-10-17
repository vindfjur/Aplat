namespace Aplat.Dialogs
{
    // Real designer file: the WinForms visual designer reads InitializeComponent()
    // below, so this dialog opens and is editable on the design surface in VS.
    // Runtime-only work (enumerating installed fonts, reading results) lives in the
    // hand-written TextInputDialog.cs partial.
    partial class TextInputDialog : System.Windows.Forms.Form
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label    lblText;
        private System.Windows.Forms.Label    lblFont;
        private System.Windows.Forms.Label    lblSize;
        private System.Windows.Forms.TextBox  _txt;
        private System.Windows.Forms.ComboBox _font;
        private System.Windows.Forms.NumericUpDown _size;
        private System.Windows.Forms.Button   btnOk;
        private System.Windows.Forms.Button   btnCancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblText   = new System.Windows.Forms.Label();
            this.lblFont   = new System.Windows.Forms.Label();
            this.lblSize   = new System.Windows.Forms.Label();
            this._txt      = new System.Windows.Forms.TextBox();
            this._font     = new System.Windows.Forms.ComboBox();
            this._size     = new System.Windows.Forms.NumericUpDown();
            this.btnOk     = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this._size)).BeginInit();
            this.SuspendLayout();
            //
            // lblText
            //
            this.lblText.AutoSize  = true;
            this.lblText.ForeColor = Aplat.UI.Theme.Text;
            this.lblText.Location  = new System.Drawing.Point(18, 21);
            this.lblText.Name      = "lblText";
            this.lblText.Text      = Loc.T("Текст:");
            //
            // lblFont
            //
            this.lblFont.AutoSize  = true;
            this.lblFont.ForeColor = Aplat.UI.Theme.Text;
            this.lblFont.Location  = new System.Drawing.Point(18, 61);
            this.lblFont.Name      = "lblFont";
            this.lblFont.Text      = Loc.T("Шрифт:");
            //
            // lblSize
            //
            this.lblSize.AutoSize  = true;
            this.lblSize.ForeColor = Aplat.UI.Theme.Text;
            this.lblSize.Location  = new System.Drawing.Point(18, 101);
            this.lblSize.Name      = "lblSize";
            this.lblSize.Text      = Loc.T("Размер:");
            //
            // _txt
            //
            this._txt.Location = new System.Drawing.Point(110, 18);
            this._txt.Name     = "_txt";
            this._txt.Size     = new System.Drawing.Size(230, 23);
            this._txt.TabIndex = 0;
            this._txt.Text     = "Text";
            //
            // _font
            //
            this._font.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._font.Location      = new System.Drawing.Point(110, 58);
            this._font.Name          = "_font";
            this._font.Size          = new System.Drawing.Size(230, 21);
            this._font.TabIndex      = 1;
            //
            // _size
            //
            this._size.DecimalPlaces = 1;
            this._size.Increment     = new decimal(new int[] { 5, 0, 0, 65536 });
            this._size.Location      = new System.Drawing.Point(110, 98);
            this._size.Maximum       = new decimal(new int[] { 6, 0, 0, 0 });
            this._size.Minimum       = new decimal(new int[] { 5, 0, 0, 65536 });
            this._size.Name          = "_size";
            this._size.Size          = new System.Drawing.Size(90, 23);
            this._size.TabIndex      = 2;
            this._size.Value         = new decimal(new int[] { 15, 0, 0, 65536 });
            //
            // btnOk
            //
            this.btnOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOk.FlatStyle    = System.Windows.Forms.FlatStyle.System;
            this.btnOk.Location     = new System.Drawing.Point(196, 142);
            this.btnOk.Name         = "btnOk";
            this.btnOk.Size         = new System.Drawing.Size(70, 26);
            this.btnOk.TabIndex     = 3;
            this.btnOk.Text         = Loc.T("ОК");
            this.btnOk.Click       += new System.EventHandler(this.btnOk_Click);
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.FlatStyle    = System.Windows.Forms.FlatStyle.System;
            this.btnCancel.Location     = new System.Drawing.Point(272, 142);
            this.btnCancel.Name         = "btnCancel";
            this.btnCancel.Size         = new System.Drawing.Size(78, 26);
            this.btnCancel.TabIndex     = 4;
            this.btnCancel.Text         = Loc.T("Отмена");
            //
            // TextInputDialog
            //
            this.AcceptButton        = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor           = Aplat.UI.Theme.Card;
            this.CancelButton        = this.btnCancel;
            this.ClientSize          = new System.Drawing.Size(360, 196);
            this.Controls.Add(this.lblText);
            this.Controls.Add(this.lblFont);
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this._txt);
            this.Controls.Add(this._font);
            this.Controls.Add(this._size);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.btnCancel);
            this.Font            = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.Name            = "TextInputDialog";
            this.StartPosition   = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text            = Loc.T("Объёмный текст");
            ((System.ComponentModel.ISupportInitialize)(this._size)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
