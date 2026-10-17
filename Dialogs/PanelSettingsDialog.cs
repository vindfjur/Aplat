using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aplat.Dialogs
{
    // "View → Panels → Position and transparency…": every panel of the window in one list, toolbars and
    // floating cards alike — whether it is shown, where it sits (an edge or corner, or free / a window for
    // a toolbar) — plus one transparency for the floating cards and a button that restores the default
    // layout. Everything is applied live through the callbacks given to AddRow, so the user sees the
    // changes while the dialog is open; there is just a Close button.
    public sealed partial class PanelSettingsDialog : Form
    {
        private bool _ready;

        // new opacity in percent (50–100).
        public event Action<int> OpacityChanged;
        public event EventHandler ResetRequested;

        public PanelSettingsDialog()
        {
            InitializeComponent();
            Aplat.UI.ThemeManager.StyleDialog(this);

            _opacity.Scroll      += (s, e) => ApplyOpacity();
            _closeButton.Click   += (s, e) => Close();
            _resetButton.Click   += (s, e) =>
            {
                var h = ResetRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
        }

        // A small bold header that starts a group of rows.
        public void AddSection(string title)
        {
            var label = new Label
            {
                Text      = title,
                AutoSize  = true,
                Font      = new Font(Aplat.UI.Theme.FontFamily, 8.5F, FontStyle.Bold),
                ForeColor = Aplat.UI.Theme.TextSoft,
                Margin    = new Padding(0, _rows.RowCount == 0 ? 0 : 12, 0, 4)
            };
            _rows.RowCount++;
            _rows.Controls.Add(label, 0, _rows.RowCount - 1);
            _rows.SetColumnSpan(label, 2);
        }

        // One panel: a "shown" check box and a position list. onShown / onPosition run on every change.
        public void AddRow(string name, bool shown, Action<bool> onShown, string[] positions, int selected, Action<int> onPosition)
        {
            var cb = new Aplat.UI.ModernCheckBox
            {
                Text      = name,
                Checked   = shown,
                AutoSize  = true,
                Margin    = new Padding(0, 5, 0, 5),
                ForeColor = Aplat.UI.Theme.Text,
                BackColor = Aplat.UI.Theme.Card
            };
            cb.CheckedChanged += (s, e) => { if (_ready) onShown(cb.Checked); };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width  = 176,
                Margin = new Padding(0, 2, 0, 2)
            };
            combo.Items.AddRange(positions);
            combo.SelectedIndex = Math.Max(0, Math.Min(positions.Length - 1, selected));
            combo.SelectedIndexChanged += (s, e) => { if (_ready) onPosition(combo.SelectedIndex); };

            _rows.RowCount++;
            _rows.Controls.Add(cb, 0, _rows.RowCount - 1);
            _rows.Controls.Add(combo, 1, _rows.RowCount - 1);
        }

        public int OpacityPercent
        {
            get { return _opacity.Value; }
            set { _opacity.Value = Math.Max(_opacity.Minimum, Math.Min(_opacity.Maximum, value)); }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Lay out the lower controls now that the row count (and thus height) is known.
            _rows.PerformLayout();
            int y = _rows.Bottom + 16;
            _opacityHeader.Top = y; y += 26;
            _opacity.Location    = new Point(14, y);
            _opacityVal.Location = new Point(_opacity.Right + 6, y + 6);
            y += _opacity.Height + 14;
            _resetButton.Location = new Point(16, y);
            _closeButton.Location = new Point(ClientSize.Width - _closeButton.Width - 16, y);
            ClientSize = new Size(ClientSize.Width, y + _closeButton.Height + 16);

            UpdateOpacityLabel();
            _ready = true;
        }

        private void ApplyOpacity()
        {
            UpdateOpacityLabel();
            var h = OpacityChanged;
            if (h != null) h(_opacity.Value);
        }

        private void UpdateOpacityLabel()
        {
            _opacityVal.Text = _opacity.Value + "%";
        }
    }
}
