using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    // Live canvas elements: a shape, a text or a cut-out region
    // stays editable after it is drawn — handles resize it, the round handle
    // rotates it, dragging the body moves it, and its colour / stroke / fill follow
    // the panel. It is merged into the canvas pixels (committed) by Enter, a click
    // outside, another tool, or any other edit; Esc removes it. Ctrl+Z right after
    // a commit brings it back to the editable state.
    //
    // While live, the paint layer shows base + item (so saving, exporting and the
    // undo snapshots always see the picture as it looks); only the handles are an
    // overlay. Redraws touch just the area the item covered before and after.
    //
    // Item geometry is in "sheet" pixels: layer px horizontally, and vertically
    // scaled by K so a sheet pixel is square on the canvas (the layer is square but
    // the sheet is 4:3, so layer pixels are not). Circles stay round, rotation is
    // rigid, text keeps its proportions; K is applied only when painting the layer.
    public partial class SceneView
    {
        private LiveItem    _live;
        private SceneObject _liveCanvas;
        private Bitmap      _liveBase;      // the layer without the item
        private Bitmap      _liveCancel;    // what Esc restores when it differs from the base (cut-out)
        private Rectangle   _liveDrawn;     // layer area the item painted last time
        private Rectangle   _liveCutRect;   // a cut-out: the hole it left in the layer

        private enum LiveDrag { None, Create, Move, Handle, Rotate }
        private LiveDrag _liveDrag;
        private int      _liveHandle;
        private LiveItem _liveStart;        // item as it was when the drag began
        private float    _liveMargin;       // frame offset (layer px) when the drag began
        private PointF   _liveGrab;         // layer point where the drag began

        // Last committed item, kept so Ctrl+Z can reopen it for editing.
        private LiveItem    _reopenItem;
        private SceneObject _reopenCanvas;
        private Bitmap      _reopenBase, _reopenCancel;
        private Rectangle   _reopenCut;
        // Its entries in the canvas's object journal — taken out again if it is reopened.
        private List<InkOp> _reopenOps;

        private const float HandleHit   = 8f;    // px
        private const float RotateReach = 26f;   // px above the top edge

        public event EventHandler LiveChanged;             // appeared, edited or gone
        public event EventHandler LiveCancelled;           // removed by Esc: drop its undo step

        public bool     HasLiveItem { get { return _live != null; } }

        // Layer px per 1024-sheet px of the live element's canvas (font sizes, strokes).
        public float LiveLayerScale
        {
            get { return _surf != null ? 1f : _liveCanvas != null ? Material.PaintScale(PaintLayerFor(_liveCanvas)) : 1f; }
        }
        public LiveItem Live        { get { return _live; } }

        // Fill of new closed shapes (and of the live one, when changed).
        private ShapeFill _shapeFill = ShapeFill.Outline;
        public ShapeFill ShapeFillMode
        {
            get { return _shapeFill; }
            set
            {
                _shapeFill = value;
                if (_live != null && _live.IsClosedShape) EditLive(i => i.Fill = value);
            }
        }

        // ── Canvas mapping ────────────────────────────────────────────
        // Layer px ⇄ screen for the flat canvas sheet. Screen → layer uses the exact
        // plane homography of the four projected corners, so it also works past the
        // sheet's edge (handles can be dragged outside the canvas).
        // Sheet y = layer y × K (see the note at the top).
        private float LiveK
        {
            get
            {
                if (_surf != null) return 1f;
                if (_liveCanvas == null || !CanvasCorners(_liveCanvas, out Vec3 tl, out Vec3 tr, out Vec3 _, out Vec3 bl))
                    return 1f;
                var layer = PaintLayerFor(_liveCanvas);
                float w = (tr - tl).Length, h = (bl - tl).Length;
                if (w < 1e-6f || h < 1e-6f) return 1f;
                return (h / w) * ((float)layer.Width / layer.Height);
            }
        }

        // Item (sheet) rectangle → layer pixels.
        private Rectangle ItemAreaInLayer(LiveItem item)
        {
            var layer = PaintLayerFor(_liveCanvas);
            if (_surf != null) return new Rectangle(Point.Empty, layer.Size);
            float k = LiveK;
            Rectangle r = item.PaintBounds(layer.Width, (int)Math.Ceiling(layer.Height * k));
            if (r.IsEmpty) return r;
            int y0 = (int)Math.Floor(r.Top / k), y1 = (int)Math.Ceiling(r.Bottom / k);
            return Rectangle.Intersect(Rectangle.FromLTRB(r.Left, y0, r.Right, y1),
                                       new Rectangle(0, 0, layer.Width, layer.Height));
        }

        private bool CanvasCorners(SceneObject canvas, out Vec3 tl, out Vec3 tr, out Vec3 br, out Vec3 bl)
        {
            tl = tr = br = bl = Vec3.Zero;
            if (canvas == null || canvas.Mesh == null || canvas.Mesh.Vertices.Count < 4) return false;
            var xf = SceneGeometry.Xform.For(canvas);
            var v = canvas.Mesh.Vertices;
            bl = xf.Apply(v[0]); br = xf.Apply(v[1]); tr = xf.Apply(v[2]); tl = xf.Apply(v[3]);
            return true;
        }

        private PointF LayerToScreen(PointF p)
        {
            if (_surf != null) return p;   // a text on a shape lives in screen space
            var layer = PaintLayerFor(_liveCanvas);
            if (!CanvasCorners(_liveCanvas, out Vec3 tl, out Vec3 tr, out Vec3 _, out Vec3 bl)) return PointF.Empty;
            float u = p.X / layer.Width, v = p.Y / LiveK / layer.Height;
            return Proj(tl + (tr - tl) * u + (bl - tl) * v);
        }

        private bool ScreenToLayer(Point s, out PointF p)
        {
            p = PointF.Empty;
            if (_liveCanvas == null) return false;
            if (_surf != null) { p = new PointF(s.X, s.Y); return true; }
            if (!CanvasCorners(_liveCanvas, out Vec3 tl, out Vec3 tr, out Vec3 br, out Vec3 bl)) return false;
            var src = new[] { Proj(tl), Proj(tr), Proj(br), Proj(bl) };
            foreach (var q in src) if (float.IsNaN(q.X) || q.X < -9000) return false;
            var dst = new[] { new PointF(0, 0), new PointF(1, 0), new PointF(1, 1), new PointF(0, 1) };
            double[] h = Homography(src, dst);
            if (h == null) return false;
            double w = h[6] * s.X + h[7] * s.Y + 1.0;
            if (Math.Abs(w) < 1e-12) return false;
            double u = (h[0] * s.X + h[1] * s.Y + h[2]) / w;
            double v = (h[3] * s.X + h[4] * s.Y + h[5]) / w;
            var layer = PaintLayerFor(_liveCanvas);
            p = new PointF((float)(u * layer.Width), (float)(v * layer.Height * LiveK));
            return true;
        }

        // 3×3 homography (h8 = 1) mapping four source points onto four targets.
        private static double[] Homography(PointF[] s, PointF[] d)
        {
            var a = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = s[i].X, y = s[i].Y, u = d[i].X, v = d[i].Y;
                int r = i * 2;
                a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1; a[r, 6] = -x * u; a[r, 7] = -y * u; a[r, 8] = u;
                a[r + 1, 3] = x; a[r + 1, 4] = y; a[r + 1, 5] = 1; a[r + 1, 6] = -x * v; a[r + 1, 7] = -y * v; a[r + 1, 8] = v;
            }
            for (int c = 0; c < 8; c++)
            {
                int piv = c;
                for (int r = c + 1; r < 8; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-12) return null;
                if (piv != c) for (int k = 0; k < 9; k++) { double t = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = t; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    if (f == 0) continue;
                    for (int k = c; k < 9; k++) a[r, k] -= f * a[c, k];
                }
            }
            var h = new double[8];
            for (int i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
            return h;
        }

        // Screen px → layer px scale near a point (for screen-constant tolerances).
        private float LayerPerScreenPx(Point s)
        {
            if (!ScreenToLayer(s, out PointF a) || !ScreenToLayer(new Point(s.X + 10, s.Y), out PointF b)) return 1f;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Max(0.05f, (float)Math.Sqrt(dx * dx + dy * dy) / 10f);
        }

        // ── Session ───────────────────────────────────────────────────
        private int LivePenWidth(Bitmap layer)
        {
            return Math.Max(1, (int)Math.Round(BrushRadius * (_surf != null ? 1f : Material.PaintScale(layer))));
        }

        private Color LiveColor()
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, BrushOpacity * 255 / 100)), BrushColor);
        }

        private void StartLive(SceneObject canvas, LiveItem item, Bitmap baseLayer, Bitmap cancelLayer)
        {
            _liveCanvas = canvas;
            if (canvas.PrimitiveType == "Canvas") EndSurface();
            _live       = item;
            _liveBase   = baseLayer;
            _liveCancel = cancelLayer;
            _liveDrawn  = ItemAreaInLayer(item);
            _lastPaintMaterial = canvas.Material;
            RenderLive();
        }

        // Layer = base + item, redrawing only what the item covered before and now.
        private void RenderLive()
        {
            if (_live == null || _liveCanvas == null || _liveBase == null) return;
            if (_surf != null) { RenderSurfaceLive(); return; }
            var layer = PaintLayerFor(_liveCanvas);
            Rectangle now = ItemAreaInLayer(_live);
            Rectangle dirty = _liveDrawn.IsEmpty ? now : (now.IsEmpty ? _liveDrawn : Rectangle.Union(_liveDrawn, now));
            if (!dirty.IsEmpty)
            {
                using (var g = Graphics.FromImage(layer))
                {
                    g.SetClip(dirty);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(_liveBase, dirty, dirty, GraphicsUnit.Pixel);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.ScaleTransform(1f, 1f / LiveK);   // sheet px → layer px
                    _live.Render(g);
                }
                _liveCanvas.Material.MarkPaintDirty(dirty);
            }
            _liveDrawn = now;
            Redraw();
        }

        // Applies an edit to the live item (from the panel, the text dialog, the host).
        public void EditLive(Action<LiveItem> edit)
        {
            if (_live == null || edit == null) return;
            edit(_live);
            _live.Normalize();
            RenderLive();
            if (_scene != null) _scene.IsModified = true;
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }

        // Enter, a click outside, another tool: the item becomes canvas pixels.
        public void CommitLive()
        {
            if (_live == null) return;
            if (_textEditing)
            {
                StopTextEditState();
                // A text box nobody typed into leaves nothing behind.
                if (_live.IsText && string.IsNullOrEmpty(_live.Text)) { CancelLive(); return; }
            }
            RenderLive();
            ForgetReopen();
            _reopenOps    = RecordLiveInk(true);
            if (_live.IsGroup)
            {
                // Settled for good: Ctrl+Z goes back to the step taken at the lift instead.
                _reopenOps = null;
                _live.Dispose(); _liveBase?.Dispose(); _liveCancel?.Dispose();
            }
            else if (_surf != null)
            {
                // On a shape the element becomes plain paint: nothing to reopen.
                _live.Dispose(); _liveBase?.Dispose(); _liveCancel?.Dispose();
            }
            else
            {
                _reopenItem   = _live;
                _reopenCanvas = _liveCanvas;
                _reopenBase   = _liveBase;
                _reopenCancel = _liveCancel;
                _reopenCut    = _liveCutRect;
            }
            _live = null; _liveCanvas = null; _liveBase = null; _liveCancel = null;
            EndSurface();
            _liveCutRect = Rectangle.Empty;
            _liveDrag = LiveDrag.None;
            FlushPaintEncoding();
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
        }

        // The committed element joins the canvas's object journal: a cut-out first as
        // the hole it left, then as the picture itself.
        private List<InkOp> RecordLiveInk(bool final = false)
        {
            var ink = _liveCanvas.Material.Ink;
            if (ink == null) return null;
            var layer = PaintLayerFor(_liveCanvas);
            var ops = new List<InkOp>();
            if (_live.IsGroup)
            {
                // A lasso selection leaves as one group (the ops, and the transform they are
                // now drawn through) — or, if the frame was not touched, as the ops it picked up.
                if (final && _lifted != null && GroupUntouched(_live))
                {
                    ink.Unlift(_lifted);
                    _lifted = null;
                    return ops;
                }
                var copy = _live.Clone();
                float[] m;
                using (var gm = copy.GroupMatrix()) m = gm.Elements;
                var group = GroupOp.Create(copy.Ops, m, layer.Size);
                copy.Ops = null;
                copy.Dispose();
                ops.Add(group);
                ink.Add(group);
                if (final) DisposeLifted();
                if (ink.NeedsBake) ink.Bake(layer.Size);
                return ops;
            }
            if (_liveCancel != null && !_liveCutRect.IsEmpty) ops.Add(new ClearOp { Bounds = _liveCutRect });
            ops.Add(new ItemOp { Item = _live.Clone(), K = LiveK, Bounds = ItemAreaInLayer(_live) });
            foreach (var op in ops) ink.Add(op);
            if (ink.NeedsBake) ink.Bake(layer.Size);
            return ops;
        }

        // For writing the file while the element is still being edited — see
        // MainForm.WriteSceneFile: its journal entries exist only during the save.
        private List<InkOp> _savingOps;

        public void JournalLiveForSave()
        {
            if (_live == null || _liveCanvas == null || _savingOps != null) return;
            _savingOps = RecordLiveInk(false);
        }

        public void UnjournalLiveAfterSave()
        {
            if (_savingOps == null) return;
            var ink = _liveCanvas != null ? _liveCanvas.Material.Ink : null;
            if (ink != null) foreach (var op in _savingOps) ink.Discard(op);
            _savingOps = null;
        }

        // Esc / Delete: the item is removed (a cut-out goes back where it came from).
        public bool CancelLive()
        {
            if (_live == null) return false;
            StopTextEditState();
            var layer = PaintLayerFor(_liveCanvas);
            if (_live.IsGroup && _lifted != null)
            {
                var inkBack = _liveCanvas.Material.Ink;
                if (inkBack != null) inkBack.Unlift(_lifted);   // the picked-up ops go back where they were
                _lifted = null;
            }
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                if (_liveCancel != null)
                {
                    g.DrawImage(_liveCancel, 0, 0, layer.Width, layer.Height);
                    _liveCanvas.Material.MarkPaintDirty();
                }
                else if (!_liveDrawn.IsEmpty)
                {
                    g.DrawImage(_liveBase, _liveDrawn, _liveDrawn, GraphicsUnit.Pixel);
                    _liveCanvas.Material.MarkPaintDirty(_liveDrawn);
                }
            }
            DisposeLive();
            ForgetReopen();
            FlushPaintEncoding();
            LiveCancelled?.Invoke(this, EventArgs.Empty);
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
            return true;
        }

        // Drops the session without touching the layer (the scene is being replaced,
        // or undo is about to restore a snapshot).
        public void DiscardLive()
        {
            bool had = _live != null;
            DisposeLive();
            ForgetReopen();
            if (had) LiveChanged?.Invoke(this, EventArgs.Empty);
        }

        // Ctrl+Z right after a commit: the item is editable again (no history step).
        public bool ReopenLastLive()
        {
            if (_live != null || _reopenItem == null || _scene == null) return false;
            if (!_scene.Objects.Contains(_reopenCanvas)) { ForgetReopen(); return false; }
            var item = _reopenItem; var canvas = _reopenCanvas;
            var bas = _reopenBase;  var cancel = _reopenCancel;
            var cut = _reopenCut;   var ops = _reopenOps;
            _reopenItem = null; _reopenCanvas = null; _reopenBase = null; _reopenCancel = null;
            _reopenOps = null; _reopenCut = Rectangle.Empty;
            // Editable again: out of the journal until it is committed anew (the
            // layer's pixels stay as they are — the element draws itself over its base).
            var ink = canvas.Material.Ink;
            if (ink != null && ops != null) foreach (var op in ops) ink.Discard(op);
            _liveCutRect = cut;
            StartLive(canvas, item, bas, cancel);
            LiveChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // Enter on a live text (not being typed into): back into typing, caret at the end.
        public bool EditLiveText()
        {
            if (_live == null || !_live.IsText || _textEditing) return false;
            int n = (_live.Text ?? "").Length;
            EnterTextEdit(n, n);
            return true;
        }

        public void ForgetReopen()
        {
            _reopenItem?.Dispose();
            _reopenBase?.Dispose();
            _reopenCancel?.Dispose();
            _reopenItem = null; _reopenCanvas = null; _reopenBase = null; _reopenCancel = null;
            _reopenOps = null; _reopenCut = Rectangle.Empty;   // the journal entries stay: it is final
        }

        private void DisposeLive()
        {
            StopTextEditState();
            _live?.Dispose();       _live = null;
            _liveBase?.Dispose();   _liveBase = null;
            _liveCancel?.Dispose(); _liveCancel = null;
            _liveStart?.Dispose();  _liveStart = null;
            _liveCanvas = null;
            EndSurface();
            _liveDrawn  = Rectangle.Empty;
            _liveCutRect = Rectangle.Empty;
            _liveDrag   = LiveDrag.None;
            DisposeLifted();
        }

        // Arrow keys: nudge by one layer pixel (Shift — ten).
        public void NudgeLive(int dx, int dy, bool big)
        {
            if (_live == null) return;
            float k = big ? 10f : 1f;
            EditLive(i =>
            {
                i.P1 = new PointF(i.P1.X + dx * k, i.P1.Y + dy * k);
                i.P2 = new PointF(i.P2.X + dx * k, i.P2.Y + dy * k);
            });
        }

        // ── Creation ──────────────────────────────────────────────────
        // Shapes on a sheet use sheet coordinates; on a mesh they use a frozen
        // camera projection, so dragging across atlas islands stays continuous.
        private bool BeginLiveShape(SceneObject canvas, Point pt)
        {
            if (canvas == null) return false;
            CommitLive();
            TransformStarted?.Invoke(this, EventArgs.Empty);   // one undo step per element

            var layer = PaintLayerFor(canvas);
            if (canvas.PrimitiveType != "Canvas") _surf = CaptureSurfaceMap();
            _liveCanvas = canvas;                                // ScreenToLayer needs it
            if (!ScreenToLayer(pt, out PointF p)) { _liveCanvas = null; return false; }
            var item = new LiveItem
            {
                Kind        = LiveItem.KindFor(PaintKind),
                P1          = p,
                P2          = p,
                Color       = LiveColor(),
                StrokeWidth = LivePenWidth(layer),
                Fill        = _shapeFill,
            };
            StartLive(canvas, item, new Bitmap(layer), null);
            _liveDrag = LiveDrag.Create;
            _liveGrab = p;
            return true;
        }

        // Host entry for the text tool: the text becomes a live element centred on
        // the click. On a 3-D object it is stamped straight away.
        // Rect-select finished: the region becomes a live image; Esc puts it back.
        private void BeginLiveCut(SceneObject canvas, Rectangle r, Bitmap preCut)
        {
            var layer = PaintLayerFor(canvas);
            var bmp = preCut.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var cleared = new Bitmap(preCut);
            using (var g = Graphics.FromImage(cleared))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var br = new SolidBrush(Color.Transparent)) g.FillRectangle(br, r);
            }
            // The layer currently equals preCut; the item covers the same pixels.
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(cleared, 0, 0, layer.Width, layer.Height);
            }
            _liveCanvas = canvas;
            float k = LiveK;
            var item = new LiveItem { Kind = LiveKind.Image, Image = bmp,
                                      P1 = new PointF(r.Left, r.Top * k), P2 = new PointF(r.Right, r.Bottom * k) };
            _liveCutRect = r;
            StartLive(canvas, item, cleared, preCut);
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Mouse ─────────────────────────────────────────────────────
        // Returns true when the press was taken by the live item (a handle or its body).
        // A press elsewhere commits it and returns false, so the tool can start anew.
        private bool LiveMouseDown(MouseEventArgs e)
        {
            if (_live == null || e.Button != MouseButtons.Left) return false;
            if (!ScreenToLayer(e.Location, out PointF p))
            {
                _liveTextJustEnded = _live.IsText;
                CommitLive();
                return false;
            }

            bool typing = IsEditingText;
            int h = typing ? -1 : LiveHandleAt(e.Location);   // no handles while typing
            _liveMargin = LiveMargin();
            bool inside = _live.Contains(p, _liveMargin);
            // Text: the text tool / a double-click / typing put the caret at the click.
            if (_live.IsText && h < 0 && inside && TextMouseDown(e, p)) return true;
            if (!typing && (h >= 0 || inside))
            {
                _liveStart?.Dispose();
                _liveStart  = _live.Clone();
                _liveGrab   = p;
                _liveHandle = h;
                _liveDrag   = h == RotateHandle ? LiveDrag.Rotate : h >= 0 ? LiveDrag.Handle : LiveDrag.Move;
                if (_live.IsGroup) LiveDragStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            // Outside: the element is done. With the text tool, a click that finishes
            // a text doesn't start another one (Word / Figma: it just leaves the box).
            _liveTextJustEnded = _live.IsText;
            CommitLive();
            return false;
        }

        private bool LiveMouseMove(MouseEventArgs e)
        {
            if (TextMouseMove(e)) return true;
            if (_live == null || _liveDrag == LiveDrag.None) return false;
            if ((e.Button & MouseButtons.Left) == 0) return false;
            if (!ScreenToLayer(e.Location, out PointF q)) return true;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool ctrl  = (ModifierKeys & Keys.Control) == Keys.Control;

            switch (_liveDrag)
            {
                case LiveDrag.Create:   DragCreate(q, shift || ctrl); break;
                case LiveDrag.Move:     DragMove(q, shift);           break;
                case LiveDrag.Handle:   DragHandle(q, shift);         break;
                case LiveDrag.Rotate:   DragRotate(q, shift);         break;
            }
            RenderLive();
            return true;
        }

        private bool LiveMouseUp(MouseEventArgs e)
        {
            if (TextMouseUp(e)) return true;
            if (_live == null || _liveDrag == LiveDrag.None || e.Button != MouseButtons.Left) return false;
            var mode = _liveDrag;
            _liveDrag = LiveDrag.None;
            _liveStart?.Dispose(); _liveStart = null;
            if (mode == LiveDrag.Create)
            {
                var b = _live.Box;
                float len = _live.IsLine
                    ? (float)Math.Sqrt((_live.P2.X - _live.P1.X) * (_live.P2.X - _live.P1.X) + (_live.P2.Y - _live.P1.Y) * (_live.P2.Y - _live.P1.Y))
                    : Math.Max(b.Width, b.Height);
                // A click without a drag draws nothing (and leaves no undo step).
                if (len < 3f * LayerPerScreenPx(e.Location)) { CancelLive(); return true; }
            }
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
            return true;
        }

        private void DragCreate(PointF q, bool regular)
        {
            PointF a = _liveGrab;
            float dx = q.X - a.X, dy = q.Y - a.Y;
            if (regular)
            {
                if (_live.IsLine) q = Snap45(a, q);
                else
                {
                    float m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    q = new PointF(a.X + (dx < 0 ? -m : m), a.Y + (dy < 0 ? -m : m));
                }
            }
            _live.P1 = a;
            _live.P2 = q;
        }

        private static PointF Snap45(PointF a, PointF q)
        {
            double ang = Math.Atan2(q.Y - a.Y, q.X - a.X);
            double len = Math.Sqrt((q.X - a.X) * (q.X - a.X) + (q.Y - a.Y) * (q.Y - a.Y));
            double step = Math.PI / 4;
            ang = Math.Round(ang / step) * step;
            return new PointF(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
        }

        private void DragMove(PointF q, bool axisLock)
        {
            float dx = q.X - _liveGrab.X, dy = q.Y - _liveGrab.Y;
            if (axisLock) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            _live.P1 = new PointF(_liveStart.P1.X + dx, _liveStart.P1.Y + dy);
            _live.P2 = new PointF(_liveStart.P2.X + dx, _liveStart.P2.Y + dy);
        }

        private void DragRotate(PointF q, bool snap)
        {
            PointF c = _liveStart.Center;
            double a0 = Math.Atan2(_liveGrab.Y - c.Y, _liveGrab.X - c.X);
            double a1 = Math.Atan2(q.Y - c.Y, q.X - c.X);
            float ang = _liveStart.Angle + (float)((a1 - a0) * 180.0 / Math.PI);
            if (snap) ang = (float)(Math.Round(ang / 15.0) * 15.0);
            ang %= 360f;
            if (ang > 180f) ang -= 360f;
            if (ang <= -180f) ang += 360f;
            _live.Angle = ang;
        }

        private void DragHandle(PointF q, bool proportional)
        {
            var s = _liveStart;
            if (s.IsLine)
            {
                if (_liveHandle == 0) _live.P1 = proportional ? Snap45(s.P2, q) : q;
                else                  _live.P2 = proportional ? Snap45(s.P1, q) : q;
                return;
            }

            RectangleF b0 = s.Box;
            float hw = Math.Max(0.5f, b0.Width / 2f), hh = Math.Max(0.5f, b0.Height / 2f);
            PointF d = LiveItem.HandleDirs[_liveHandle];
            bool corner = d.X != 0 && d.Y != 0;
            PointF qL = s.ToLocal(q);
            // The grabbed handle sits on the frame, _liveMargin outside the box.
            qL = new PointF(qL.X - d.X * _liveMargin, qL.Y - d.Y * _liveMargin);
            float fx = -d.X * hw, fy = -d.Y * hh;           // the opposite point stays put
            float mx = d.X != 0 ? qL.X : hw, my = d.Y != 0 ? qL.Y : hh;
            float x0 = d.X != 0 ? Math.Min(fx, mx) : -hw, x1 = d.X != 0 ? Math.Max(fx, mx) : hw;
            float y0 = d.Y != 0 ? Math.Min(fy, my) : -hh, y1 = d.Y != 0 ? Math.Max(fy, my) : hh;

            if (s.IsText || s.IsGroup || (corner && proportional))
            {
                // Uniform scale about the opposite corner (text always keeps its shape):
                // the pointer is projected onto the box diagonal, so the size follows
                // the hand smoothly whatever the box's proportions.
                float gx = d.X * 2 * hw, gy = d.Y * 2 * hh;
                float k = ((qL.X - fx) * gx + (qL.Y - fy) * gy) / (gx * gx + gy * gy);
                k = Math.Max(0.02f, k);
                float sx = d.X, sy = d.Y;
                float w = 2 * hw * k, h = 2 * hh * k;
                if (s.IsText)
                {
                    _live.FontPx = Math.Max(2f, s.FontPx * k);
                    _live.P1 = PointF.Empty; _live.P2 = PointF.Empty;
                    _live.Normalize();                       // exact glyph box at the new size
                    w = _live.Box.Width; h = _live.Box.Height;
                }
                x0 = Math.Min(fx, fx + sx * w); x1 = Math.Max(fx, fx + sx * w);
                y0 = Math.Min(fy, fy + sy * h); y1 = Math.Max(fy, fy + sy * h);
            }

            float nw = Math.Max(1f, x1 - x0), nh = Math.Max(1f, y1 - y0);
            PointF center = s.ToLayer((x0 + x1) / 2f, (y0 + y1) / 2f);
            _live.SetBox(center, nw, nh);
        }

        // ── Handles (screen space) ────────────────────────────────────
        private const int RotateHandle = 100;
        private const float FrameGap = 5f;   // px between the shape and its frame

        // The frame sits outside the stroke (half its width + a few screen px), so
        // it never hides a thin outline; handles live on the frame.
        private float LiveMargin()
        {
            if (_live == null) return 0f;
            PointF c = LayerToScreen(_live.Center);
            float perPx = LayerPerScreenPx(new Point((int)c.X, (int)c.Y));
            return (_live.IsShape ? _live.StrokeWidth / 2f : 0f) + FrameGap * perPx;
        }

        private PointF FrameHandle(int i, float m)
        {
            RectangleF b = _live.Box;
            PointF d = LiveItem.HandleDirs[i];
            return _live.ToLayer(d.X * (b.Width / 2f + m), d.Y * (b.Height / 2f + m));
        }

        private PointF[] FrameCorners(float m)
        {
            return new[] { FrameHandle(0, m), FrameHandle(2, m), FrameHandle(4, m), FrameHandle(6, m) };
        }

        // Handles shown for the item at its current on-screen size.
        private bool ShowEdgeHandles()
        {
            if (_live == null || _live.IsLine || _live.IsText || _live.IsGroup) return false;
            PointF a = LayerToScreen(_live.HandleLayer(0)), b = LayerToScreen(_live.HandleLayer(4));   // box size
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy) > 48;
        }

        private bool RotatePoint(out PointF rot, out PointF top)
        {
            rot = top = PointF.Empty;
            if (_live == null || _live.IsLine) return false;
            PointF c = LayerToScreen(_live.Center);
            top = LayerToScreen(FrameHandle(1, LiveMargin()));
            float dx = top.X - c.X, dy = top.Y - c.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) { dx = 0; dy = -1; len = 1; }
            rot = new PointF(top.X + dx / len * RotateReach, top.Y + dy / len * RotateReach);
            return true;
        }

        private int LiveHandleAt(Point s)
        {
            if (_live == null) return -1;
            if (RotatePoint(out PointF rot, out PointF _) && Dist(rot, s) <= HandleHit) return RotateHandle;
            if (_live.IsLine)
            {
                if (Dist(LayerToScreen(_live.P1), s) <= HandleHit) return 0;
                if (Dist(LayerToScreen(_live.P2), s) <= HandleHit) return 1;
                return -1;
            }
            bool edges = ShowEdgeHandles();
            float m = LiveMargin();
            for (int i = 0; i < 8; i++)
            {
                if (i % 2 == 1 && !edges) continue;
                if (Dist(LayerToScreen(FrameHandle(i, m)), s) <= HandleHit) return i;
            }
            return -1;
        }

        private static float Dist(PointF a, Point b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // Cursor over the live item: resize arrows follow the handle's real direction.
        private Cursor LiveCursorAt(Point s)
        {
            if (_live == null) return null;
            // Over text that will take a caret: the I-beam, as in any editor.
            if (_live.IsText && (IsEditingText || (_tool == ToolMode.Paint && PaintKind == BrushKind.Text2D))
                && ScreenToLayer(s, out PointF tq) && _live.Contains(tq, LiveMargin()))
                return Cursors.IBeam;
            if (IsEditingText) return null;
            int h = LiveHandleAt(s);
            if (h == RotateHandle) return Cursors.Hand;
            if (h >= 0)
            {
                PointF c = LayerToScreen(_live.Center);
                PointF p = _live.IsLine ? LayerToScreen(h == 0 ? _live.P1 : _live.P2) : LayerToScreen(FrameHandle(h, LiveMargin()));
                double ang = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180.0 / Math.PI;
                int oct = (int)Math.Round(((ang % 180) + 180) % 180 / 45.0) % 4;   // 0 —, 1 \, 2 |, 3 /
                switch (oct)
                {
                    case 0:  return Cursors.SizeWE;
                    case 1:  return Cursors.SizeNWSE;
                    case 2:  return Cursors.SizeNS;
                    default: return Cursors.SizeNESW;
                }
            }
            if (ScreenToLayer(s, out PointF q) && _live.Contains(q, LiveMargin()))
                return Cursors.SizeAll;
            return null;
        }

        private void DrawLiveGL()
        {
            if (_live == null || _liveCanvas == null) return;
            if (SurfaceCameraMoved()) { BeginInvoke(new Action(CommitLive)); }   // a text on a shape is tied to its view
            BeginScreenOverlay();
            GL.Enable(EnableCap.LineSmooth);
            Color acc = Aplat.UI.Theme.Accent;
            float ar = acc.R / 255f, ag = acc.G / 255f, ab = acc.B / 255f;

            if (!_live.IsLine)
            {
                // Frame: a dark under-stroke keeps it readable on any paint.
                float m = LiveMargin();
                PointF[] q = Array.ConvertAll(FrameCorners(m), LayerToScreen);
                GL.LineWidth(3f);
                GL.Color4(0f, 0f, 0f, 0.28f);
                GL.Begin(PrimitiveType.LineLoop); foreach (var p in q) GL.Vertex2(p.X, p.Y); GL.End();
                GL.LineWidth(1.25f);
                GL.Color4(ar, ag, ab, 1f);
                GL.Begin(PrimitiveType.LineLoop); foreach (var p in q) GL.Vertex2(p.X, p.Y); GL.End();

                if (IsEditingText)
                {
                    DrawTextEditGL(ar, ag, ab);   // caret + selection; no handles while typing
                }
                else if (RotatePoint(out PointF rot, out PointF top))
                {
                    GL.Begin(PrimitiveType.Lines); GL.Vertex2(top.X, top.Y); GL.Vertex2(rot.X, rot.Y); GL.End();
                    GL.Color4(1f, 1f, 1f, 1f);
                    Circle(PrimitiveType.TriangleFan, rot.X, rot.Y, 5.5f, 24);
                    GL.LineWidth(1.5f);
                    GL.Color4(ar, ag, ab, 1f);
                    Circle(PrimitiveType.LineLoop, rot.X, rot.Y, 5.5f, 24);
                }

                bool edges = ShowEdgeHandles();
                for (int i = 0; i < 8 && !IsEditingText; i++)
                {
                    if (i % 2 == 1 && !edges) continue;
                    DrawHandleSquare(LayerToScreen(FrameHandle(i, m)), ar, ag, ab);
                }
            }
            else
            {
                DrawHandleSquare(LayerToScreen(_live.P1), ar, ag, ab);
                DrawHandleSquare(LayerToScreen(_live.P2), ar, ag, ab);
            }
            GL.Disable(EnableCap.LineSmooth);
            GL.LineWidth(1f);
            EndScreenOverlay();
        }

        private static void DrawHandleSquare(PointF p, float r, float g, float b)
        {
            float x = (float)Math.Round(p.X) + 0.5f, y = (float)Math.Round(p.Y) + 0.5f;
            const float s = 4.5f;
            GL.Color4(1f, 1f, 1f, 1f);
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex2(x - s, y - s); GL.Vertex2(x + s, y - s); GL.Vertex2(x + s, y + s); GL.Vertex2(x - s, y + s);
            GL.End();
            GL.LineWidth(1.5f);
            GL.Color4(r, g, b, 1f);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Vertex2(x - s, y - s); GL.Vertex2(x + s, y - s); GL.Vertex2(x + s, y + s); GL.Vertex2(x - s, y + s);
            GL.End();
        }
    }
}
