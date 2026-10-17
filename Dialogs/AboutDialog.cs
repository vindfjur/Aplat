using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Aplat.UI;

namespace Aplat.Dialogs
{
    // "Help → About": name, version, licence, where the source lives, and a way to check for updates.
    // The links are fixed addresses (the project page and its licence files), nothing is read from the scene.
    public sealed class AboutDialog : Form
    {
        internal const string ProjectUrl = "https://github.com/vindfjur/Aplat";
        internal const string LicenseUrl = ProjectUrl + "/blob/main/LICENSE.txt";
        internal const string NoticesUrl = ProjectUrl + "/blob/main/THIRD_PARTY_NOTICES.md";

        public AboutDialog(Action checkUpdates)
        {
            Text            = Loc.T("О программе");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            ClientSize      = new Size(420, 356);
            ThemeManager.StyleDialog(this);

            int left = 24, textX = 112, y = 22;
            var logo = new PictureBox { Location = new Point(left, y), Size = new Size(72, 72), SizeMode = PictureBoxSizeMode.Zoom };
            logo.Image = LoadLogo();
            Controls.Add(logo);

            var name = new Label { Text = "Aplat", AutoSize = true, Location = new Point(textX, y - 2), Font = Theme.Font(20f, FontStyle.Bold) };
            Controls.Add(name);
            var ver = UpdateCheck.Current;
            Controls.Add(new Label
            {
                Text = string.Format(Loc.T("Версия {0}"), ver.Major + "." + ver.Minor + "." + Math.Max(0, ver.Build)),
                AutoSize = true, Location = new Point(textX, y + 40), ForeColor = Theme.TextSoft,
            });
            Controls.Add(new Label
            {
                Text = string.Format(Loc.T("Разработчик: {0}"), "vindfjur"),
                AutoSize = true, Location = new Point(textX, y + 62), ForeColor = Theme.TextSoft,
            });

            y = 112;
            var about = new Label
            {
                Text = Loc.T("Aplat — программа для рисования на 3D-холсте: кисти, фигуры и текст на листе в пространстве, сцены из примитивов со светом, мягкими тенями и прозрачностью, экспорт в OBJ, STL и FBX."),
                AutoSize = true, MaximumSize = new Size(420 - 2 * left, 0), Location = new Point(left, y),
            };
            Controls.Add(about);
            y += about.PreferredSize.Height + 16;
            Controls.Add(new Label { Text = Copyright(), AutoSize = true, Location = new Point(left, y), ForeColor = Theme.TextSoft });
            y += 24;
            Controls.Add(new Label { Text = Loc.T("Свободная программа, лицензия Apache 2.0."), AutoSize = true, Location = new Point(left, y) });
            y += 30;
            Link(Loc.T("Страница проекта на GitHub"), ProjectUrl, left, ref y);
            Link(Loc.T("Текст лицензии"), LicenseUrl, left, ref y);
            Link(Loc.T("Лицензии используемых компонентов"), NoticesUrl, left, ref y);

            var btnUpdate = new Button { Text = Loc.T("Проверить обновления…"), Location = new Point(left, y + 14), Size = new Size(190, 30), FlatStyle = FlatStyle.System };
            btnUpdate.Click += (s, e) => { if (checkUpdates != null) { DialogResult = DialogResult.OK; Close(); checkUpdates(); } };
            btnUpdate.Enabled = checkUpdates != null;
            Controls.Add(btnUpdate);

            var close = new Button { Text = Loc.T("Закрыть"), DialogResult = DialogResult.Cancel, Location = new Point(420 - left - 100, y + 14), Size = new Size(100, 30), FlatStyle = FlatStyle.System };
            Controls.Add(close);
            AcceptButton = close; CancelButton = close;
            ClientSize = new Size(420, y + 14 + 30 + 24);
        }

        private void Link(string text, string url, int x, ref int y)
        {
            var l = new LinkLabel
            {
                Text = text, AutoSize = true, Location = new Point(x, y),
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent,
            };
            l.LinkClicked += (s, e) => { try { Process.Start(url); } catch (Exception) { } };
            Controls.Add(l);
            y += 24;
        }

        private static string Copyright()
        {
            var a = (AssemblyCopyrightAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyCopyrightAttribute));
            return a != null ? a.Copyright : "";
        }

        // The app's picture (assets/brand/about.png, 256 px), embedded in the exe.
        private static Image LoadLogo()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aplat.about.png"))
                using (var img = Image.FromStream(s))
                    return new Bitmap(img);
            }
            catch (Exception) { return null; }
        }
    }
}
