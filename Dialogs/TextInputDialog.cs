using System;
using System.Windows.Forms;

namespace Aplat.Dialogs
{
    // Small dialog for the "3D text" feature: enter the string, pick a font and an
    // overall size. The visual layout lives in TextInputDialog.Layout.cs (editable
    // in the VS designer); only the runtime behaviour is here — enumerating the
    // installed fonts and committing the chosen values when OK is pressed.
    public partial class TextInputDialog : Form
    {
        public string Text3D    { get; private set; } = "Text";
        public string FontName  { get; private set; } = "Segoe UI";
        // Overall height of the text in world units (depth is derived as height * 0.28).
        public float  TextHeight { get; private set; } = 1.5f;

        public TextInputDialog()
        {
            InitializeComponent();
            Aplat.UI.ThemeManager.StyleDialog(this);

            // The font list is a runtime concern (depends on what's installed), so it
            // is populated here rather than baked into the designer.
            foreach (var ff in System.Drawing.FontFamily.Families)
                _font.Items.Add(ff.Name);
            SelectFont("Segoe UI");
        }

        // Prefill for "Edit the text…" — editing an existing 3D-text object.
        public void Preset(string text, string fontName, float height)
        {
            if (!string.IsNullOrEmpty(text)) _txt.Text = text;
            if (!string.IsNullOrEmpty(fontName)) SelectFont(fontName);
            if (height > 0.01f)
            {
                decimal v = (decimal)height;
                if (v < _size.Minimum) v = _size.Minimum;
                if (v > _size.Maximum) v = _size.Maximum;
                _size.Value = v;
            }
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            Text3D     = string.IsNullOrWhiteSpace(_txt.Text) ? "Text" : _txt.Text;
            FontName   = _font.SelectedItem != null ? _font.SelectedItem.ToString() : "Segoe UI";
            TextHeight = (float)_size.Value;
        }

        private void SelectFont(string name)
        {
            int idx = FallbackFontIndex(_font.Items, name);
            if (idx >= 0) _font.SelectedIndex = idx;
        }

        // The requested font, else a common sans (never the first family in the list —
        // that is often a symbol font, which would turn letters into pictograms).
        public static int FallbackFontIndex(System.Collections.IList items, string name)
        {
            foreach (var candidate in new[] { name, "Segoe UI", "Arial", "Helvetica", "DejaVu Sans",
                                              "Liberation Sans", System.Drawing.FontFamily.GenericSansSerif.Name })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                int i = items.IndexOf(candidate);
                if (i >= 0) return i;
            }
            return items.Count > 0 ? 0 : -1;
        }
    }
}
