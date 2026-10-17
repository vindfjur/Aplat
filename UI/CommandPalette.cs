using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Aplat.UI
{
    // One runnable entry of the command palette.
    public sealed class PaletteCommand
    {
        public string Title;      // "Duplicate"
        public string Group;      // "Edit" / "View › Theme"
        public string Hint;       // optional one-liner
        public string Shortcut;   // "Ctrl+D"
        public UiIcon Icon;
        public bool   Checked;
        public bool   Enabled = true;   // unavailable commands are listed dimmed and do nothing
        public string Aliases;          // other names, ';'-separated: the other language, synonyms
        public Action Run;

        internal string Key { get { return (Group ?? "") + "\u0001" + Title; } }
    }

    // Ctrl+Q — Visual Studio style "search everything you can do": a floating card
    // under the toolbars with a search field and a keyboard-driven result list.
    //   type        filter (all words must match; wrong layout — "rbcnm" — also works; with no
    //               good match the nearest commands are offered under "Did you mean")
    //   ↑ ↓ PgUp/Dn move, Enter run, Esc / click outside close
    // The matching is in CommandMatcher; the commands come from MainForm.BuildCommands.
    public sealed class CommandPalette : Form
    {
        // A result row: a command, or a caption ("Did you mean:") between groups of commands.
        private sealed class Row
        {
            public PaletteCommand Cmd;
            public string Header;
            public int Height { get { return Cmd == null ? HeaderH : RowH; } }
        }

        private const int CardWidth = 580, SearchH = 52, RowH = 46, HeaderH = 30, MaxRows = 8, FooterH = 30;

        // Most recently run commands float to the top of the empty query (per session).
        private static readonly List<string> Recent = new List<string>();

        private readonly List<PaletteCommand> _all;
        private readonly List<MatchItem> _items;
        private List<Row> _rows = new List<Row>();
        private readonly TextBox _search;
        private readonly Label _placeholder;
        private readonly ResultList _list;
        private int _selected, _top;
        private bool _closing;

        // What the matcher searches in a command: its title, other names, and (with less weight)
        // group, hint and keys. Index i of the result belongs to commands[i].
        public static List<MatchItem> MakeItems(IList<PaletteCommand> commands)
        {
            return commands.Select(c => new MatchItem(c.Title, c.Aliases,
                (c.Group ?? "") + " " + (c.Hint ?? "") + " " + (c.Shortcut ?? ""))).ToList();
        }

        public static void Open(Form owner, IList<PaletteCommand> commands)
        {
            var p = new CommandPalette(commands);
            Point origin = owner.PointToScreen(Point.Empty);
            int x = origin.X + (owner.ClientSize.Width - CardWidth) / 2;
            int y = origin.Y + Math.Min(110, owner.ClientSize.Height / 6);
            p.Location = new Point(x, y);
            p.Show(owner);
            p.Activate();
            p._search.Focus();
        }

        private CommandPalette(IList<PaletteCommand> commands)
        {
            _all = commands.Where(c => c != null && c.Run != null && !string.IsNullOrEmpty(c.Title)).ToList();
            _items = MakeItems(_all);
            // Recent commands rank a little higher, unavailable ones a little lower.
            for (int i = 0; i < _all.Count; i++)
                _items[i].Boost = (Recent.Contains(_all[i].Key) ? 3 : 0) + (_all[i].Enabled ? 0 : -8);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            AutoScaleMode   = AutoScaleMode.None;
            KeyPreview      = true;
            BackColor       = Theme.Card;
            DoubleBuffered  = true;
            Font            = Theme.Font(9.25f);

            _search = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor   = Theme.Card,
                ForeColor   = Theme.Text,
                Font        = Theme.Font(11.5f),
            };
            _search.TextChanged += (s, e) => { _placeholder.Visible = _search.TextLength == 0; Filter(); };
            _search.KeyDown     += Search_KeyDown;
            Controls.Add(_search);

            // A child label rather than a cue banner: works the same on every platform.
            _placeholder = new Label
            {
                Text      = Loc.T("Поиск команд, инструментов и настроек"),
                AutoSize  = true,
                Font      = _search.Font,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Card,
                Location  = new Point(0, 0),
                Cursor    = Cursors.IBeam,
            };
            _placeholder.MouseDown += (s, e) => _search.Focus();
            _search.Controls.Add(_placeholder);

            _list = new ResultList(this) { Location = new Point(1, SearchH + 1) };
            Controls.Add(_list);

            Filter();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int CS_DROPSHADOW    = 0x00020000;
                var cp = base.CreateParams;
                cp.ExStyle    |= WS_EX_TOOLWINDOW;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            CloseOnce();
        }

        // Hand focus back to the editor, so its shortcuts (Ctrl+Q again) work at once.
        // Done while the palette still exists: once its window is destroyed some
        // platforms (Mono/X11) leave keyboard focus pointing at nothing.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || Owner == null || Owner.IsDisposed) return;
            Owner.Activate();
            var focus = Owner.ActiveControl;
            if (focus != null && focus.CanFocus) focus.Focus();
            else Owner.Focus();
        }

        private void CloseOnce()
        {
            if (_closing) return;
            _closing = true;
            BeginInvoke(new Action(Close));
        }

        // ── Filtering ─────────────────────────────────────────────────
        // The matching itself lives in CommandMatcher (pure, unit-tested); this only lays the
        // result out: hits, then — when nothing matched well — the "did you mean" candidates.
        private void Filter()
        {
            string q = _search.Text.Trim();
            _rows = BuildRows(q);
            _selected = FirstSelectable();
            _top = 0;
            LayoutCard();
            _list.Invalidate();
        }

        private List<Row> BuildRows(string q)
        {
            var rows = new List<Row>();
            if (q.Length == 0)
            {
                var recent = Recent.Select(k => _all.FirstOrDefault(c => c.Key == k)).Where(c => c != null).ToList();
                foreach (var c in recent.Concat(_all.Where(c => !recent.Contains(c)))) rows.Add(new Row { Cmd = c });
                return rows;
            }

            var res = CommandMatcher.Search(q, _items);
            if (res.LayoutSwapped && res.Hits.Count > 0)
                rows.Add(new Row { Header = string.Format(Loc.T("Другая раскладка: показано для «{0}»"), res.Effective) });
            foreach (int i in res.Hits) rows.Add(new Row { Cmd = _all[i] });
            if (res.DidYouMean)
            {
                rows.Add(new Row { Header = res.LayoutSwapped
                    ? string.Format(Loc.T("Возможно, вы имели в виду (раскладка: «{0}»):"), res.Effective)
                    : Loc.T("Возможно, вы имели в виду:") });
                foreach (int i in res.Suggestions) rows.Add(new Row { Cmd = _all[i] });
            }
            return rows;
        }

        private int FirstSelectable()
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Cmd != null) return i;
            return -1;
        }

        private int CommandCount { get { return _rows.Count(r => r.Cmd != null); } }

        // ── Layout: the card grows with the result count ──────────────
        private void LayoutCard()
        {
            int listH = ListHeight + 8;
            Size = new Size(CardWidth, 1 + SearchH + 1 + listH + FooterH + 1);
            int th = _search.PreferredHeight;
            _search.SetBounds(48, (SearchH - th) / 2 + 1, CardWidth - 64, th);
            _list.Size = new Size(CardWidth - 2, listH);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            IconFactory.Draw(g, UiIcon.Zoom, Theme.TextMuted, new RectangleF(18, (SearchH - 18) / 2f + 1, 18, 18));

            using (var soft = new Pen(Theme.BorderSoft))
            {
                g.DrawLine(soft, 1, SearchH, Width - 2, SearchH);
                g.DrawLine(soft, 1, Height - FooterH - 1, Width - 2, Height - FooterH - 1);
            }
            using (var border = new Pen(Theme.Border))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            // Footer: the three keys that drive the palette.
            int x = 14;
            int y = Height - FooterH;
            foreach (var hint in new[] { new[] { "↑↓", Loc.T("выбрать") }, new[] { "Enter", Loc.T("выполнить") }, new[] { "Esc", Loc.T("закрыть") } })
                x = DrawKeyHint(g, hint[0], hint[1], x, y, FooterH) + 16;
            string count = CommandCount + Loc.T(" из ") + _all.Count;
            using (var af = new Font(Font.FontFamily, 8.25f))
                TextKit.Draw(g, count, af, Theme.TextMuted, new Rectangle(14, y, Width - 28, FooterH), HAlign.Right);
        }

        private int DrawKeyHint(Graphics g, string key, string action, int x, int y, int h)
        {
            var cap = new Rectangle(x, y + (h - 18) / 2, 0, 18);
            using (var kf = new Font(Font.FontFamily, 7.75f, FontStyle.Bold))
            {
                cap.Width = KeyCap.WidthFor(key, kf, 18);
                KeyCap.Draw(g, key, kf, cap);
            }
            x = cap.Right + 6;
            using (var af = new Font(Font.FontFamily, 8.25f))
            {
                int w = TextKit.InkWidth(action, af);
                TextKit.Draw(g, action, af, Theme.TextMuted, new Rectangle(x, cap.Y, w, cap.Height));
                return x + w;
            }
        }

        // ── Keyboard ──────────────────────────────────────────────────
        private void Search_KeyDown(object sender, KeyEventArgs e)
        {
            int page = Math.Max(1, VisibleCount - 1);
            switch (e.KeyCode)
            {
                case Keys.Down:     MoveSelection(+1);    break;
                case Keys.Up:       MoveSelection(-1);    break;
                case Keys.PageDown: MoveSelection(+page); break;
                case Keys.PageUp:   MoveSelection(-page); break;
                case Keys.Enter:    RunSelected(); break;
                case Keys.Escape:   CloseOnce(); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        // The list shows at most MaxRows command rows' worth of height; captions are shorter.
        private int ListHeight
        {
            get
            {
                int h = 0;
                foreach (var r in _rows)
                {
                    h += r.Height;
                    if (h >= MaxRows * RowH) return MaxRows * RowH;
                }
                return Math.Max(h, RowH);
            }
        }

        private int HeightFrom(int from, int to)
        {
            int h = 0;
            for (int i = from; i <= to && i < _rows.Count; i++) h += _rows[i].Height;
            return h;
        }

        // How many rows fit from the first visible one.
        private int VisibleCount
        {
            get
            {
                int n = 0, h = 0, max = ListHeight;
                for (int i = _top; i < _rows.Count; i++)
                {
                    h += _rows[i].Height;
                    if (h > max) break;
                    n++;
                }
                return Math.Max(1, n);
            }
        }

        // Moves over commands only: captions are never selected.
        private void MoveSelection(int delta)
        {
            if (_selected < 0) return;
            int step = delta < 0 ? -1 : 1, left = Math.Abs(delta), at = _selected;
            while (left > 0)
            {
                int j = at + step;
                while (j >= 0 && j < _rows.Count && _rows[j].Cmd == null) j += step;
                if (j < 0 || j >= _rows.Count) break;
                at = j;
                left--;
            }
            SelectRow(at);
        }

        private void SelectRow(int index)
        {
            _selected = index;
            if (_selected < _top)
            {
                _top = _selected;
                while (_top > 0 && _rows[_top - 1].Cmd == null) _top--;   // keep its caption in view
            }
            while (_top < _selected && HeightFrom(_top, _selected) > ListHeight) _top++;
            _list.Invalidate();
        }

        private void ScrollRows(int rows)
        {
            int maxTop = 0;
            while (maxTop < _rows.Count - 1 && HeightFrom(maxTop, _rows.Count - 1) > ListHeight) maxTop++;
            _top = Math.Max(0, Math.Min(maxTop, _top + rows));
            _list.Invalidate();
        }

        private void RunSelected()
        {
            if (_selected < 0 || _selected >= _rows.Count) return;
            var cmd = _rows[_selected].Cmd;
            if (cmd == null || !cmd.Enabled) return;
            Recent.Remove(cmd.Key);
            Recent.Insert(0, cmd.Key);
            if (Recent.Count > 6) Recent.RemoveAt(Recent.Count - 1);

            var owner = Owner;
            _closing = true;
            Close();
            // Run after the palette is gone, so dialogs the command opens get focus.
            if (owner != null && owner.IsHandleCreated) owner.BeginInvoke(cmd.Run);
            else cmd.Run();
        }

        // ── Result list (owner-drawn, scrolls by rows) ────────────────
        private sealed class ResultList : Control
        {
            private readonly CommandPalette _p;

            public ResultList(CommandPalette p)
            {
                _p = p;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                Cursor = Cursors.Hand;
            }

            // The selectable command under y, or -1.
            private int RowAt(int y)
            {
                int top = 4;
                for (int i = _p._top; i < _p._rows.Count; i++)
                {
                    int h = _p._rows[i].Height;
                    if (y >= top && y < top + h) return _p._rows[i].Cmd != null ? i : -1;
                    top += h;
                }
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int i = RowAt(e.Y);
                if (i >= 0 && i != _p._selected) { _p._selected = i; Invalidate(); }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                int i = RowAt(e.Y);
                if (e.Button == MouseButtons.Left && i >= 0) { _p._selected = i; _p.RunSelected(); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                _p.ScrollRows(e.Delta > 0 ? -2 : 2);
                var he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Card);
                var rows = _p._rows;
                if (rows.Count == 0)
                {
                    TextKit.Draw(g, Loc.T("Ничего не найдено"), Font, Theme.Text,
                        new Rectangle(0, 6, Width, 20), HAlign.Center);
                    using (var sub = new Font(Font.FontFamily, 8.25f))
                        TextKit.Draw(g, Loc.T("Поиск понимает опечатки и английские названия"), sub,
                            Theme.TextMuted, new Rectangle(0, 28, Width, 16), HAlign.Center);
                    return;
                }

                int visible = _p.VisibleCount;
                using (var titleFont = new Font(Font.FontFamily, 9.5f))
                using (var subFont = new Font(Font.FontFamily, 8f))
                using (var capFont = new Font(Font.FontFamily, 7.75f, FontStyle.Bold))
                using (var headFont = new Font(Font.FontFamily, 8.25f, FontStyle.Bold))
                {
                    int y = 4;
                    for (int n = 0; n < visible && _p._top + n < rows.Count; n++)
                    {
                        int i = _p._top + n;
                        var r = rows[i];
                        if (r.Cmd == null)
                        {
                            TextKit.Draw(g, r.Header, headFont, Theme.TextMuted,
                                new Rectangle(18, y + 6, Width - 36, 20), HAlign.Left, ellipsis: true);
                            y += r.Height;
                            continue;
                        }
                        var c = r.Cmd;
                        var row = new Rectangle(6, y, Width - 12, RowH);
                        y += RowH;
                        bool sel = i == _p._selected;
                        if (sel)
                            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, row);

                        Color ink = !c.Enabled ? Theme.TextDisabled : sel ? Theme.Accent : Theme.Text;
                        if (c.Icon != UiIcon.None)
                            IconFactory.Draw(g, c.Icon, !c.Enabled ? Theme.TextDisabled : sel ? Theme.Accent : Theme.TextSoft,
                                new RectangleF(row.X + 12, row.Y + (RowH - 18) / 2f, 18, 18));

                        // Shortcut caps on the right, then an optional check mark.
                        int right = row.Right - 12;
                        if (!string.IsNullOrEmpty(c.Shortcut))
                            right = DrawCaps(g, c.Shortcut, capFont, right, row) - 10;
                        if (c.Checked)
                        {
                            IconFactory.Draw(g, UiIcon.Check, Theme.Accent, new RectangleF(right - 16, row.Y + (RowH - 16) / 2f, 16, 16));
                            right -= 26;
                        }

                        // Two lines centred as a block on the icon's centre line.
                        int textX = row.X + 44;
                        string sub = string.IsNullOrEmpty(c.Hint) ? c.Group : c.Group + "  ·  " + c.Hint;
                        int mid = row.Y + RowH / 2;
                        TextKit.Draw(g, c.Title, titleFont, ink,
                            new Rectangle(textX, mid - 16, right - textX, 14), HAlign.Left, ellipsis: true);
                        TextKit.Draw(g, sub ?? "", subFont, Theme.TextMuted,
                            new Rectangle(textX, mid + 4, right - textX, 12), HAlign.Left, ellipsis: true);
                    }
                }

                // Thin position indicator when the list scrolls.
                if (_p._top > 0 || _p._top + visible < rows.Count)
                {
                    int trackH = Height - 8;
                    int thumbH = Math.Max(24, trackH * visible / rows.Count);
                    int thumbY = 4 + (trackH - thumbH) * _p._top / Math.Max(1, rows.Count - visible);
                    using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, Width - 4, thumbY, 2, thumbH);
                }
            }

            private static int DrawCaps(Graphics g, string shortcut, Font font, int right, Rectangle row)
            {
                string[] keys = shortcut.Split('+');
                int x = right;
                for (int k = keys.Length - 1; k >= 0; k--)
                {
                    string key = keys[k].Trim();
                    if (key.Length == 0) key = "+";
                    int w = KeyCap.WidthFor(key, font, 20);
                    var cap = new Rectangle(x - w, row.Y + (row.Height - 20) / 2, w, 20);
                    KeyCap.Draw(g, key, font, cap);
                    x = cap.Left - 4;
                }
                return x;
            }
        }
    }
}
