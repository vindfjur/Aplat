using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Controls
{
    // The lasso (the iPhone Markup way): draw a loop round what you want — everything the loop
    // encloses or crosses (strokes, shapes, text) is picked up as one selection, with a frame to
    // move, scale and turn it, and a small bar to recolour, copy or delete it.
    //
    // The picked ops leave the canvas's journal for the time of the edit and become one live
    // element (LiveKind.Group), shown through the frame's transform; settling it puts them back as
    // a single GroupOp that carries that transform. Nothing is re-rendered from pixels: strokes stay
    // strokes, so a moved drawing is as sharp as it was and can be erased or lassoed again as an object.
    // Esc puts everything back as it was.
    public partial class SceneView
    {
        private bool          _lassoDragging;
        private List<PointF>  _lassoPts;                       // layer px
        private List<PointF>  _lassoScreen;                    // for drawing the loop
        private SceneObject   _lassoCanvas;

        // The ops the live selection took out of the journal, with their places.
        private List<KeyValuePair<int, InkOp>> _lifted;
        private bool _groupRecoloured;     // the copies no longer equal the originals: settling must not put those back

        // Raised when a drag of the live selection starts, so the host can hide its action bar.
        public event EventHandler LiveDragStarted;

        private void BeginLasso(Point pt)
        {
            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv)) return;
            if (obj.PrimitiveType != "Canvas") { Notify?.Invoke(this, Loc.T("Лассо работает на холсте")); return; }
            if (IsLocked(obj)) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }
            PaintLayerFor(obj);                    // readies the journal
            _lassoCanvas   = obj;
            _liveCanvas    = obj;                  // ScreenToLayer needs it
            _lassoDragging = true;
            _lassoPts      = new List<PointF>();
            _lassoScreen   = new List<PointF>();
            LassoTo(pt);
        }

        private void LassoTo(Point pt)
        {
            if (!ScreenToLayer(pt, out PointF p)) return;
            if (_lassoScreen.Count > 0)
            {
                PointF last = _lassoScreen[_lassoScreen.Count - 1];
                if (Math.Abs(last.X - pt.X) + Math.Abs(last.Y - pt.Y) < 3) return;
            }
            _lassoPts.Add(p);
            _lassoScreen.Add(pt);
            Redraw();
        }

        private void EndLasso()
        {
            _lassoDragging = false;
            var canvas = _lassoCanvas;
            var poly = _lassoPts;
            _lassoCanvas = null;
            var screen = _lassoScreen; _lassoScreen = null; _lassoPts = null;
            if (canvas == null || poly == null || poly.Count < 3) { _liveCanvas = null; Redraw(); return; }

            PointF endPt = screen[screen.Count - 1];
            float perPx = LayerPerScreenPx(new Point((int)endPt.X, (int)endPt.Y));
            var picked = LassoPick(canvas, poly, Math.Max(1.5f, 4f * perPx));
            if (picked.Count == 0)
            {
                _liveCanvas = null;
                Notify?.Invoke(this, Loc.T("Лассо: петля ничего не задела"));
                Redraw();
                return;
            }
            TransformStarted?.Invoke(this, EventArgs.Empty);   // the undo step is the state before the lift
            LiftSelection(canvas, picked);
        }

        // ── Choosing ──────────────────────────────────────────────────
        // An object is picked when the loop encloses a point of it or crosses its ink. Fills have no
        // shape to carry along and stay; what a pixel eraser already wiped out is not picked.
        private List<InkOp> LassoPick(SceneObject canvas, List<PointF> poly, float tol)
        {
            var picked = new List<InkOp>();
            var ink = canvas.Material.Ink;
            var layer = PaintLayerFor(canvas);
            if (ink == null) return picked;

            RectangleF bb = RectangleF.FromLTRB(poly[0].X, poly[0].Y, poly[0].X, poly[0].Y);
            foreach (var p in poly) bb = RectangleF.Union(bb, new RectangleF(p.X, p.Y, 0, 0));
            bb.Inflate(tol + 3, tol + 3);

            // The loop's outline, closed, walked in steps no longer than the tolerance.
            var edge = new List<PointF>();
            for (int i = 0; i < poly.Count; i++)
            {
                PointF a = poly[i], b = poly[(i + 1) % poly.Count];
                float len = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int n = Math.Max(1, (int)Math.Ceiling(len / Math.Max(1f, tol)));
                for (int k = 0; k < n; k++) edge.Add(new PointF(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n));
            }

            foreach (var op in ink.Ops)
            {
                if (!op.IsObject || op is FillOp) continue;
                var box = RectangleF.FromLTRB(op.Bounds.Left, op.Bounds.Top, op.Bounds.Right, op.Bounds.Bottom);
                if (!box.IntersectsWith(bb)) continue;

                bool seen = false, inside = false;
                foreach (PointF s in op.SamplePoints())
                {
                    if (!seen && VisibleInk(layer, s)) seen = true;
                    if (!inside && InPolygon(poly, s)) inside = true;
                    if (seen && inside) break;
                }
                bool hit = inside && (seen || !op.ChecksVisibility);
                if (!hit)
                {
                    var wide = box; wide.Inflate(tol, tol);
                    foreach (PointF e in edge)
                    {
                        if (!wide.Contains(e)) continue;
                        PointF q;
                        if (!op.NearestInk(e, tol, out q)) continue;
                        if (op.ChecksVisibility && !VisibleInk(layer, q)) continue;
                        hit = true;
                        break;
                    }
                }
                if (hit) picked.Add(op);
            }
            return picked;
        }

        private static bool VisibleInk(Bitmap layer, PointF p)
        {
            int x = Math.Max(0, Math.Min(layer.Width - 1, (int)p.X));
            int y = Math.Max(0, Math.Min(layer.Height - 1, (int)p.Y));
            return layer.GetPixel(x, y).A > 12;
        }

        private static bool InPolygon(List<PointF> poly, PointF p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            return inside;
        }

        // ── Lifting ───────────────────────────────────────────────────
        private void LiftSelection(SceneObject canvas, List<InkOp> picked)
        {
            var layer = PaintLayerFor(canvas);
            var ink = canvas.Material.Ink;
            _liveCanvas = canvas;
            float k = LiveK;

            // What the live element is made of: copies of the picked ops (the originals wait in
            // _lifted); a lone group is opened, so moving the same drawing again does not nest.
            var ops = new List<InkOp>();
            float[] m0 = { 1, 0, 0, 1, 0, 0 };
            if (picked.Count == 1 && picked[0] is GroupOp)
            {
                var g = (GroupOp)picked[0].Clone();
                m0 = g.M;
                ops.AddRange(g.Children);
                g.Children = new List<InkOp>();
            }
            else foreach (var op in picked) ops.Add(op.Clone());

            RectangleF ib = RectangleF.Empty; float ow = 0f;
            using (var probe = GroupOp.Create(ops, m0, layer.Size))
            {
                ib = probe.InkBounds; ow = probe.InkWidth;
                probe.Children = new List<InkOp>();   // still in use by the item
            }
            var box0 = RectangleF.FromLTRB(ib.Left, ib.Top * k, ib.Right, ib.Bottom * k);

            var preLift = new Bitmap(layer);
            List<KeyValuePair<int, InkOp>> removed;
            Rectangle area = ink.Lift(picked, layer, out removed);
            canvas.Material.MarkPaintDirty(area);
            _lifted = removed;
            _groupRecoloured = false;

            var item = new LiveItem
            {
                Kind = LiveKind.Group, Ops = ops, M0 = m0, Box0 = box0, GroupK = k, GroupInk = ow,
                P1 = new PointF(box0.Left, box0.Top), P2 = new PointF(box0.Right, box0.Bottom),
            };
            StartLive(canvas, item, new Bitmap(layer), preLift);
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool GroupUntouched(LiveItem g)
        {
            return !_groupRecoloured && Math.Abs(g.Angle) < 0.01f &&
                   Math.Abs(g.P1.X - g.Box0.Left) < 0.01f && Math.Abs(g.P1.Y - g.Box0.Top) < 0.01f &&
                   Math.Abs(g.P2.X - g.Box0.Right) < 0.01f && Math.Abs(g.P2.Y - g.Box0.Bottom) < 0.01f;
        }

        private void DisposeLifted()
        {
            if (_lifted == null) return;
            foreach (var kv in _lifted) kv.Value.Dispose();
            _lifted = null;
        }

        // ── The action bar's commands ─────────────────────────────────
        // Keyboard focus back to the viewport (a floating helper window may have taken it).
        public void FocusView() { if (_gl != null) _gl.Focus(); }

        public bool HasLiveGroup { get { return _live != null && _live.IsGroup; } }

        // Where the host puts its bar: above the frame's top edge, in this control's pixels.
        public bool LiveBarAnchor(out Point top, out Point bottom)
        {
            top = bottom = Point.Empty;
            if (!HasLiveGroup || _liveCanvas == null) return false;
            PointF a = LayerToScreen(FrameHandle(0, 0)), b = LayerToScreen(FrameHandle(2, 0));
            PointF c = LayerToScreen(FrameHandle(4, 0)), d = LayerToScreen(FrameHandle(6, 0));
            float y0 = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)), y1 = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
            float cx = (a.X + b.X + c.X + d.X) / 4f;
            top = new Point((int)cx, (int)y0);
            bottom = new Point((int)cx, (int)y1);
            return true;
        }

        // The colour dot: every stroke and shape of the selection takes the colour.
        public void RecolorLive(Color color)
        {
            if (!HasLiveGroup) return;
            _groupRecoloured = true;
            EditLive(i => { foreach (var op in i.Ops) op.Recolor(color.ToArgb()); });
        }

        // The copy button: the selection stays where it is and a copy, a little aside, is picked up.
        public void DuplicateLive()
        {
            if (!HasLiveGroup) return;
            var canvas = _liveCanvas;
            var copy = _live.Clone();
            float step = 24f * Math.Max(0.5f, LiveLayerScale);
            copy.P1 = new PointF(copy.P1.X + step, copy.P1.Y + step);
            copy.P2 = new PointF(copy.P2.X + step, copy.P2.Y + step);

            TransformStarted?.Invoke(this, EventArgs.Empty);   // commits this one, then the step
            if (_live != null) CommitLive();
            _liveCanvas = canvas;
            var layer = PaintLayerFor(canvas);
            StartLive(canvas, copy, new Bitmap(layer), null);
            if (_scene != null) _scene.IsModified = true;
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }

        // The bin: the selection is gone for good (its picture goes, it is not put back).
        public void DeleteLive()
        {
            if (!HasLiveGroup) return;
            var layer = PaintLayerFor(_liveCanvas);
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(_liveBase, 0, 0, layer.Width, layer.Height);
            }
            _liveCanvas.Material.MarkPaintDirty();
            DisposeLifted();
            _live.Dispose(); _liveBase?.Dispose(); _liveCancel?.Dispose();
            _live = null; _liveBase = null; _liveCancel = null;
            _liveCanvas = null;
            _liveDrawn = Rectangle.Empty;
            _liveDrag = LiveDrag.None;
            FlushPaintEncoding();
            if (_scene != null) _scene.IsModified = true;
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
            LiveChanged?.Invoke(this, EventArgs.Empty);
            Redraw();
        }

        // ── The loop on screen ────────────────────────────────────────
        private void DrawLassoGL()
        {
            if (!_lassoDragging || _lassoScreen == null || _lassoScreen.Count < 2) return;
            BeginScreenOverlay();
            GL.Enable(EnableCap.LineSmooth);
            GL.LineWidth(3f);
            GL.Color4(1f, 1f, 1f, 0.9f);
            GL.Begin(PrimitiveType.LineStrip); foreach (var p in _lassoScreen) GL.Vertex2(p.X, p.Y); GL.End();
            GL.Enable(EnableCap.LineStipple);
            GL.LineStipple(1, 0x00FF);
            GL.LineWidth(1.5f);
            Color acc = Aplat.UI.Theme.Accent;
            GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 1f);
            GL.Begin(PrimitiveType.LineStrip); foreach (var p in _lassoScreen) GL.Vertex2(p.X, p.Y); GL.End();
            GL.Disable(EnableCap.LineStipple);
            GL.Disable(EnableCap.LineSmooth);
            GL.LineWidth(1f);
            EndScreenOverlay();
        }
    }
}
