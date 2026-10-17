namespace Aplat.Controls
{
    partial class SceneView
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(50, 50, 53);
            this.Cursor = System.Windows.Forms.Cursors.Cross;
            this.Name = "SceneView";
            this.Size = new System.Drawing.Size(700, 500);
            this.ResumeLayout(false);
        }
    }
}
