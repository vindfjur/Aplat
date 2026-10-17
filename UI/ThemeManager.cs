using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aplat.UI
{
    // Applies the current theme to live windows and follows theme switches.
    //
    // Custom-painted controls (RoundedButton, ModernSlider, …) read Theme tokens at
    // paint time and only need a repaint. Standard controls keep the colours they
    // were created with, so on a switch every control's BackColor / ForeColor is
    // re-mapped by *role*: the old palette's "Card" becomes the new palette's
    // "Card", its "Text" the new "Text", and so on. Inputs (text boxes, numeric
    // fields, combos) get the Input well explicitly. Controls whose colour is data —
    // palette swatches, the brush / material colour buttons — are left alone
    // (IThemeExempt or KeepColors).
    //
    // Windows chrome: a dark title bar (DWM) and dark native scrollbars (uxtheme) in
    // Graphite. Both are no-ops where the APIs don't exist (older Windows, Mono).
    public static class ThemeManager
    {
        private static readonly HashSet<Control> Kept = new HashSet<Control>();

        // Tracks a top-level window: themes it now, re-themes it on every switch and
        // un-hooks itself when the window is disposed.
        public static void Attach(Form form)
        {
            if (form == null) return;
            AppIcon.Apply(form);   // never the stock WinForms icon in a title bar
            ApplyChrome(form);
            EventHandler<ThemeChangedEventArgs> onChanged = (s, e) =>
            {
                if (form.IsDisposed) return;
                Remap(form, e.Previous, e.Current);
                ApplyChrome(form);
            };
            Theme.Changed += onChanged;
            form.HandleCreated += (s, e) => ApplyChrome(form);
            form.Disposed      += (s, e) => Theme.Changed -= onChanged;
        }

        // The control's colours carry data (a chosen colour), not a theme role.
        // True for controls whose colours are data (a brush / light / material swatch).
        public static bool IsKept(Control c) { return c != null && Kept.Contains(c); }

        public static void KeepColors(Control c)
        {
            if (c == null || !Kept.Add(c)) return;
            c.Disposed += (s, e) => Kept.Remove(c);
        }

        // ── Re-colouring by role ──────────────────────────────────
        public static void Remap(Control root, ThemePalette from, ThemePalette to)
        {
            if (root == null || from == null || to == null) return;
            var back = BackMap(from, to);
            var fore = ForeMap(from, to);
            root.SuspendLayout();
            try { RemapTree(root, back, fore, to); }
            finally { root.ResumeLayout(true); }
            root.Invalidate(true);
        }

        private static void RemapTree(Control c, Dictionary<int, Color> back, Dictionary<int, Color> fore, ThemePalette to)
        {
            if (!(c is IThemeExempt) && !Kept.Contains(c))
            {
                if (IsInput(c))
                {
                    c.BackColor = to.Input;
                    c.ForeColor = to.Text;
                }
                else
                {
                    c.BackColor = Map(back, c.BackColor);
                    c.ForeColor = Map(fore, c.ForeColor);
                }
            }

            // Labels showing a UiIcon (Tag) regenerate the glyph in their new colour.
            var label = c as Label;
            if (label != null && label.Tag is UiIcon && label.Image != null)
            {
                int size = label.Image.Width;
                var old = label.Image;
                label.Image = IconFactory.Create((UiIcon)label.Tag, label.ForeColor, size);
                old.Dispose();
            }

            var strip = c as ToolStrip;
            if (strip != null) RemapItems(strip.Items, back, fore);

            ApplyNative(c);
            foreach (Control child in c.Controls) RemapTree(child, back, fore, to);
        }

        private static void RemapItems(ToolStripItemCollection items, Dictionary<int, Color> back, Dictionary<int, Color> fore)
        {
            foreach (ToolStripItem it in items)
            {
                it.BackColor = Map(back, it.BackColor);
                it.ForeColor = Map(fore, it.ForeColor);
                var drop = it as ToolStripDropDownItem;
                if (drop != null && drop.HasDropDownItems) RemapItems(drop.DropDownItems, back, fore);
            }
        }

        // Owner-draws a combo box's text (closed field and list rows) in theme colours.
        // A disabled native combo paints its field with the system face colour and
        // GrayText — a light slab on the dark theme; here it stays a dark well with the
        // readable disabled tone. Idempotent: a combo is hooked once.
        public static void ThemeCombo(ComboBox cb)
        {
            if (cb == null || cb.DrawMode != DrawMode.Normal) return;
            cb.DrawMode   = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = Math.Max(cb.ItemHeight, (int)Math.Ceiling(cb.Font.GetHeight()) + 4);
            cb.DrawItem  += DrawComboItem;
            cb.EnabledChanged += (s, e) => ((Control)s).Invalidate();
            Chromes.Add(new ComboChrome(cb));
            cb.Disposed += (s, e) => Chromes.RemoveWhere(x => x.Owner == s);
        }

        private static readonly HashSet<ComboChrome> Chromes = new HashSet<ComboChrome>();

        // Optional per-row font for a combo's drop-down list (a font picker shows each
        // family in its own face, as Word does). The closed field keeps the UI font.
        private static readonly Dictionary<ComboBox, Func<string, Font>> RowFonts =
            new Dictionary<ComboBox, Func<string, Font>>();

        public static void SetComboRowFont(ComboBox cb, Func<string, Font> rowFont)
        {
            if (cb == null) return;
            RowFonts[cb] = rowFont;
            cb.Disposed += (s, e) => RowFonts.Remove((ComboBox)s);
        }

        // The flat combo's frame and drop-down button are painted by WinForms in
        // system colours (a white outline, a light-grey button) — glaring on the dark
        // theme. This hook lets the control paint first, then repaints the frame, the
        // 2px ring around the field and the button in theme colours with a chevron.
        private sealed class ComboChrome : NativeWindow
        {
            private const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085,
                              WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_ENABLE = 0x000A;
            public readonly ComboBox Owner;

            public ComboChrome(ComboBox cb)
            {
                Owner = cb;
                if (cb.IsHandleCreated) Attach();
                cb.HandleCreated   += (s, e) => Attach();
                cb.HandleDestroyed += (s, e) => { if (Handle != IntPtr.Zero) ReleaseHandle(); };
            }

            private void Attach()
            {
                try { if (Handle == IntPtr.Zero) AssignHandle(Owner.Handle); } catch { }
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_PAINT || m.Msg == WM_NCPAINT || m.Msg == WM_SETFOCUS
                    || m.Msg == WM_KILLFOCUS || m.Msg == WM_ENABLE)
                    PaintChrome();
            }

            private void PaintChrome()
            {
                if (Owner.IsDisposed || Owner.Width < 8 || Owner.Height < 8) return;
                try
                {
                    using (var g = Graphics.FromHwnd(Owner.Handle))
                    {
                        bool on = Owner.Enabled;
                        Color face = on ? Theme.Input : Theme.BtnFace;
                        var r = new Rectangle(0, 0, Owner.Width - 1, Owner.Height - 1);

                        // Ring between the frame and the owner-drawn field.
                        using (var p = new Pen(face)) { g.DrawRectangle(p, 1, 1, r.Width - 2, r.Height - 2);
                                                        g.DrawRectangle(p, 2, 2, r.Width - 4, r.Height - 4); }

                        // Drop-down button: same well as the field, a chevron, a hairline.
                        // The native button's own edge sits a few px left of the nominal
                        // width on some platforms: cover a little more than the button.
                        int bw = Math.Min(SystemInformation.VerticalScrollBarWidth + 4, Owner.Width / 3);
                        var btn = new Rectangle(r.Right - bw, 1, bw, r.Height - 1);
                        using (var b = new SolidBrush(face)) g.FillRectangle(b, btn);
                        using (var p = new Pen(Theme.BorderSoft))
                            g.DrawLine(p, btn.Left, 5, btn.Left, r.Bottom - 5);

                        float cx = btn.Left + btn.Width / 2f + 0.5f, cy = r.Height / 2f + 0.5f;
                        var old = g.SmoothingMode;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using (var p = new Pen(on ? Theme.TextSoft : Theme.TextDisabled, 1.6f)
                               { StartCap = System.Drawing.Drawing2D.LineCap.Round,
                                 EndCap   = System.Drawing.Drawing2D.LineCap.Round,
                                 LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                            g.DrawLines(p, new[] { new PointF(cx - 4f, cy - 2f), new PointF(cx, cy + 2f),
                                                   new PointF(cx + 4f, cy - 2f) });
                        g.SmoothingMode = old;

                        // Frame: theme border, accent while focused.
                        using (var p = new Pen(on && Owner.Focused ? Theme.Accent : (on ? Theme.Border : Theme.BorderSoft)))
                            g.DrawRectangle(p, r);
                    }
                }
                catch { /* painting is best-effort; never take the app down */ }
            }
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var cb = (ComboBox)sender;
            bool field    = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool disabled = !cb.Enabled || (e.State & DrawItemState.Disabled) != 0;
            bool selected = !field && (e.State & DrawItemState.Selected) != 0;

            Color bg = field ? (disabled ? Theme.BtnFace : Theme.Input)
                             : (selected ? Theme.AccentSoft : Theme.Card);
            Color fg = disabled ? Theme.TextDisabled : (selected ? Theme.Accent : Theme.Text);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

            string text = e.Index >= 0 && e.Index < cb.Items.Count ? cb.GetItemText(cb.Items[e.Index]) : cb.Text;
            var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
            Font font = cb.Font;
            Func<string, Font> rowFont;
            if (!field && RowFonts.TryGetValue(cb, out rowFont)) font = rowFont(text) ?? cb.Font;
            TextKit.Draw(e.Graphics, text, font, fg, r, HAlign.Left, ellipsis: true);
        }

        private static bool IsInput(Control c)
        {
            return c is TextBoxBase || c is NumericUpDown || c is ComboBox || c is ListBox;
        }

        private static Color Map(Dictionary<int, Color> map, Color c)
        {
            Color to;
            return map.TryGetValue(c.ToArgb(), out to) ? to : c;
        }

        // Surfaces first: when two roles share a colour in the old palette, the
        // earlier role wins (e.g. Light's Card and Input are both near-white).
        private static Dictionary<int, Color> BackMap(ThemePalette a, ThemePalette b)
        {
            var m = new Dictionary<int, Color>();
            Put(m, a.Card, b.Card);           Put(m, a.Bg, b.Bg);
            Put(m, a.Surface, b.Surface);     Put(m, a.Input, b.Input);
            Put(m, a.BtnFace, b.BtnFace);     Put(m, a.BtnHover, b.BtnHover);
            Put(m, a.BtnDown, b.BtnDown);     Put(m, a.AccentSoft, b.AccentSoft);
            Put(m, a.Accent, b.Accent);       Put(m, a.AccentHover, b.AccentHover);
            Put(m, a.BorderSoft, b.BorderSoft); Put(m, a.Border, b.Border);
            Put(m, a.SliderTrack, b.SliderTrack);
            Put(m, SystemColors.Control, b.Surface);
            Put(m, SystemColors.Window, b.Card);
            return m;
        }

        private static Dictionary<int, Color> ForeMap(ThemePalette a, ThemePalette b)
        {
            var m = new Dictionary<int, Color>();
            Put(m, a.Text, b.Text);           Put(m, a.TextSoft, b.TextSoft);
            Put(m, a.TextMuted, b.TextMuted); Put(m, a.Accent, b.Accent);
            Put(m, a.TextDisabled, b.TextDisabled);
            Put(m, a.AccentText, b.AccentText);
            Put(m, SystemColors.ControlText, b.Text);
            Put(m, SystemColors.WindowText, b.Text);
            Put(m, SystemColors.GrayText, b.TextDisabled);
            return m;
        }

        private static void Put(Dictionary<int, Color> m, Color from, Color to)
        {
            int key = from.ToArgb();
            if (!m.ContainsKey(key)) m[key] = to;
        }

        // ── Native chrome (Windows 10 1809+ / 11) ─────────────────
        public static void ApplyChrome(Form form)
        {
            if (!IsWindows || form == null || !form.IsHandleCreated) return;
            int dark = Theme.IsDark ? 1 : 0;
            try
            {
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; 19 on pre-20H1 builds.
                if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
                // Repaint the non-client area now, not on the next activation.
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            ApplyNativeTree(form);
        }

        private static void ApplyNativeTree(Control c)
        {
            ApplyNative(c);
            foreach (Control child in c.Controls) ApplyNativeTree(child);
        }

        // Dark scrollbars / edit borders for the native controls that draw them.
        private static void ApplyNative(Control c)
        {
            if (!IsWindows) return;
            string theme;
            if (c is ComboBox)                                      theme = Theme.IsDark ? "DarkMode_CFD" : null;
            else if (c is ListView || c is TreeView || c is TextBoxBase ||
                     (c is ScrollableControl && ((ScrollableControl)c).AutoScroll))
                                                                     theme = Theme.IsDark ? "DarkMode_Explorer" : "Explorer";
            else return;

            if (!c.IsHandleCreated)
            {
                // Once, when the window exists.
                EventHandler h = null;
                h = (s, e) => { c.HandleCreated -= h; ApplyNative(c); };
                c.HandleCreated += h;
                return;
            }
            try { SetWindowTheme(c.Handle, theme, null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        // ── Dialogs ───────────────────────────────────────────────
        // One look for every dialog: theme font and surface, flat buttons with the
        // AcceptButton in the accent, input wells, spinner-free numeric fields.
        // Call it first thing in the dialog's constructor: styling runs on Load, when
        // all controls exist, and the dialog then follows theme switches.
        public static void StyleDialog(Form dialog)
        {
            if (dialog == null) return;
            dialog.Font      = Theme.Font(9f);
            dialog.BackColor = Theme.Card;
            dialog.ForeColor = Theme.Text;
            Attach(dialog);
            dialog.Load += (s, e) =>
            {
                StyleDialogTree(dialog, dialog);
                BalanceMargins(dialog);
            };
        }

        // Fixed dialogs laid out by coordinates: grow the client area so the right and
        // bottom margins equal the left and top ones (controls got taller when styled,
        // and hand-placed layouts tend to end flush against the frame).
        private static void BalanceMargins(Form dialog)
        {
            if (dialog.FormBorderStyle != FormBorderStyle.FixedDialog) return;
            int minLeft = int.MaxValue, minTop = int.MaxValue, maxRight = 0, maxBottom = 0;
            foreach (Control c in dialog.Controls)
            {
                if (!c.Visible && c.IsHandleCreated) continue;
                if (c.Dock != DockStyle.None) return;   // docked layouts size themselves
                minLeft   = Math.Min(minLeft, c.Left);
                minTop    = Math.Min(minTop, c.Top);
                maxRight  = Math.Max(maxRight, c.Right);
                maxBottom = Math.Max(maxBottom, c.Bottom);
            }
            if (maxRight == 0) return;
            var size = dialog.ClientSize;
            dialog.ClientSize = new Size(Math.Max(size.Width, maxRight + minLeft),
                                         Math.Max(size.Height, maxBottom + minTop));
        }

        private static void StyleDialogTree(Form dialog, Control c)
        {
            foreach (Control child in c.Controls)
            {
                if (!(child is IThemeExempt) && !Kept.Contains(child)) StyleDialogControl(dialog, child);
                StyleDialogTree(dialog, child);
            }
        }

        private static void StyleDialogControl(Form dialog, Control c)
        {
            var button = c as Button;
            if (button != null && !(button is RoundedButton))
            {
                bool primary = ReferenceEquals(dialog.AcceptButton, button);
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.Height    = Math.Max(button.Height, 30);
                button.BackColor = primary ? Theme.Accent : Theme.BtnFace;
                button.ForeColor = primary ? Theme.AccentText : Theme.Text;
                button.FlatAppearance.BorderColor        = primary ? Theme.Accent : Theme.Border;
                button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.BtnHover;
                button.FlatAppearance.MouseDownBackColor = primary ? Theme.AccentHover : Theme.BtnDown;
                if (primary) KeepColors(button);   // stays accent through a theme switch
                return;
            }

            var check = c as ButtonBase;
            if (check is CheckBox || check is RadioButton)
            {
                check.FlatStyle = FlatStyle.Flat;
                check.ForeColor = Theme.Text;
                return;
            }

            var num = c as NumericUpDown;
            if (num != null && num.Controls.Count > 0) num.Controls[0].Visible = false;

            if (IsInput(c))
            {
                c.BackColor = Theme.Input;
                c.ForeColor = Theme.Text;
                var box = c as TextBox;
                if (box != null && box.BorderStyle == BorderStyle.Fixed3D) box.BorderStyle = BorderStyle.FixedSingle;
                if (num != null) num.BorderStyle = BorderStyle.FixedSingle;
                var combo = c as ComboBox;
                if (combo != null) { combo.FlatStyle = FlatStyle.Flat; ThemeCombo(combo); }
            }
        }

        // Gives every drop-down under `strip` (menus, submenus, split buttons) the same
        // renderer as the strip itself. Drop-downs otherwise fall back to the global
        // ToolStripManager renderer on some runtimes, with system-grey margins.
        public static void ShareRenderer(ToolStrip strip)
        {
            if (strip == null) return;
            ShareRenderer(strip.Items, strip.Renderer);
        }

        private static void ShareRenderer(ToolStripItemCollection items, ToolStripRenderer renderer)
        {
            foreach (ToolStripItem it in items)
            {
                var drop = it as ToolStripDropDownItem;
                if (drop == null) continue;
                drop.DropDown.Renderer = renderer;
                ShareRenderer(drop.DropDownItems, renderer);
            }
        }

        // ── Tooltips in the theme's colours ───────────────────────
        public static void Style(ToolTip tip)
        {
            if (tip == null) return;
            tip.OwnerDraw = true;
            tip.Draw += (s, e) =>
            {
                var r = e.Bounds;
                using (var bg = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(bg, r);
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                TextKit.Draw(e.Graphics, e.ToolTipText, TooltipFont, Theme.Text, r, HAlign.Center);
            };
            tip.Popup += (s, e) =>
            {
                string text = ((ToolTip)s).GetToolTip(e.AssociatedControl);
                e.ToolTipSize = new Size(TextKit.InkWidth(text ?? string.Empty, TooltipFont) + 20,
                                         TextKit.CapHeight(TooltipFont) + 18);
            };
        }

        private static Font _tooltipFont;
        private static Font TooltipFont
        {
            get { return _tooltipFont ?? (_tooltipFont = Theme.Font(8.75f)); }
        }

        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT && Type.GetType("Mono.Runtime") == null; }
        }

        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
                           SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    // Marker: a control whose colours are content (e.g. a colour swatch) and must
    // never be re-mapped by a theme switch.
    public interface IThemeExempt { }
}
