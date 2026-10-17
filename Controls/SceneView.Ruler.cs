using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Aplat.Controls
{
    // The ruler (the iPhone Markup one): a frosted, graduated strip laid over the sheet. Draw
    // along either long edge and the stroke runs perfectly straight there — start within a hand's
    // breadth of the edge and the pen follows the edge line until it is pulled well away.
    //
    // It is a helper, not a drawing: it lives over the canvas, is never painted into it and takes
    // no undo step. Drag its body to move it, the round knob at either end to turn it (it settles on
    // multiples of 45°; Shift — steps of 15°), the end of its body to make it longer or shorter.
    // A double click on an end pins it: that end stays where it is, and the ruler hinges on it (the
    // free end follows the mouse; its end handle still sets the length); a second double click lets
    // it go. Geometry is kept in sheet px (a sheet pixel is square, see SceneView.Live), so the
    // ruler stays put on the canvas as the camera moves.
    //
    // The ruler is a real scene object (RulerFactory): two plates in a group, drawn by the ordinary
    // renderer, so it has a place in space, is hidden by shapes in front of it and can be picked up with
    // the Move / Rotate / Scale tools too. Its place on the sheet (_rulerC, _rulerAngle, _rulerLen) is read
    // from the object whenever the object has been changed from outside, and written back to it when the
    // handles here change it.
    public partial class SceneView
    {
        private bool        _rulerOn { get { return RulerObject() != null; } }
        private string      _rulerSig = "";          // the object's transform as last read or written
        private bool        _rulerOnSheet = true;     // lying flat on the sheet (not lifted or tilted away by the 3D tools)
        private bool        _rulerRetex;              // its picture no longer fits its length
        private SceneObject _rulerCanvas;
        private bool        _rulerPlaced;
        private PointF      _rulerC;                 // centre, sheet px
        private float       _rulerAngle;             // degrees, clockwise on screen
        private float       _rulerLen, _rulerThick;  // sheet px

        // Blocked: the press landed on a pinned end (or on a ruler pinned at both ends) — it is the
        // ruler's, so it must not paint through, but nothing moves.
        private enum RulerDrag { None, Move, KnobA, KnobB, StretchA, StretchB, Blocked, Body }
        private RulerDrag _rulerDrag;
        private PointF    _rulerGrab, _rulerStartC;
        private float     _rulerGrabAngle, _rulerGrabLen, _rulerGrabOff;
        private Point     _rulerPressPt;
        private bool      _rulerMoved;                // past the click-jitter threshold
        private bool      _rulerPinA, _rulerPinB;     // an end fixed on the sheet (double click)

        private int       _rulerEdge = -1;           // the edge the current stroke follows (0 top, 1 bottom)
        private bool      _rulerEdgeDecided;
        private bool      _overRuler;
        private float     _rulerStrokeGap;

        private const float RulerSnapPx    = 16f;    // start this close to an edge: the stroke follows it
        private const float RulerReleasePx = 44f;    // … until the pen is this far from it
        private const float RulerKnobPx    = 11f;
        private const float RulerKnobGapPx = 18f;
        private const float RulerEndZonePx = 9f;     // the end of the body that stretches it
        private const float RulerDragPx    = 3f;     // a press must travel this far to count as a drag
        private const float RulerMinUnits  = 60f;    // shortest ruler, in brush-size px (see RulerUnit)

        private int    _rulerLabelTex;
        private string _rulerLabelKey;

        public event EventHandler RulerChanged;

        public bool RulerVisible
        {
            get { return _rulerOn; }
            set
            {
                if (_rulerOn == value) return;
                if (value)
                {
                    if (!PlaceRuler()) { Notify?.Invoke(this, Loc.T("Линейка кладётся на холст")); return; }
                    CreateRulerObject();
                }
                else if (_scene != null)
                {
                    var grp = RulerObject();
                    _scene.Objects.Remove(grp);
                    _selected.RemoveAll(o => ReferenceEquals(o, grp) || grp.Children.Contains(o));
                }
                _rulerDrag = RulerDrag.None;
                _rulerEdge = -1; _rulerEdgeDecided = false;
                Redraw();
                RulerChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // The ruler's group in the scene (null: there is none).
        private SceneObject RulerObject()
        {
            if (_scene == null) return null;
            foreach (var o in _scene.Objects)
                if (o.IsGroup && RulerFactory.IsRuler(o) && o.Children.Count >= 3) return o;
            return null;
        }

        // ── Geometry ──────────────────────────────────────────────────
        private SceneObject FindRulerCanvas()
        {
            if (_scene == null) return null;
            foreach (var top in _scene.Objects)
            {
                if (!top.IsVisible) continue;
                foreach (var leaf in SceneGeometry.Leaves(top))
                    if (leaf.PrimitiveType == "Canvas" && leaf.IsVisible && leaf.Mesh != null && leaf.Material != null) return leaf;
            }
            return null;
        }

        private bool RulerFacesCamera()
        {
            if (!CanvasCorners(_rulerCanvas, out Vec3 tl, out Vec3 tr, out Vec3 br, out Vec3 bl)) return false;
            Vec3 normal = Vec3.Cross(br - bl, tl - bl).Normalized();
            Vec3 view = _camera.Mode == ViewMode.Perspective
                ? (_camera.GetPosition() - tl).Normalized() : _camera.GetOrthoViewDir();
            return Vec3.Dot(normal, view) > 1e-5f;
        }

        private bool PlaceRuler()
        {
            var canvas = FindRulerCanvas();
            if (canvas == null) return false;
            if (!ReferenceEquals(canvas, _rulerCanvas)) _rulerPlaced = false;
            _rulerCanvas = canvas;
            if (!_rulerPlaced)
            {
                var existing = canvas.Material.PaintLayer;
                float w = existing != null ? existing.Width : Material.CanvasPaintSize;
                float k = RulerK();
                _rulerLen = w * 0.78f;
                _rulerThick = w * 0.11f;
                _rulerC = new PointF(w / 2f, w * k * 0.62f);
                _rulerAngle = 0f;
                _rulerPinA = _rulerPinB = false;
                _rulerPlaced = true;
            }
            return true;
        }

        private T WithRulerCanvas<T>(Func<T> f)
        {
            var saved = _liveCanvas;
            _liveCanvas = _rulerCanvas;
            try { return f(); }
            finally { _liveCanvas = saved; }
        }

        private float RulerK() { return WithRulerCanvas(() => LiveK); }

        private PointF SheetToScreen(PointF sh, float k)
        {
            // LayerToScreen already accepts square sheet pixels (including LiveK).
            return WithRulerCanvas(() => LayerToScreen(sh));
        }

        private bool ScreenToSheet(Point s, float k, out PointF sh)
        {
            PointF p = PointF.Empty;
            bool ok = WithRulerCanvas(() => ScreenToLayer(s, out p));
            sh = p; // ScreenToLayer already returns square sheet pixels.
            return ok;
        }

        private float SheetPerScreenPx(Point s)
        {
            return WithRulerCanvas(() => LayerPerScreenPx(s));
        }

        // A point of the ruler by its own axes: x along it, y across (sheet px, from the centre).
        private PointF RulerLocal(float lx, float ly)
        {
            double a = _rulerAngle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            return new PointF(_rulerC.X + lx * cos - ly * sin, _rulerC.Y + lx * sin + ly * cos);
        }

        // An end of the ruler on the sheet (A is the left one at angle 0).
        private PointF RulerEnd(bool b) { return RulerLocal(b ? _rulerLen / 2f : -_rulerLen / 2f, 0f); }

        // Sheet px per brush-size px: the scale and the length label count in the units of the size slider.
        private float RulerUnit
        {
            get { return _rulerCanvas != null && _rulerCanvas.Material != null ? Material.PaintScale(_rulerCanvas.Material.PaintLayer) : 1f; }
        }

        // Sets the ruler by its two ends (tests and the pivot/stretch code share it).
        private void RulerSetEnds(PointF a, PointF b)
        {
            _rulerC = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            _rulerLen = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (_rulerLen > 1e-3f) _rulerAngle = (float)NormalizeAngle(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI);
        }

        private float RulerMinLen { get { return RulerMinUnits * RulerUnit; } }
        private float RulerMaxLen { get { return 3f * Material.CanvasPaintSize; } }

        private struct RulerGeom
        {
            public PointF[] Corners;   // screen: TL, TR, BR, BL
            public PointF Centre, KnobA, KnobB, EndA, EndB;
        }

        private RulerGeom RulerScreen()
        {
            float k = RulerK(), hl = _rulerLen / 2f, ht = _rulerThick / 2f;
            var g = new RulerGeom
            {
                Corners = new[]
                {
                    SheetToScreen(RulerLocal(-hl, -ht), k), SheetToScreen(RulerLocal(hl, -ht), k),
                    SheetToScreen(RulerLocal(hl,  ht), k),  SheetToScreen(RulerLocal(-hl, ht), k),
                },
                Centre = SheetToScreen(_rulerC, k),
            };
            PointF endA = SheetToScreen(RulerLocal(-hl, 0), k), endB = SheetToScreen(RulerLocal(hl, 0), k);
            g.EndA = endA; g.EndB = endB;
            g.KnobA = Beyond(g.Centre, endA, RulerKnobGapPx);
            g.KnobB = Beyond(g.Centre, endB, RulerKnobGapPx);
            return g;
        }

        private static PointF Beyond(PointF from, PointF to, float gap)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return to;
            return new PointF(to.X + dx / len * gap, to.Y + dy / len * gap);
        }

        private static bool InQuad(PointF[] q, PointF p)
        {
            bool inside = false;
            for (int i = 0, j = q.Length - 1; i < q.Length; j = i++)
                if ((q[i].Y > p.Y) != (q[j].Y > p.Y) &&
                    p.X < (q[j].X - q[i].X) * (p.Y - q[i].Y) / (q[j].Y - q[i].Y) + q[i].X)
                    inside = !inside;
            return inside;
        }

        private RulerDrag RulerHit(Point s)
        {
            if (!_rulerOn || _rulerCanvas == null) return RulerDrag.None;
            RulerSync();
            // The ruler is an object, not paper: a drawing tool does nothing on it, wherever it lies.
            bool plate = ReferenceEquals(PickTopLevel(s), RulerObject());
            if (!_rulerOnSheet) return plate ? RulerDrag.Body : RulerDrag.None;
            var g = RulerScreen();
            if (Dist(g.KnobA, s) <= RulerKnobPx + 3) return RulerDrag.KnobA;
            if (Dist(g.KnobB, s) <= RulerKnobPx + 3) return RulerDrag.KnobB;
            // The short edges of the body: grab to stretch.
            if (DistSeg(g.Corners[0], g.Corners[3], s) <= RulerEndZonePx) return RulerDrag.StretchA;
            if (DistSeg(g.Corners[1], g.Corners[2], s) <= RulerEndZonePx) return RulerDrag.StretchB;
            if (!plate && !InQuad(g.Corners, s)) return RulerDrag.None;
            return RulerMoveKey ? RulerDrag.Move : RulerDrag.Body;   // Ctrl: carry the ruler by its body
        }

        // ── Mouse ─────────────────────────────────────────────────────
        // True when the press is the ruler's (its body or a knob).
        private bool _rulerMoveHeld;   // tests: as if Ctrl were down
        private bool RulerMoveKey { get { return _rulerMoveHeld || (ModifierKeys & Keys.Control) == Keys.Control; } }

        private bool RulerMouseDown(MouseEventArgs e)
        {
            if (!_rulerOn || e.Button != MouseButtons.Left || _tool != ToolMode.Paint || _rulerCanvas == null) return false;
            var hit = RulerHit(e.Location);   // (reads the object first)
            if (hit == RulerDrag.None) return false;
            float k = RulerK();
            PointF q;
            if (!ScreenToSheet(e.Location, k, out q)) return false;

            // A double click on an end (its knob or its edge) pins it, or lets it go.
            if (hit == RulerDrag.Body) { _rulerDrag = RulerDrag.Blocked; _rulerMoved = false; _rulerPressPt = e.Location; return true; }
            if (e.Clicks >= 2 && hit != RulerDrag.Move)
            {
                bool b = hit == RulerDrag.KnobB || hit == RulerDrag.StretchB;
                if (b) _rulerPinB = !_rulerPinB; else _rulerPinA = !_rulerPinA;
                _rulerDrag = RulerDrag.None;
                Redraw();
                RulerChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            bool pinnedA = hit == RulerDrag.KnobA || hit == RulerDrag.StretchA ? _rulerPinA : false;
            bool pinnedB = hit == RulerDrag.KnobB || hit == RulerDrag.StretchB ? _rulerPinB : false;
            bool both = hit == RulerDrag.Move && _rulerPinA && _rulerPinB;
            _rulerDrag = pinnedA || pinnedB || both ? RulerDrag.Blocked : hit;
            _rulerGrab = q;
            _rulerStartC = _rulerC;
            _rulerGrabAngle = _rulerAngle;
            _rulerGrabLen = _rulerLen;
            _rulerPressPt = e.Location;
            _rulerMoved = false;
            if (hit == RulerDrag.StretchA || hit == RulerDrag.StretchB)
            {
                // Keep the offset between the grab and the end, so the end does not jump to the mouse.
                bool b = hit == RulerDrag.StretchB;
                PointF f = RulerEnd(!b);
                double a = _rulerAngle * Math.PI / 180.0;
                float dx = (float)Math.Cos(a) * (b ? 1 : -1), dy = (float)Math.Sin(a) * (b ? 1 : -1);
                _rulerGrabOff = _rulerLen - ((q.X - f.X) * dx + (q.Y - f.Y) * dy);
            }
            return true;
        }

        private bool RulerMouseMove(MouseEventArgs e)
        {
            if (_rulerDrag == RulerDrag.None) return false;
            if ((e.Button & MouseButtons.Left) == 0) { _rulerDrag = RulerDrag.None; return false; }
            if (_rulerDrag == RulerDrag.Blocked) return true;
            if (!_rulerMoved)
            {
                if (Math.Abs(e.X - _rulerPressPt.X) < RulerDragPx && Math.Abs(e.Y - _rulerPressPt.Y) < RulerDragPx) return true;
                _rulerMoved = true;   // a double click's first press must not nudge the ruler
            }
            float k = RulerK();
            PointF q;
            if (!ScreenToSheet(e.Location, k, out q)) return true;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool pivot = _rulerPinA != _rulerPinB && _rulerDrag != RulerDrag.StretchA && _rulerDrag != RulerDrag.StretchB;
            if (pivot)
                RulerPivot(q, shift);
            else if (_rulerDrag == RulerDrag.StretchA || _rulerDrag == RulerDrag.StretchB)
                RulerStretch(_rulerDrag == RulerDrag.StretchB, q, SheetPerScreenPx(e.Location));
            else if (_rulerDrag == RulerDrag.Move)
                _rulerC = new PointF(_rulerStartC.X + q.X - _rulerGrab.X, _rulerStartC.Y + q.Y - _rulerGrab.Y);
            else
            {
                double a = Math.Atan2(q.Y - _rulerC.Y, q.X - _rulerC.X) * 180.0 / Math.PI;
                if (_rulerDrag == RulerDrag.KnobA) a += 180.0;   // that end points back along the ruler
                _rulerAngle = (float)NormalizeAngle(SnapAngle(a, shift));
            }
            RulerApply();
            if (_rulerDrag == RulerDrag.StretchA || _rulerDrag == RulerDrag.StretchB || pivot) _rulerRetex = true;
            Redraw();
            return true;
        }

        private static double SnapAngle(double a, bool shift)
        {
            return shift ? Math.Round(a / 15.0) * 15.0 : MagnetTo45(a);
        }

        // One end is pinned: the ruler turns about it so the free end follows the mouse (length kept).
        private void RulerPivot(PointF q, bool shift)
        {
            bool pinB = _rulerPinB;                                  // the other end is free
            PointF p = RulerEnd(pinB);
            double dx = q.X - p.X, dy = q.Y - p.Y;
            if (dx * dx + dy * dy < 1e-6) return;
            double a = Math.Atan2(dy, dx) * 180.0 / Math.PI;         // direction pin → free end
            if (pinB) a += 180.0;                                    // the ruler's own direction runs A → B
            a = NormalizeAngle(SnapAngle(a, shift));
            double r = a * Math.PI / 180.0;
            float len = _rulerGrabLen, ox = (float)Math.Cos(r) * len, oy = (float)Math.Sin(r) * len;
            if (pinB) RulerSetEnds(new PointF(p.X - ox, p.Y - oy), p);
            else      RulerSetEnds(p, new PointF(p.X + ox, p.Y + oy));
            _rulerAngle = (float)a;                                   // keep the snapped angle exactly
        }

        // Drag of an end handle: the opposite end stays put, the dragged one slides along the ruler.
        private void RulerStretch(bool endB, PointF q, float per)
        {
            PointF f = RulerEnd(!endB);
            double a = _rulerAngle * Math.PI / 180.0;
            float dx = (float)Math.Cos(a) * (endB ? 1 : -1), dy = (float)Math.Sin(a) * (endB ? 1 : -1);
            float len = (q.X - f.X) * dx + (q.Y - f.Y) * dy + _rulerGrabOff;
            // Settles on a round number of size-slider px (every 25) when it passes close to one.
            float step = 25f * RulerUnit, near = (float)Math.Round(len / step) * step;
            if (Math.Abs(len - near) <= 5f * per) len = near;
            len = Math.Max(RulerMinLen, Math.Min(RulerMaxLen, len));
            _rulerLen = len;
            _rulerC = new PointF(f.X + dx * len / 2f, f.Y + dy * len / 2f);
        }

        private bool RulerMouseUp(MouseEventArgs e)
        {
            if (_rulerDrag == RulerDrag.None || e.Button != MouseButtons.Left) return false;
            _rulerDrag = RulerDrag.None;
            if (_rulerRetex) { _rulerRetex = false; RulerRetexture(); }
            Redraw();
            return true;
        }

        // The ruler settles on the axes and diagonals when it passes close to them.
        private static double MagnetTo45(double a)
        {
            double n = Math.Round(a / 45.0) * 45.0;
            return Math.Abs(a - n) <= 2.5 ? n : a;
        }

        private static double NormalizeAngle(double a)
        {
            a %= 360.0;
            if (a > 180.0) a -= 360.0;
            if (a <= -180.0) a += 360.0;
            return a;
        }

        // Hover: the move / turn cursor over the ruler; the tool's own cursor when it leaves.
        private void UpdateRulerCursor(Point pt)
        {
            if (_gl == null) return;
            var hit = _rulerOn && _tool == ToolMode.Paint && _live == null ? RulerHit(pt) : RulerDrag.None;
            bool over = hit != RulerDrag.None;
            if (over) _gl.Cursor = RulerCursor(hit);
            else if (_overRuler) _gl.Cursor = ToolCursor(_tool);
            _overRuler = over;
        }

        private Cursor RulerCursor(RulerDrag hit)
        {
            switch (hit)
            {
                case RulerDrag.Body: return Cursors.No;   // crossed out: nothing is drawn on the ruler
                case RulerDrag.Move: return _rulerPinA && _rulerPinB ? Cursors.No : Cursors.SizeAll;
                case RulerDrag.KnobA: return _rulerPinA ? Cursors.No : Cursors.Hand;
                case RulerDrag.KnobB: return _rulerPinB ? Cursors.No : Cursors.Hand;
                case RulerDrag.StretchA: case RulerDrag.StretchB:
                    if ((hit == RulerDrag.StretchA && _rulerPinA) || (hit == RulerDrag.StretchB && _rulerPinB)) return Cursors.No;
                    var g = RulerScreen();   // the arrow follows the ruler's direction on screen
                    double deg = Math.Atan2(g.EndB.Y - g.EndA.Y, g.EndB.X - g.EndA.X) * 180.0 / Math.PI;
                    deg = ((deg % 180.0) + 180.0) % 180.0;
                    if (deg < 22.5 || deg >= 157.5) return Cursors.SizeWE;
                    if (deg < 67.5) return Cursors.SizeNWSE;
                    if (deg < 112.5) return Cursors.SizeNS;
                    return Cursors.SizeNESW;
                default: return Cursors.Hand;
            }
        }

        // ── Drawing along it ──────────────────────────────────────────
        private void RulerBeginStroke()
        {
            _rulerEdge = -1;
            _rulerEdgeDecided = false;
            _rulerStrokeGap = 0f;
        }

        // A stroke point on the sheet (layer px) → where the pen really is: on the ruler's edge
        // while the stroke follows it.
        private PointF RulerConstrain(PointF layerPt, SceneObject canvas)
        {
            if (!_rulerOn || !ReferenceEquals(canvas, _rulerCanvas) || !RulerFacesCamera()) return layerPt;
            RulerSync();
            if (!_rulerOnSheet) return layerPt;
            float k = RulerK();
            var sh = new PointF(layerPt.X, layerPt.Y * k);
            float per = Math.Max(1e-3f, SheetPerScreenPx(_lastMouse));
            double a = _rulerAngle * Math.PI / 180.0;
            var dir = new PointF((float)Math.Cos(a), (float)Math.Sin(a));
            var nrm = new PointF(-dir.Y, dir.X);

            if (!_rulerEdgeDecided)
            {
                _rulerEdgeDecided = true;
                // A round brush in texture pixels is an ellipse in square sheet pixels.
                // Its support along the ruler normal keeps the entire dab outside the body.
                float radius = PaintKind == BrushKind.Pen ? Math.Max(1f, _strokeRadius / 2f) : _strokeRadius;
                float gap = radius * (float)Math.Sqrt(nrm.X * nrm.X + k * k * nrm.Y * nrm.Y);
                float best = RulerSnapPx * per + gap;
                for (int e = 0; e < 2; e++)
                {
                    PointF on = RulerLocal(0, e == 0 ? -_rulerThick / 2f : _rulerThick / 2f);
                    float d = Math.Abs((sh.X - on.X) * nrm.X + (sh.Y - on.Y) * nrm.Y);
                    if (d <= best) { best = d; _rulerEdge = e; }
                }
                _rulerStrokeGap = gap;
            }
            if (_rulerEdge < 0) return layerPt;

            PointF edge = RulerLocal(0, _rulerEdge == 0 ? -_rulerThick / 2f : _rulerThick / 2f);
            float off = (sh.X - edge.X) * nrm.X + (sh.Y - edge.Y) * nrm.Y;
            if (Math.Abs(off) > RulerReleasePx * per + _rulerStrokeGap) { _rulerEdge = -1; return layerPt; }
            float outside = _rulerEdge == 0 ? -_rulerStrokeGap : _rulerStrokeGap;
            return new PointF(sh.X + nrm.X * (outside - off), (sh.Y + nrm.Y * (outside - off)) / k);
        }

        // ── The ruler as an object ─────────────────────────────────────
        // The sheet's front normal (world).
        private Vec3 RulerNormal()
        {
            if (!CanvasCorners(_rulerCanvas, out Vec3 tl, out Vec3 tr, out Vec3 br, out Vec3 bl)) return new Vec3(0f, 0f, 1f);
            return Vec3.Cross(br - bl, tl - bl).Normalized();
        }

        // World size of one sheet pixel (the sheet's pixels are square).
        private float RulerWorldPerPx()
        {
            var layer = PaintLayerFor(_rulerCanvas);
            return CanvasCorners(_rulerCanvas, out Vec3 tl, out Vec3 tr, out Vec3 _, out Vec3 _) ? (tr - tl).Length / layer.Width : 0.002f;
        }

        // A world point → sheet px (the inverse of SheetToWorld; along the sheet's normal it is dropped).
        private PointF WorldToSheet(Vec3 w, float k)
        {
            return WithRulerCanvas(() =>
            {
                var layer = PaintLayerFor(_liveCanvas);
                if (!CanvasCorners(_liveCanvas, out Vec3 tl, out Vec3 tr, out Vec3 _, out Vec3 bl)) return PointF.Empty;
                Vec3 e1 = tr - tl, e2 = bl - tl;
                float u = Vec3.Dot(w - tl, e1) / Math.Max(1e-9f, Vec3.Dot(e1, e1));
                float v = Vec3.Dot(w - tl, e2) / Math.Max(1e-9f, Vec3.Dot(e2, e2));
                return new PointF(u * layer.Width, v * k * layer.Height);
            });
        }

        private static string RulerSignature(SceneObject plate)
        {
            return plate.Position.X.ToString("R") + "," + plate.Position.Y + "," + plate.Position.Z + "|" +
                   plate.Rotation.X + "," + plate.Rotation.Y + "," + plate.Rotation.Z + "|" + plate.Scale.X;
        }

        // Puts a new ruler on the sheet where PlaceRuler left its parameters.
        private void CreateRulerObject()
        {
            if (_scene == null || _rulerCanvas == null) return;
            float k = RulerK(), wpp = RulerWorldPerPx();
            CanvasCorners(_rulerCanvas, out Vec3 tl, out Vec3 tr, out Vec3 br, out Vec3 bl);
            Vec3 yAxis = (tl - bl).Normalized();
            float unit = RulerUnit;
            Vec3 centre = SheetToWorld(_rulerC, k);
            using (var face = BuildRulerTexture(_rulerLen, _rulerThick, false, unit))
            using (var back = BuildRulerTexture(_rulerLen, _rulerThick, true, unit))
            {
                var grp = RulerFactory.Create(centre, _rulerCanvas.Rotation, RulerNormal(), yAxis,
                                              _rulerLen * wpp, _rulerThick * wpp, face, back);
                _scene.Objects.Add(grp);
                _rulerSig = RulerSignature(grp.Children[0]);
            }
        }

        // The picture is tied to the length (the scale counts in slider px), so it is redrawn after a stretch.
        private void RulerRetexture()
        {
            var grp = RulerObject();
            if (grp == null) return;
            using (var face = BuildRulerTexture(_rulerLen, _rulerThick, false, RulerUnit))
            using (var back = BuildRulerTexture(_rulerLen, _rulerThick, true, RulerUnit))
            {
                RulerFactory.SetPicture(grp.Children[0], face);
                RulerFactory.SetPicture(grp.Children[1], back);
            }
        }

        // Reads the ruler's place on the sheet off the object, if the object was changed since.
        private void RulerSync()
        {
            var grp = RulerObject();
            if (grp == null) return;
            if (_rulerCanvas == null || !ReferenceEquals(FindRulerCanvas(), _rulerCanvas))
            {
                _rulerCanvas = FindRulerCanvas();
                if (_rulerCanvas == null) return;
                _rulerPlaced = true;
            }
            if (_rulerDrag != RulerDrag.None) return;
            var plate = grp.Children[0];
            string sig = RulerSignature(plate);
            if (sig == _rulerSig) return;
            _rulerSig = sig;
            RulerReadParams(plate, out PointF c, out float angle, out float len);
            _rulerC = c; _rulerLen = len;
            if (len > 1e-3f) _rulerAngle = angle;
            // Picked up with the 3D tools it stops guiding the pen: only a ruler lying on the sheet does.
            var xf = SceneGeometry.Xform.For(plate);
            Vec3 n = RulerNormal(), up = xf.ApplyNormal(new Vec3(0f, 0f, 1f));
            float height = Vec3.Dot(xf.Apply(Vec3.Zero) - SheetToWorld(c, RulerK()), n);
            _rulerOnSheet = Math.Abs(Vec3.Dot(up, n)) > 0.996f && Math.Abs(height) < RulerFactory.Depth + 0.03f;
        }

        private void RulerReadParams(SceneObject plate, out PointF c, out float angle, out float len)
        {
            float k = RulerK();
            var xf = SceneGeometry.Xform.For(plate);
            PointF a = WorldToSheet(xf.Apply(new Vec3(-0.5f, 0f, 0f)), k), b = WorldToSheet(xf.Apply(new Vec3(0.5f, 0f, 0f)), k);
            c = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            len = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            angle = (float)NormalizeAngle(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI);
        }

        // Carries the handles' result over to the object: stretch, turn about its centre, then move.
        private void RulerApply()
        {
            var grp = RulerObject();
            if (grp == null || _rulerCanvas == null) return;
            float k = RulerK();
            RulerReadParams(grp.Children[0], out PointF c0, out float a0, out float l0);

            if (l0 > 1e-3f && Math.Abs(_rulerLen - l0) > 1e-3f)
            {
                float f = _rulerLen / l0;
                foreach (var plate in grp.Children) plate.Scale = new Vec3(plate.Scale.X * f, plate.Scale.Y, plate.Scale.Z);
            }

            float da = (float)NormalizeAngle(_rulerAngle - a0);
            if (Math.Abs(da) > 1e-3f)
            {
                Vec3 w0 = SheetToWorld(c0, k), n = RulerNormal();
                double r0 = a0 * Math.PI / 180.0, r1 = _rulerAngle * Math.PI / 180.0;
                Vec3 oldAxis = SheetToWorld(new PointF(c0.X + (float)Math.Cos(r0), c0.Y + (float)Math.Sin(r0)), k) - w0;
                Vec3 newAxis = SheetToWorld(new PointF(c0.X + (float)Math.Cos(r1), c0.Y + (float)Math.Sin(r1)), k) - w0;
                float deg = (float)(Math.Atan2(Vec3.Dot(n, Vec3.Cross(oldAxis, newAxis)), Vec3.Dot(oldAxis, newAxis)) * 180.0 / Math.PI);
                SceneGeometry.RotateAroundAxis(grp, w0, n, deg);
            }

            Vec3 d = SheetToWorld(_rulerC, k) - SheetToWorld(c0, k);
            foreach (var plate in grp.Children) plate.Position = plate.Position + d;
            grp.Position = grp.Position + d;
            _rulerSig = RulerSignature(grp.Children[0]);
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
        }

        // ── Drawing the ruler ─────────────────────────────────────────
        // A point of the sheet (sheet px) in world space.
        private Vec3 SheetToWorld(PointF sh, float k)
        {
            return WithRulerCanvas(() =>
            {
                var layer = PaintLayerFor(_liveCanvas);
                if (!CanvasCorners(_liveCanvas, out Vec3 tl, out Vec3 tr, out Vec3 _, out Vec3 bl)) return Vec3.Zero;
                float u = sh.X / layer.Width, v = sh.Y / k / layer.Height;
                return tl + (tr - tl) * u + (bl - tl) * v;
            });
        }

        // Is something nearer to the eye than this world point (a shape in front of the sheet)? Read from
        // the depth buffer the scene has just filled; the matrices are still the scene's own.
        private bool Covered(Vec3 w)
        {
            var mv = new double[16]; var pr = new double[16];
            GL.GetDouble(GetPName.ModelviewMatrix, mv);
            GL.GetDouble(GetPName.ProjectionMatrix, pr);
            double[] e = { w.X, w.Y, w.Z, 1.0 }, c = new double[4];
            for (int r = 0; r < 4; r++) { double v = 0; for (int i = 0; i < 4; i++) v += mv[i * 4 + r] * e[i]; c[r] = v; }
            double[] q = new double[4];
            for (int r = 0; r < 4; r++) { double v = 0; for (int i = 0; i < 4; i++) v += pr[i * 4 + r] * c[i]; q[r] = v; }
            if (Math.Abs(q[3]) < 1e-9) return false;
            double nx = q[0] / q[3], ny = q[1] / q[3], nz = q[2] / q[3];
            if (Math.Abs(nx) > 1 || Math.Abs(ny) > 1 || nz < -1 || nz > 1) return false;
            int px = (int)((nx + 1) * 0.5 * W), py = (int)((ny + 1) * 0.5 * H);
            var d = new float[1];
            GL.ReadPixels(px, py, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.Float, d);
            return d[0] < (nz + 1) * 0.5 - 0.0015;
        }

        private void DrawRulerGL()
        {
            // The body is a scene object, drawn with the scene; what is left here is the hand's gear —
            // outline, grips, knobs and pins — only while a drawing tool is in use.
            if (!_rulerOn || _tool != ToolMode.Paint || _rulerCanvas == null || !ReferenceEquals(FindRulerCanvas(), _rulerCanvas)) return;
            RulerSync();
            if (!_rulerOnSheet) return;
            float k = RulerK(), hl = _rulerLen / 2f;
            var g = RulerScreen();
            if (float.IsNaN(g.Centre.X) || g.Centre.X < -9000) return;

            // Which parts are visible (not behind a shape): the middle and the two ends, a hair above the plate.
            Vec3 lift = RulerNormal() * (RulerFactory.Lift + RulerFactory.Depth / 2f + 0.006f);
            bool seeC = !Covered(SheetToWorld(_rulerC, k) + lift),
                 seeA = !Covered(SheetToWorld(RulerLocal(-hl, 0), k) + lift),
                 seeB = !Covered(SheetToWorld(RulerLocal(hl, 0), k) + lift);
            bool dragging = _rulerDrag != RulerDrag.None;
            if (!dragging && !seeC && !seeA && !seeB) return;

            BeginScreenOverlay();
            GL.Enable(EnableCap.LineSmooth);

            // Outline: the accent while it is being handled.
            Color acc = Aplat.UI.Theme.Accent;
            bool active = _rulerDrag != RulerDrag.None;
            GL.LineWidth(active ? 1.75f : 1f);
            if (active) GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 1f); else GL.Color4(0.25f, 0.28f, 0.33f, 0.55f);
            GL.Begin(PrimitiveType.LineLoop); foreach (var p in g.Corners) GL.Vertex2(p.X, p.Y); GL.End();

            // Both ends: a grip across the short edge (drag to stretch), the pin mark on the end point
            // (double click to fix it) and, for a free end, the round knob that turns the ruler.
            for (int e = 0; e < 2; e++)
            {
                bool b = e == 1, pinned = b ? _rulerPinB : _rulerPinA;
                if (!dragging && !(b ? seeB : seeA)) continue;
                PointF kn = b ? g.KnobB : g.KnobA, end = b ? g.EndB : g.EndA;
                PointF c0 = g.Corners[b ? 1 : 0], c1 = g.Corners[b ? 2 : 3];
                DrawRulerGrip(c0, c1, g.Centre, pinned, acc);
                if (!pinned)
                {
                    GL.Color4(1f, 1f, 1f, 1f);
                    Circle(PrimitiveType.TriangleFan, kn.X, kn.Y, RulerKnobPx - 1.5f, 28);
                    GL.LineWidth(1.5f);
                    GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 1f);
                    Circle(PrimitiveType.LineLoop, kn.X, kn.Y, RulerKnobPx - 1.5f, 28);
                    Circle(PrimitiveType.TriangleFan, kn.X, kn.Y, 2.6f, 14);
                }
                DrawRulerPin(end, pinned, acc);
            }

            // The angle while it turns, the length while it stretches.
            if (_rulerDrag == RulerDrag.KnobA || _rulerDrag == RulerDrag.KnobB)
                DrawRulerLabel(g, RulerAngleText());
            else if ((_rulerDrag == RulerDrag.StretchA || _rulerDrag == RulerDrag.StretchB) && _rulerMoved)
                DrawRulerLabel(g, Math.Round(_rulerLen / RulerUnit).ToString(System.Globalization.CultureInfo.InvariantCulture) + " px");
            else if (_rulerMoved && _rulerPinA != _rulerPinB && _rulerDrag != RulerDrag.None && _rulerDrag != RulerDrag.Blocked)
                DrawRulerLabel(g, RulerAngleText());

            GL.Disable(EnableCap.LineSmooth);
            GL.LineWidth(1f);
            EndScreenOverlay();
        }

        private string RulerAngleText()
        {
            double deg = _rulerAngle;
            if (deg > 90) deg -= 180; else if (deg <= -90) deg += 180;   // the ruler has no direction
            return (-deg).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";
        }

        // The end grip: a short bar across the ruler a few px inside its end — the visible
        // "drag me to stretch"; a pinned end shows it in the accent colour, locked.
        private void DrawRulerGrip(PointF c0, PointF c1, PointF centre, bool pinned, Color acc)
        {
            PointF m = new PointF((c0.X + c1.X) / 2f, (c0.Y + c1.Y) / 2f);
            float ex = c1.X - c0.X, ey = c1.Y - c0.Y, el = (float)Math.Sqrt(ex * ex + ey * ey);
            float ix = centre.X - m.X, iy = centre.Y - m.Y, il = (float)Math.Sqrt(ix * ix + iy * iy);
            if (el < 1f || il < 1f) return;
            ix /= il; iy /= il; ex /= el; ey /= el;
            float half = Math.Min(el * 0.22f, 22f);
            for (int i = 0; i < 2; i++)
            {
                float d = 5f + i * 4f;
                PointF c = new PointF(m.X + ix * d, m.Y + iy * d);
                if (pinned) GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 0.9f);
                else GL.Color4(0.25f, 0.28f, 0.33f, 0.55f);
                GL.LineWidth(1.5f);
                GL.Begin(PrimitiveType.Lines);
                GL.Vertex2(c.X - ex * half, c.Y - ey * half); GL.Vertex2(c.X + ex * half, c.Y + ey * half);
                GL.End();
            }
        }

        // A hollow ring on a free end; a solid disc with a white dot on a pinned one.
        private void DrawRulerPin(PointF p, bool pinned, Color acc)
        {
            if (pinned)
            {
                GL.Color4(1f, 1f, 1f, 1f);
                Circle(PrimitiveType.TriangleFan, p.X, p.Y, 8.5f, 24);
                GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 1f);
                Circle(PrimitiveType.TriangleFan, p.X, p.Y, 7f, 24);
                GL.Color4(1f, 1f, 1f, 1f);
                Circle(PrimitiveType.TriangleFan, p.X, p.Y, 2.4f, 12);
            }
            else
            {
                GL.Color4(1f, 1f, 1f, 0.9f);
                Circle(PrimitiveType.TriangleFan, p.X, p.Y, 4.5f, 18);
                GL.LineWidth(1.25f);
                GL.Color4(acc.R / 255f, acc.G / 255f, acc.B / 255f, 0.9f);
                Circle(PrimitiveType.LineLoop, p.X, p.Y, 4.5f, 18);
            }
        }

        private void DrawRulerLabel(RulerGeom g, string text)
        {
            if (_rulerLabelKey != text)
            {
                if (_rulerLabelTex != 0) GL.DeleteTexture(_rulerLabelTex);
                using (var bmp = BuildAngleLabel(text)) _rulerLabelTex = UploadRgba(bmp, false);
                _rulerLabelKey = text;
            }
            PointF c = g.Centre;
            const float w = 76f, h = 28f;
            float x = c.X - w / 2f, y = c.Y - h / 2f;
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, _rulerLabelTex);
            GL.Color4(1f, 1f, 1f, 1f);
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0, 0); GL.Vertex2(x, y);     GL.TexCoord2(1, 0); GL.Vertex2(x + w, y);
            GL.TexCoord2(1, 1); GL.Vertex2(x + w, y + h); GL.TexCoord2(0, 1); GL.Vertex2(x, y + h);
            GL.End();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Disable(EnableCap.Texture2D);
        }

        // ── Textures ──────────────────────────────────────────────────
        private const int RulerTexW = 1024, RulerTexH = 128;

        // Lattice value noise in 0..1: the grain of the wood is this, stretched along the ruler.
        private static float WoodNoise(float x, float y, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = WoodHash(xi, yi, seed), b = WoodHash(xi + 1, yi, seed);
            float c = WoodHash(xi, yi + 1, seed), d = WoodHash(xi + 1, yi + 1, seed);
            return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
        }

        private static float WoodHash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        // A bar of beech (the back, `back`, is a darker stain): a warm gradient with long fibres, a bright top lip, a soft lower shade and
        // rounded corners (the corners are transparent). The scale or the engraving is drawn over it.
        private static Bitmap BuildWood(bool back)
        {
            var bmp = new Bitmap(RulerTexW, RulerTexH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = RoundRect(new RectangleF(1, 1, RulerTexW - 2, RulerTexH - 2), 12))
                {
                    g.SetClip(shape);
                    using (var br = new LinearGradientBrush(new Rectangle(0, 0, RulerTexW, RulerTexH),
                        back ? Color.FromArgb(255, 184, 124, 78) : Color.FromArgb(255, 226, 182, 128),
                        back ? Color.FromArgb(255, 164, 106, 64) : Color.FromArgb(255, 208, 160, 104), 90f))
                        g.FillRectangle(br, 0, 0, RulerTexW, RulerTexH);
                }
            }
            var data = bmp.LockBits(new Rectangle(0, 0, RulerTexW, RulerTexH), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[RulerTexW];
                for (int y = 0; y < RulerTexH; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, RulerTexW);
                    for (int x = 0; x < RulerTexW; x++)
                    {
                        uint p = (uint)row[x];
                        if ((p >> 24) == 0) continue;
                        // long fibres (stretched along x), a slow drift of tone, and a few darker pores
                        float fibre = WoodNoise(x * 0.012f, y * 0.45f, 3) * 0.6f + WoodNoise(x * 0.03f, y * 1.3f, 7) * 0.4f;
                        float drift = WoodNoise(x * 0.0025f, y * 0.05f, 11);
                        float pore  = WoodNoise(x * 0.09f, y * 2.2f, 5) > 0.86f ? 0.07f : 0f;
                        float v = (fibre - 0.5f) * 0.20f + (drift - 0.5f) * 0.10f - pore;
                        int r = Math.Max(0, Math.Min(255, (int)(((p >> 16) & 255) * (1f + v))));
                        int gg = Math.Max(0, Math.Min(255, (int)(((p >> 8) & 255) * (1f + v))));
                        int b = Math.Max(0, Math.Min(255, (int)((p & 255) * (1f + v * 1.1f))));
                        row[x] = (int)((p & 0xFF000000u) | (uint)(r << 16) | (uint)(gg << 8) | (uint)b);
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), RulerTexW);
                }
            }
            finally { bmp.UnlockBits(data); }
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = RoundRect(new RectangleF(1, 1, RulerTexW - 2, RulerTexH - 2), 12))
                {
                    g.SetClip(shape);
                    using (var pen = new Pen(Color.FromArgb(110, 255, 244, 220), 3f)) g.DrawLine(pen, 0, 2f, RulerTexW, 2f);
                    using (var pen = new Pen(Color.FromArgb(80, 90, 52, 20), 4f)) g.DrawLine(pen, 0, RulerTexH - 2f, RulerTexW, RulerTexH - 2f);
                    g.ResetClip();
                    using (var pen = new Pen(Color.FromArgb(120, 120, 78, 36), 1.5f)) g.DrawPath(pen, shape);
                }
            }
            return bmp;
        }

        // The ruler's face: wood with a printed scale along the top (a tick every 25 slider px, taller at
        // 50, longest at 100 and numbered) and a short second reference along the lower edge.
        internal static Bitmap BuildRulerTexture(float len, float thick, bool back, float unit = 1f)
        {
            var bmp = BuildWood(back);
            Color ink = Color.FromArgb(235, 28, 22, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                // Texture px per sheet px; the ruler's thickness is what the 128 rows stand for.
                // (Ticks on 20 and 50 together bunch up: 40-50-60 look like a cluster.)
                float perSheet = RulerTexW / Math.Max(1f, len);
                using (var pen = new Pen(ink, 2.2f))
                using (var font = new Font(FontFamily.GenericSansSerif, 24f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(ink))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center };
                    // `unit` = sheet px per size-slider px, so the numbers read in the slider's px.
                    int steps = (int)(len / unit / 25f);
                    for (int i = 0; i <= steps; i++)
                    {
                        int at = i * 25;                       // slider px
                        bool hundred = at % 100 == 0, half = at % 50 == 0;
                        float x = at * unit * perSheet;
                        float h = hundred ? 40f : half ? 30f : 20f;
                        g.DrawLine(pen, x, 6f, x, 6f + h);
                        if (hundred && at > 0 && x < RulerTexW - 24)
                            g.DrawString((at / 100).ToString(), font, tb, x, 52f, fmt);
                    }
                    // The lower edge is a ruler too: the same scale, counted up from the bottom.
                    for (int i = 0; i <= steps; i++)
                    {
                        int at = i * 25;
                        bool hundred = at % 100 == 0, half = at % 50 == 0;
                        float x = at * unit * perSheet, h = hundred ? 40f : half ? 30f : 20f;
                        g.DrawLine(pen, x, RulerTexH - 6f, x, RulerTexH - 6f - h);
                    }
                }
            }
            return bmp;
        }

        private static Bitmap BuildAngleLabel(string text)
        {
            var bmp = new Bitmap(128, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (var path = RoundRect(new RectangleF(2, 2, 124, 60), 26))
                using (var br = new SolidBrush(Color.FromArgb(225, 30, 34, 42)))
                    g.FillPath(br, path);
                using (var font = new Font(FontFamily.GenericSansSerif, 30f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(Color.White))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, br, new RectangleF(0, 0, 128, 64), fmt);
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundRect(RectangleF r, float d)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // A plain RGBA texture with a linear filter (power-of-two sizes only: the fixed-function
        // generic Windows renderer is GL 1.1).
        private static int UploadRgba(Bitmap bmp, bool repeat)
        {
            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            int wrap = (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return id;
        }
    }
}
