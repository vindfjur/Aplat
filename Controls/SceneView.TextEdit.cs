using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Controls
{
    // Typing straight onto the canvas — the Figma / Word model, no dialog.
    //
    //   Text tool, click on the sheet   → a text box with a blinking caret; type.
    //   Click inside live text          → caret there (drag selects, double-click
    //                                     selects a word; Shift+click extends).
    //   Double-click / Enter on text    → back into editing.
    //   Esc, Ctrl+Enter, click outside  → editing ends; an empty box disappears.
    //
    // While editing, every key belongs to the text: letters never switch tools,
    // Delete never deletes objects, arrows never nudge. Supported: arrows (Ctrl —
    // by word), Home / End (Ctrl — whole text), Shift+navigation selects,
    // Backspace / Delete (Ctrl — by word), Enter for a new line, Ctrl+A / C / X / V,
    // Ctrl+Z / Y inside the typing session, Ctrl+B / I / U, Ctrl+L / E / R.
    //
    // Formatting applies to the whole text element (font, size, style, alignment;
    // colour and opacity come from the brush), like a Figma text layer.
    public partial class SceneView
    {
        private bool  _textEditing;
        private int   _caret, _anchor;          // selection = [min, max) of the two
        private float _caretX = -1f;            // remembered x for ↑ / ↓ (layout px)
        private bool  _caretOn = true;
        private bool  _textSelecting;           // mouse drag selection in progress
        private bool  _liveTextJustEnded;       // this press finished a text: don't start another
        private Timer _caretTimer;

        private struct TextSnap { public string Text; public int Caret, Anchor; }
        private readonly List<TextSnap> _textUndo = new List<TextSnap>();
        private readonly List<TextSnap> _textRedo = new List<TextSnap>();
        private bool     _typingRun;
        private DateTime _lastTyped;

        // Defaults for the next text (and the live text's format while it exists).
        public string          TextFontFamily { get; private set; } = "Segoe UI";
        public float           TextSizePt     { get; private set; } = 24f;
        public FontStyle       TextFontStyle  { get; private set; } = FontStyle.Regular;
        public StringAlignment TextAlignment  { get; private set; } = StringAlignment.Near;

        public bool IsEditingText { get { return _textEditing && _live != null && _live.IsText; } }

        // Keys reach the text only while the viewport itself has focus (typing into
        // a panel field must keep working while a text is being edited).
        public bool OwnsFocus(Control c)
        {
            return c != null && (c == _gl || c == this || Contains(c));
        }

        private float PtToPx(float pt, Bitmap layer) { return pt * 96f / 72f * (_surf != null ? 1f : Material.PaintScale(layer)); }
        private float PxToPt(float px, Bitmap layer) { return px / (_surf != null ? 1f : Material.PaintScale(layer)) * 72f / 96f; }

        // Host (the "TEXT" panel section): sets the format for new text and, when a
        // text is live, restyles it at once.
        public void SetTextFormat(string family, float sizePt, FontStyle style, StringAlignment align)
        {
            TextFontFamily = string.IsNullOrEmpty(family) ? TextFontFamily : family;
            TextSizePt     = Math.Max(2f, Math.Min(1000f, sizePt));
            TextFontStyle  = style & (FontStyle.Bold | FontStyle.Italic | FontStyle.Underline);
            TextAlignment  = align;
            if (_live != null && _live.IsText)
            {
                var layer = PaintLayerFor(_liveCanvas);
                EditLive(i =>
                {
                    i.FontFamily = TextFontFamily;
                    i.FontPx     = PtToPx(TextSizePt, layer);
                    i.FontStyle  = TextFontStyle;
                    i.Align      = TextAlignment;
                });
            }
            ResumeTyping();
        }

        // Panel clicks move focus away; bring it back so typing continues.
        public void ResumeTyping()
        {
            if (!IsEditingText || _gl == null) return;
            BeginInvoke(new Action(() => { if (IsEditingText) _gl.Focus(); }));
            ShowCaretNow();
        }

        // The live text's format, for the panel to show.
        public bool TryGetLiveTextFormat(out string family, out float sizePt, out FontStyle style, out StringAlignment align)
        {
            family = TextFontFamily; sizePt = TextSizePt; style = TextFontStyle; align = TextAlignment;
            if (_live == null || !_live.IsText || _liveCanvas == null) return false;
            family = _live.FontFamily;
            sizePt = (float)Math.Round(PxToPt(_live.FontPx, PaintLayerFor(_liveCanvas)), 1);
            style  = _live.FontStyle;
            align  = _live.Align;
            // The live text's format becomes the default for the next one.
            TextFontFamily = family; TextSizePt = sizePt; TextFontStyle = style; TextAlignment = align;
            return true;
        }

        // ── Session ───────────────────────────────────────────────────
        // Text tool click on the sheet: a new, empty text with the caret in it.
        private void BeginTextAt(SceneObject canvas, Point screen)
        {
            CommitLive();
            _liveCanvas = canvas;                               // ScreenToLayer needs it
            if (!ScreenToLayer(screen, out PointF p)) { _liveCanvas = null; return; }
            TransformStarted?.Invoke(this, EventArgs.Empty);   // one undo step per text

            var layer = PaintLayerFor(canvas);
            var item = new LiveItem
            {
                Kind       = LiveKind.Text,
                Text       = "",
                FontFamily = TextFontFamily,
                FontStyle  = TextFontStyle,
                FontPx     = PtToPx(TextSizePt, layer),
                Align      = TextAlignment,
                Color      = LiveColor(),
            };
            // The click is where the first line sits: its left / centre / right edge
            // per alignment, vertically centred on the line.
            SizeF sz = item.TextLayoutSize;
            float ax = item.Align == StringAlignment.Center ? 0.5f : item.Align == StringAlignment.Far ? 1f : 0f;
            item.P1 = new PointF(p.X - ax * sz.Width, p.Y - item.LineHeight / 2f);
            item.P2 = new PointF(item.P1.X + sz.Width, item.P1.Y + sz.Height);
            StartLive(canvas, item, new Bitmap(layer), null);
            EnterTextEdit(0, 0);
        }

        private void EnterTextEdit(int caret, int anchor)
        {
            if (_live == null || !_live.IsText) return;
            int len = (_live.Text ?? "").Length;
            _textEditing = true;
            _caret  = Math.Max(0, Math.Min(len, caret));
            _anchor = Math.Max(0, Math.Min(len, anchor));
            _caretX = -1f;
            _textUndo.Clear(); _textRedo.Clear(); _typingRun = false;
            if (_caretTimer == null)
            {
                _caretTimer = new Timer { Interval = 530 };
                _caretTimer.Tick += (s, e) => { _caretOn = !_caretOn; Redraw(); };
            }
            ShowCaretNow();
            _gl?.Focus();
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
        }

        // Esc / Ctrl+Enter: stop typing, keep the text live (frame + handles).
        // A box left empty is removed, with its undo step.
        public void EndTextEdit()
        {
            if (!_textEditing) return;
            StopTextEditState();
            if (_live != null && _live.IsText && string.IsNullOrEmpty(_live.Text)) { CancelLive(); return; }
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
        }

        private void StopTextEditState()
        {
            _textEditing   = false;
            _textSelecting = false;
            _caretTimer?.Stop();
            _textUndo.Clear(); _textRedo.Clear();
        }

        private void ShowCaretNow()
        {
            _caretOn = true;
            if (_caretTimer != null && _textEditing) { _caretTimer.Stop(); _caretTimer.Start(); }
            Redraw();
        }

        // ── Editing primitives ────────────────────────────────────────
        private string LiveText { get { return _live.Text ?? ""; } }
        private int SelStart { get { return Math.Min(_caret, _anchor); } }
        private int SelEnd   { get { return Math.Max(_caret, _anchor); } }
        private bool HasSelection { get { return _caret != _anchor; } }

        private void ReplaceSelection(string insert, bool typing)
        {
            PushTextUndo(typing);
            string t = LiveText;
            int a = SelStart, b = SelEnd;
            SetLiveText(t.Substring(0, a) + insert + t.Substring(b), a + insert.Length);
        }

        private void DeleteRange(int a, int b)
        {
            if (a == b) return;
            PushTextUndo(false);
            string t = LiveText;
            SetLiveText(t.Substring(0, a) + t.Substring(b), a);
        }

        private void SetLiveText(string text, int caret)
        {
            _live.Text = text;
            _caret = _anchor = Math.Max(0, Math.Min(text.Length, caret));
            _caretX = -1f;
            _live.Normalize();
            RenderLive();
            if (_scene != null) _scene.IsModified = true;
            ShowCaretNow();
        }

        // Consecutive typed characters coalesce into one undo step (until a pause,
        // a jump or a non-typing edit), like every text editor.
        private void PushTextUndo(bool typing)
        {
            DateTime now = DateTime.UtcNow;
            bool coalesce = typing && _typingRun && (now - _lastTyped).TotalMilliseconds < 1500;
            _lastTyped = now;
            _typingRun = typing;
            if (coalesce) return;
            _textUndo.Add(new TextSnap { Text = LiveText, Caret = _caret, Anchor = _anchor });
            if (_textUndo.Count > 200) _textUndo.RemoveAt(0);
            _textRedo.Clear();
        }

        // Undo / Redo from the menu or toolbar while typing: steps through the text's
        // own history (as in Word / Figma) and gives the keyboard back to the canvas.
        public bool TextHistoryStep(bool redo)
        {
            if (!_textEditing) return false;
            TextUndo(redo);
            ResumeTyping();
            return true;
        }

        private void TextUndo(bool redo)
        {
            var from = redo ? _textRedo : _textUndo;
            var to   = redo ? _textUndo : _textRedo;
            if (from.Count == 0) return;
            to.Add(new TextSnap { Text = LiveText, Caret = _caret, Anchor = _anchor });
            var s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            _typingRun = false;
            SetLiveText(s.Text, s.Caret);
            _anchor = s.Anchor;
        }

        private void MoveCaret(int index, bool extend)
        {
            _caret = Math.Max(0, Math.Min(LiveText.Length, index));
            if (!extend) _anchor = _caret;
            _typingRun = false;
            ShowCaretNow();
        }

        private static int CharClass(char c)
        {
            if (char.IsWhiteSpace(c)) return 0;
            return char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;
        }

        private int PrevWord(int i)
        {
            string t = LiveText;
            while (i > 0 && CharClass(t[i - 1]) == 0) i--;
            if (i == 0) return 0;
            int cls = CharClass(t[i - 1]);
            while (i > 0 && CharClass(t[i - 1]) == cls) i--;
            return i;
        }

        private int NextWord(int i)
        {
            string t = LiveText;
            if (i >= t.Length) return t.Length;
            int cls = CharClass(t[i]);
            while (i < t.Length && CharClass(t[i]) == cls && cls != 0) i++;
            while (i < t.Length && CharClass(t[i]) == 0) i++;
            return i;
        }

        private void SelectWordAt(int i)
        {
            string t = LiveText;
            if (t.Length == 0) { MoveCaret(0, false); return; }
            int k = Math.Max(0, Math.Min(t.Length - 1, i > 0 && (i == t.Length || CharClass(t[i]) == 0) ? i - 1 : i));
            int cls = CharClass(t[k]);
            int a = k, b = k + 1;
            while (a > 0 && CharClass(t[a - 1]) == cls) a--;
            while (b < t.Length && CharClass(t[b]) == cls) b++;
            _anchor = a; _caret = b;
            ShowCaretNow();
        }

        private void ToggleStyle(FontStyle flag)
        {
            SetTextFormat(TextFontFamily, TextSizePt, TextFontStyle ^ flag, TextAlignment);
        }

        // ── Keyboard ──────────────────────────────────────────────────
        // Non-character keys while editing. True = consumed. Plain characters return
        // false so they arrive as KeyPress (which handles layouts, Shift, AltGr, IME).
        public bool TextKeyDown(Keys keyData)
        {
            if (!IsEditingText) return false;
            Keys code = keyData & Keys.KeyCode;
            bool ctrl  = (keyData & Keys.Control) != 0;
            bool shift = (keyData & Keys.Shift) != 0;
            bool alt   = (keyData & Keys.Alt) != 0;
            if (alt) return false;
            string t = LiveText;

            switch (code)
            {
                case Keys.Escape: EndTextEdit(); return true;
                case Keys.Enter:
                    if (ctrl) { EndTextEdit(); return true; }
                    ReplaceSelection("\n", false);
                    return true;
                case Keys.Tab:
                    ReplaceSelection("    ", false);
                    return true;
                case Keys.Back:
                    if (HasSelection) DeleteRange(SelStart, SelEnd);
                    else if (_caret > 0) DeleteRange(ctrl ? PrevWord(_caret) : _caret - 1, _caret);
                    return true;
                case Keys.Delete:
                    if (HasSelection) DeleteRange(SelStart, SelEnd);
                    else if (_caret < t.Length) DeleteRange(_caret, ctrl ? NextWord(_caret) : _caret + 1);
                    return true;
                case Keys.Left:
                    if (HasSelection && !shift) MoveCaret(SelStart, false);
                    else MoveCaret(ctrl ? PrevWord(_caret) : _caret - 1, shift);
                    return true;
                case Keys.Right:
                    if (HasSelection && !shift) MoveCaret(SelEnd, false);
                    else MoveCaret(ctrl ? NextWord(_caret) : _caret + 1, shift);
                    return true;
                case Keys.Up:
                case Keys.Down:
                {
                    int line = _live.LineOf(_caret);
                    if (_caretX < 0) _caretX = _live.CaretPoint(_caret).X;
                    float keepX = _caretX;
                    int target = line + (code == Keys.Up ? -1 : 1);
                    int idx = target < 0 ? 0 : target >= _live.Lines.Length ? t.Length : _live.IndexAtX(target, keepX);
                    MoveCaret(idx, shift);
                    _caretX = keepX;
                    return true;
                }
                case Keys.Home:
                {
                    var ln = _live.Lines[_live.LineOf(_caret)];
                    MoveCaret(ctrl ? 0 : ln.Start, shift);
                    return true;
                }
                case Keys.End:
                {
                    var ln = _live.Lines[_live.LineOf(_caret)];
                    MoveCaret(ctrl ? t.Length : ln.Start + ln.S.Length, shift);
                    return true;
                }
            }

            if (!ctrl) return false;   // plain characters → KeyPress
            switch (code)
            {
                case Keys.A: _anchor = 0; _caret = t.Length; ShowCaretNow(); return true;
                case Keys.C:
                case Keys.X:
                    if (HasSelection)
                    {
                        try { Clipboard.SetText(t.Substring(SelStart, SelEnd - SelStart)); } catch { }
                        if (code == Keys.X) DeleteRange(SelStart, SelEnd);
                    }
                    return true;
                case Keys.V:
                {
                    string clip = null;
                    try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
                    if (!string.IsNullOrEmpty(clip)) ReplaceSelection(CleanPaste(clip), false);
                    return true;
                }
                case Keys.Z: TextUndo(shift); return true;
                case Keys.Y: TextUndo(true);  return true;
                case Keys.B: ToggleStyle(FontStyle.Bold);      return true;
                case Keys.I: ToggleStyle(FontStyle.Italic);    return true;
                case Keys.U: ToggleStyle(FontStyle.Underline); return true;
                case Keys.L: SetTextFormat(TextFontFamily, TextSizePt, TextFontStyle, StringAlignment.Near);   return true;
                case Keys.E: SetTextFormat(TextFontFamily, TextSizePt, TextFontStyle, StringAlignment.Center); return true;
                case Keys.R: SetTextFormat(TextFontFamily, TextSizePt, TextFontStyle, StringAlignment.Far);    return true;
            }
            return false;
        }

        private static string CleanPaste(string s)
        {
            var sb = new StringBuilder(s.Length);
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (char c in s)
            {
                if (c == '\n') sb.Append(c);
                else if (c == '\t') sb.Append("    ");
                else if (!char.IsControl(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private void Gl_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!IsEditingText) return;
            char c = e.KeyChar;
            e.Handled = true;
            if (char.IsControl(c)) return;      // Enter, Backspace, Ctrl+letter: handled as keys
            ReplaceSelection(c.ToString(), true);
            if (char.IsWhiteSpace(c)) _typingRun = false;   // a word + its space = one undo step (Word)
        }

        // ── Mouse ─────────────────────────────────────────────────────
        // Called by LiveMouseDown for a press inside live text. True = consumed.
        private bool TextMouseDown(MouseEventArgs e, PointF layerPt)
        {
            bool textTool = _tool == ToolMode.Paint && PaintKind == BrushKind.Text2D;
            if (!_textEditing && !textTool && e.Clicks < 2) return false;   // move / resize as usual

            int idx = _live.HitTest(_live.LayerToLayout(layerPt));
            if (!_textEditing)
            {
                EnterTextEdit(idx, idx);
                if (e.Clicks >= 2) SelectWordAt(idx);
                else _textSelecting = true;
                return true;
            }
            if (e.Clicks >= 2) { SelectWordAt(idx); return true; }
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            MoveCaret(idx, shift);
            _textSelecting = true;
            return true;
        }

        private bool TextMouseMove(MouseEventArgs e)
        {
            if (!_textSelecting || !IsEditingText) return false;
            if ((e.Button & MouseButtons.Left) == 0) { _textSelecting = false; return false; }
            if (ScreenToLayer(e.Location, out PointF q))
            {
                _caret = _live.HitTest(_live.LayerToLayout(q));
                ShowCaretNow();
            }
            return true;
        }

        private bool TextMouseUp(MouseEventArgs e)
        {
            if (!_textSelecting) return false;
            _textSelecting = false;
            return true;
        }

        // ── Overlay: frame, selection, caret ──────────────────────────
        private PointF LayoutToScreen(PointF lp) { return LayerToScreen(_live.LayoutToLayer(lp)); }

        private void DrawTextEditGL(float ar, float ag, float ab)
        {
            SizeF sz = _live.TextLayoutSize;
            float lh = _live.LineHeight;

            // Selection: one quad per line it covers (a line break shows as a sliver).
            if (HasSelection)
            {
                GL.Color4(ar, ag, ab, 0.30f);
                GL.Begin(PrimitiveType.Quads);
                foreach (var ln in _live.Lines)
                {
                    int lineEnd = ln.Start + ln.S.Length;
                    int a = Math.Max(SelStart, ln.Start), b = Math.Min(SelEnd, lineEnd);
                    bool brk = SelEnd > lineEnd && SelStart <= lineEnd;   // selection runs past this line
                    if (a > b || (a == b && !brk)) continue;
                    float x0 = ln.X + ln.Pos[a - ln.Start];
                    float x1 = ln.X + ln.Pos[b - ln.Start] + (brk ? _live.FontPx * 0.25f : 0f);
                    foreach (var p in new[] { new PointF(x0, ln.Top), new PointF(x1, ln.Top),
                                              new PointF(x1, ln.Top + lh), new PointF(x0, ln.Top + lh) })
                    {
                        PointF s = LayoutToScreen(p);
                        GL.Vertex2(s.X, s.Y);
                    }
                }
                GL.End();
            }

            // Caret: 2 px, accent (reads on white paper and over dark ink alike).
            if (_caretOn)
            {
                PointF top = _live.CaretPoint(_caret);
                PointF s0 = LayoutToScreen(top), s1 = LayoutToScreen(new PointF(top.X, top.Y + lh));
                GL.LineWidth(2f);
                GL.Color4(ar, ag, ab, 1f);
                GL.Begin(PrimitiveType.Lines); GL.Vertex2(s0.X, s0.Y); GL.Vertex2(s1.X, s1.Y); GL.End();
            }
        }
    }
}
