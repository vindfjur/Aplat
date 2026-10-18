using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    public enum ToolMode { Select, Move, Rotate, Scale, Paint, Hand, EditShape }
    public enum BrushKind { Brush, Pencil, Pen, Marker, Eyedropper, Fill, Eraser, Text2D, Select2D, ShapeLine, ShapeRect, ShapeEllipse, ShapeTriangle, ObjectEraser, Lasso }

    // Live viewport. Rendering runs on the GPU through an OpenTK GLControl child
    // (real depth buffer, perspective-correct textures, fast). All the editing
    // logic — picking, gizmos, fly camera, nudging, projection paint — lives here.
    public partial class SceneView : UserControl
    {
        // ── State ─────────────────────────────────────────────────
        private readonly Camera _camera   = new Camera();
        private SceneGeometry   _renderer;          // live render settings + geometry helpers
        private GLRenderer      _glr;               // live GPU renderer
        private GLControl       _gl;
        private bool            _glLoaded;

        private Scene           _scene;
        private readonly List<SceneObject> _selected = new List<SceneObject>();
        private ToolMode        _tool     = ToolMode.Select;

        // ── Auto-rotation ("turntable") ───────────────────────────
        private bool  _spin;              // enabled
        private float _spinSpeed = 24f;   // degrees per second
        private bool  _spinObjects;       // true → the selected object spins, otherwise the camera
        private float _spinNotify;        // accumulator, so the event is not raised every frame
        private bool  _spinPaused;        // rotation is on but held (the on-canvas pause button)
        private float _spinStartAngle;    // camera angle when the rotation was switched on (for reset)
        private bool _spinWhole;         // the last object spin turned the whole scene about _spinPivot
        private Vec3 _spinPivot;
        private readonly Dictionary<SceneObject, float> _spinTurned = new Dictionary<SceneObject, float>();   // degrees each object has been turned

        private Point  _lastMouse;
        private bool   _orbitDrag;
        private Point  _rightDownPt;
        private bool   _panDrag;
        private bool   _transformDrag;
        private bool   _undoFired;
        private Vec3   _pivot = Vec3.Zero;
        private Vec3   _rotAxisWorld = Vec3.Zero;   // world-space axis locked when a rotate drag starts
        private float  _rotAccum;                   // Ctrl-rotate: raw degrees pending the next 15° step

        // ── Smooth (UE5-style) keyboard fly ───────────────────────
        private readonly HashSet<Keys> _keysDown = new HashSet<Keys>();
        private readonly Timer     _flyTimer = new Timer();
        private readonly Stopwatch _flyClock = new Stopwatch();
        private Vec3  _camVel   = Vec3.Zero;
        private float _flySpeed = 1f;        // UE5-style: scrolled while flying

        private enum GizmoAxis { None, X, Y, Z }
        private GizmoAxis _activeAxis        = GizmoAxis.None;
        private GizmoAxis _hoverAxis         = GizmoAxis.None;
        private PointF    _gizmoCenterScreen = PointF.Empty;

        private const float GizmoLen = 0.65f;   // world units — arm length
        private const float RingR    = 0.55f;   // world units — ring radius
        private const float HitTip   = 14f;     // px — tip hit radius
        private const float HitShaft = 7f;      // px — shaft hit tolerance
        private const float HitRing  = 11f;     // px — ring hit tolerance

        // ── Brush (projection paint) ──────────────────────────────
        // Colour, opacity and size also restyle the live element (shape / text).
        private Color _brushColor = Color.FromArgb(220, 38, 38);   // the palette red
        public Color BrushColor
        {
            get { return _brushColor; }
            set
            {
                _brushColor = value;
                if (_live != null && _live.Kind != LiveKind.Image)   // a cut-out keeps its pixels
                    EditLive(i => i.Color = Color.FromArgb(i.Color.A, value));
            }
        }
        private int _brushRadius = 28;
        public int BrushRadius
        {
            get { return _brushRadius; }
            set
            {
                if (_brushRadius == value) return;
                _brushRadius = value;
                if (_live != null && _live.IsShape)
                    EditLive(i => i.StrokeWidth = LivePenWidth(PaintLayerFor(_liveCanvas)));
                Redraw();   // the brush ring follows
            }
        }
        private BrushKind _paintKind = BrushKind.Brush;
        public BrushKind PaintKind
        {
            get { return _paintKind; }
            set
            {
                _paintKind = value;
                if (_gl != null && _tool == ToolMode.Paint) _gl.Cursor = ToolCursor(_tool);   // I-beam for text
            }
        }
        private int _brushOpacity = 100;   // 0–100
        // How different a colour may be from the one under the cursor and still be filled, in percent (the
        // fill tool's strength): 0 takes only the identical colour, 100 floods everything.
        public int FillStrength { get; set; } = 19;

        public int BrushOpacity
        {
            get { return _brushOpacity; }
            set
            {
                _brushOpacity = value;
                if (_live != null && _live.Kind != LiveKind.Image)
                    EditLive(i => i.Color = Color.FromArgb(Math.Max(0, Math.Min(255, value * 255 / 100)), i.Color));
            }
        }
        private bool  _painting;
        private Point _lastPaintPt;
        // Canvas stroke continuity: the previous UV point of the current stroke, so
        // the 2D brushes draw solid connected lines instead of a chain of dabs.
        private PointF? _strokeUv;
        private Bitmap  _strokeLayer;

        // Canvas stroke engine: the layer as it was before the stroke, plus the
        // stroke's points (texture px). Every mouse move re-renders the whole stroke
        // as ONE smoothed path over the snapshot — translucency never stacks at
        // segment joints (no "beads" along the edge) and corners come out as curves.
        //
        // The snapshot is lazy: a reusable bitmap filled tile by tile (128px) just
        // before a tile is first drawn over, so starting a stroke costs nothing even
        // on a 2048px sheet — only the tiles the stroke actually reaches are copied.
        private const int StrokeTile = 128;
        private Bitmap _strokeBase;
        private bool[,] _strokeTiles;
        private readonly List<PointF> _strokePts = new List<PointF>();
        private bool _strokeOnCanvas;
        private int  _strokeSeed;
        // What the stroke in progress is, so it can be journalled when it ends
        // (see InkJournal): the same numbers drive the live drawing and the replay.
        private Material    _strokeMaterial;
        private StrokeStyle  _strokeStyle;
        private int          _strokeArgb, _strokeOpacity, _strokeRadius;

        // Text, cut-out regions and shapes are live elements — see SceneView.Live.cs.
        private SceneObject _selCanvas;        // canvas of the rect-select rubber band

        // Rect-selection rubber band (before the region becomes a fragment).
        private bool   _selDragging;
        private PointF _selStartUv, _selCurUv;
        private Bitmap _selBaseLayer;

        // ── Public surface ────────────────────────────────────────
        public event EventHandler<SceneObject> ObjectSelected;
        public event EventHandler              ObjectTransformed;
        public event EventHandler              TransformStarted;
        public event EventHandler<string>      Notify;          // transient status text
        public event EventHandler<Color>       BrushColorPicked; // eyedropper result
        public event Action<Point>             ContextMenuRequested; // right-click (no orbit drag)
        public event EventHandler              DuplicateForDrag;     // Alt+drag a gizmo → clone first
        public event EventHandler              ViewChanged;
        public event EventHandler<string[]>    FilesDropped;        // files dragged onto the viewport
        public event EventHandler<int>         BrushWheel;          // Alt+wheel → brush size delta

        public Scene Scene
        {
            get { return _scene; }
            set { DiscardLive(); _scene = value; Redraw(); }
        }

        public Camera Camera { get { return _camera; } }

        public float FlySpeed { get { return _flySpeed; } }

        public bool ShowGuides { get; set; }

        // Scene auto-rotation. In camera mode it moves the orbit smoothly around the
        // target (perspective only — orthographic views fix the angle themselves); in
        // object mode it spins the selection about its centre.
        public bool AutoRotate
        {
            get { return _spin; }
            set
            {
                if (value && !_spin)
                {
                    _spinPaused = false;
                    _spinStartAngle = _camera.AngleY;
                    _spinTurned.Clear();
                }
                _spin = value;
                _spinNotify = 0f;
            }
        }

        // Holds the rotation without switching it off (the on-canvas pause / resume button).
        public bool AutoRotatePaused
        {
            get { return _spinPaused; }
            set { _spinPaused = value; _spinNotify = 0f; }
        }

        // Back to where the rotation started: the camera returns to its starting angle, spun objects are
        // turned back by the degrees they have travelled.
        public void ResetAutoRotate()
        {
            if (_spinObjects)
            {
                foreach (var pair in _spinTurned)
                    if (!IsLocked(pair.Key) && Math.Abs(pair.Value) > 1e-4f)
                        SceneGeometry.RotateAroundAxis(pair.Key, _spinWhole ? _spinPivot : SceneGeometry.Center(pair.Key), new Vec3(0f, 1f, 0f), -pair.Value);
                _spinTurned.Clear();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _camera.AngleY = _spinStartAngle;
                NotifyViewChanged();
            }
            Redraw();
        }

        public float AutoRotateSpeed
        {
            get { return _spinSpeed; }
            set { _spinSpeed = value; }
        }

        public bool AutoRotateObjects
        {
            get { return _spinObjects; }
            set { _spinObjects = value; }
        }

        public bool ShowWireframe
        {
            get { return _renderer.ShowWireframe; }
            set { _renderer.ShowWireframe = value; Redraw(); }
        }

        public bool OutlineSelection
        {
            get { return _renderer.OutlineSelection; }
            set { _renderer.OutlineSelection = value; Redraw(); }
        }

        public bool ShowVertices
        {
            get { return _renderer.ShowVertices; }
            set { _renderer.ShowVertices = value; Redraw(); }
        }

        public float Brightness
        {
            get { return _renderer.Brightness; }
            set { _renderer.Brightness = value; Redraw(); }
        }

        // ── Canvas / grid surface ─────────────────────────────────
        public bool ShowGrid
        {
            get { return _renderer.ShowGrid; }
            set { _renderer.ShowGrid = value; Redraw(); }
        }

        public bool FixedGrid
        {
            get { return _renderer.FixedGrid; }
            set { _renderer.FixedGrid = value; Redraw(); }
        }

        public float GridExtent
        {
            get { return _renderer.GridExtent; }
            set { _renderer.GridExtent = value; Redraw(); }
        }

        public float GridSpacing
        {
            get { return _renderer.GridSpacing; }
            set { _renderer.GridSpacing = value; Redraw(); }
        }

        public Color BackgroundColor
        {
            get { return _renderer.BackgroundColor; }
            set { _renderer.BackgroundColor = value; Redraw(); }
        }

        public Color GridColor
        {
            get { return _renderer.GridColor; }
            set { _renderer.GridColor = value; Redraw(); }
        }

        public float SnapGrid    { get; set; } = 0.5f;

        // Snapping is momentary: hold Ctrl while moving/nudging to snap to the grid.
        private static bool SnapHeld
        {
            get { return (Control.ModifierKeys & Keys.Control) == Keys.Control; }
        }

        public ToolMode CurrentTool
        {
            get { return _tool; }
            set
            {
                _tool       = value;
                EditReset();
                _activeAxis = GizmoAxis.None;
                _hoverAxis  = GizmoAxis.None;
                if (_gl != null) _gl.Cursor = ToolCursor(value);
                Redraw();
            }
        }

        private Cursor ToolCursor(ToolMode mode)
        {
            switch (mode)
            {
                case ToolMode.Move:   return Cursors.SizeAll;
                case ToolMode.Rotate: return Cursors.Hand;
                case ToolMode.Scale:  return Cursors.SizeNWSE;
                case ToolMode.Paint:  return PaintKind == BrushKind.Text2D ? Cursors.IBeam : Cursors.Cross;
                case ToolMode.Hand:   return Cursors.Hand;
                case ToolMode.EditShape: return Cursors.Cross;
                default:              return Cursors.Default;
            }
        }

        public List<SceneObject> SelectedObjects { get { return _selected; } }

        public SceneObject Primary
        {
            get { return _selected.Count > 0 ? _selected[_selected.Count - 1] : null; }
        }

        public void SetSelectedExternal(SceneObject obj)
        {
            ClearAllSel();
            _selected.Clear();
            if (obj != null) { obj.IsSelected = true; _selected.Add(obj); }
            _activeAxis = GizmoAxis.None;
            _hoverAxis  = GizmoAxis.None;
            Redraw();
        }

        public void SetSelection(IEnumerable<SceneObject> objs)
        {
            ClearAllSel();
            _selected.Clear();
            if (objs != null)
                foreach (var o in objs)
                    if (o != null && !_selected.Contains(o)) { o.IsSelected = true; _selected.Add(o); }
            _activeAxis = GizmoAxis.None;
            _hoverAxis  = GizmoAxis.None;
            Redraw();
        }

        // ── Constructor ───────────────────────────────────────────
        public SceneView()
        {
            InitializeComponent();
            _renderer = new SceneGeometry(_camera);
            _glr      = new GLRenderer(_camera);

            _gl = new GLControl(new GraphicsMode(new ColorFormat(32), 24, 8, 4))
            {
                Dock     = DockStyle.Fill,
                VSync    = false,   // don't block the UI thread on SwapBuffers — keeps
                                    // mouse events flowing so fast brush strokes stay smooth
                TabStop  = true,
                Cursor   = Cursors.Default
            };
            _gl.Load            += Gl_Load;
            _gl.Paint           += Gl_Paint;
            _gl.Resize          += (s, e) => _gl.Invalidate();
            _gl.MouseDown       += Gl_MouseDown;
            _gl.MouseUp         += Gl_MouseUp;
            _gl.MouseMove       += Gl_MouseMove;
            _gl.MouseLeave      += (s, e) => ClearHover();
            _gl.MouseWheel      += Gl_MouseWheel;
            _gl.PreviewKeyDown  += Gl_PreviewKeyDown;
            _gl.KeyDown         += Gl_KeyDown;
            _gl.KeyUp           += Gl_KeyUp;
            _gl.KeyPress        += Gl_KeyPress;   // characters typed into canvas text
            _gl.LostFocus       += (s, e) => _keysDown.Clear();

            // Files dragged onto the viewport (scenes / texture images) are surfaced
            // to the host via FilesDropped. The GLControl must accept the drop itself —
            // a parent's AllowDrop does not cover the area a child window occupies.
            _gl.AllowDrop = true;
            _gl.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            _gl.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) FilesDropped?.Invoke(this, files);
            };
            Controls.Add(_gl);

            _flyTimer.Interval = 16;          // ~60 fps
            _flyTimer.Tick += FlyTick;
            _flyTimer.Start();
            _flyClock.Start();
        }

        private void Redraw() { if (_gl != null) _gl.Invalidate(); }
        private void NotifyViewChanged()
        {
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        private int  W => _gl != null ? Math.Max(1, _gl.ClientSize.Width)  : 1;
        private int  H => _gl != null ? Math.Max(1, _gl.ClientSize.Height) : 1;

        // ── GL lifecycle ──────────────────────────────────────────
        private void Gl_Load(object sender, EventArgs e)
        {
            _glLoaded = true;
            _gl.MakeCurrent();
            GL.Enable(EnableCap.DepthTest);
        }

        // Frames drawn since start; the FPS plate reads the difference.
        public long FrameCount { get; private set; }

        private void Gl_Paint(object sender, PaintEventArgs e)
        {
            if (!_glLoaded || DesignMode) return;
            FrameCount++;
            _gl.MakeCurrent();
            _glr.Render(_scene, _gl.ClientSize.Width, _gl.ClientSize.Height, _renderer);
            if (ShowGuides) DrawGuidesGL();
            if (_selected.Count > 0) DrawGizmoGL();
            DrawEditShapeGL();
            DrawRulerGL();
            DrawLiveGL();
            DrawLassoGL();
            DrawNavGizmoGL();
            DrawBrushCursorGL();
            _gl.SwapBuffers();
        }

        // ── Smart guides (Photoshop-style alignment while dragging) ──
        // While a Move drag is active, the selection magnetises to the centres of
        // the other objects per world axis; each match is drawn as a magenta line
        // running through both aligned centres.
        private readonly List<PointF[]> _smartGuides = new List<PointF[]>();

        private void UpdateSmartGuides()
        {
            _smartGuides.Clear();
            if (!ShowGuides || _scene == null || _selected.Count == 0) return;

            Vec3 c = SelectionCenter();
            // Threshold scales with the zoom so the magnet feels the same at any distance.
            float thr = (_camera.Mode == ViewMode.Perspective ? _camera.Distance : _camera.OrthoScale) * 0.02f;

            for (int axis = 0; axis < 3; axis++)
            {
                float bestDiff  = thr;
                Vec3  bestOther = Vec3.Zero;
                bool  found     = false;
                foreach (var top in _scene.Objects)
                {
                    if (!top.IsVisible || _selected.Contains(top)) continue;
                    Vec3 oc = SceneGeometry.Center(top);
                    float d = Math.Abs(oc.Get(axis) - c.Get(axis));
                    if (d < bestDiff) { bestDiff = d; bestOther = oc; found = true; }
                }
                if (!found) continue;

                // Magnetise the selection onto the matched coordinate…
                float diff = bestOther.Get(axis) - c.Get(axis);
                if (Math.Abs(diff) > 1e-6f)
                {
                    Vec3 delta = Vec3.Zero.With(axis, diff);
                    foreach (var o in _selected)
                        if (!o.IsLocked) SceneGeometry.Translate(o, delta);
                }
                c = c.With(axis, bestOther.Get(axis));

                // …and remember the pair of aligned centres for drawing.
                PointF pa = Proj(bestOther), pb = Proj(c);
                if (pa.X < -9000 || pb.X < -9000) continue;
                _smartGuides.Add(new[] { pa, pb });
            }
        }

        private void DrawGuidesGL()
        {
            if (_smartGuides.Count == 0) return;

            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, W, H, 0, -1, 1);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            GL.LineWidth(1.2f);
            GL.Color4(1f, 0.25f, 0.85f, 0.9f);   // photoshop-magenta
            GL.Begin(PrimitiveType.Lines);
            foreach (var pair in _smartGuides)
            {
                PointF a = pair[0], b = pair[1];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 1f) { dx = 0f; dy = 1f; len = 1f; }   // coincident → vertical
                dx /= len; dy /= len;
                GL.Vertex2(a.X - dx * 4000f, a.Y - dy * 4000f); // extend across the screen
                GL.Vertex2(a.X + dx * 4000f, a.Y + dy * 4000f);
            }
            GL.End();

            GL.PointSize(6f);
            GL.Begin(PrimitiveType.Points);
            foreach (var pair in _smartGuides)
            {
                GL.Vertex2(pair[0].X, pair[0].Y);
                GL.Vertex2(pair[1].X, pair[1].Y);
            }
            GL.End();

            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Gizmo (drawn in GL, on top of the scene) ──────────────
        private void DrawGizmoGL()
        {
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);     // gizmo always visible
            GL.LineWidth(2.5f);

            Vec3 ctr = GizmoCenter();
            switch (_tool)
            {
                case ToolMode.Move:
                case ToolMode.Scale:
                    if (AxisVisible(GizmoAxis.X)) ArmGL(ctr, new Vec3(GizmoLen, 0, 0), 1f, 0f, 0f, GizmoAxis.X);
                    if (AxisVisible(GizmoAxis.Y)) ArmGL(ctr, new Vec3(0, GizmoLen, 0), 0f, 1f, 0f, GizmoAxis.Y);
                    if (AxisVisible(GizmoAxis.Z)) ArmGL(ctr, new Vec3(0, 0, GizmoLen), 0.2f, 0.5f, 1f, GizmoAxis.Z);
                    break;

                case ToolMode.Rotate:
                {
                    // Rings follow the selected object's local axes, so the gizmo tilts
                    // with the object's current rotation.
                    LocalBasis(out Vec3 lx, out Vec3 ly, out Vec3 lz);
                    RingGL(ctr, ly, lz, 1f, 0f, 0f, GizmoAxis.X);
                    RingGL(ctr, lx, lz, 0f, 1f, 0f, GizmoAxis.Y);
                    RingGL(ctr, lx, ly, 0.2f, 0.5f, 1f, GizmoAxis.Z);
                    break;
                }

                case ToolMode.Select:
                    GL.Color4(1f, 1f, 1f, 1f);
                    GL.PointSize(7f);
                    GL.Begin(PrimitiveType.Points);
                    GL.Vertex3(ctr.X, ctr.Y, ctr.Z);
                    GL.End();
                    break;
            }

            GL.Enable(EnableCap.DepthTest);
        }

        private void ArmGL(Vec3 ctr, Vec3 axis, float r, float g, float b, GizmoAxis a)
        {
            bool lit = _activeAxis == a || _hoverAxis == a;
            if (lit) { r = 1f; g = 1f; b = 0f; }
            Vec3 tip = ctr + axis;
            GL.Color4(r, g, b, 1f);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex3(ctr.X, ctr.Y, ctr.Z);
            GL.Vertex3(tip.X, tip.Y, tip.Z);
            GL.End();
            GL.PointSize(lit ? 11f : 8f);
            GL.Begin(PrimitiveType.Points);
            GL.Vertex3(tip.X, tip.Y, tip.Z);
            GL.End();
        }

        private void RingGL(Vec3 ctr, Vec3 t1, Vec3 t2, float r, float g, float b, GizmoAxis a)
        {
            bool lit = _activeAxis == a || _hoverAxis == a;
            if (lit) { r = 1f; g = 1f; b = 0f; }
            GL.Color4(r, g, b, 1f);
            GL.Begin(PrimitiveType.LineLoop);
            const int segs = 64;
            for (int i = 0; i < segs; i++)
            {
                float ang = i * 2f * (float)Math.PI / segs;
                Vec3 p = ctr + t1 * (RingR * (float)Math.Cos(ang)) + t2 * (RingR * (float)Math.Sin(ang));
                GL.Vertex3(p.X, p.Y, p.Z);
            }
            GL.End();
        }

        // In an orthographic view the axis perpendicular to the screen projects to a
        // dot and can't be dragged meaningfully — hide it from the move/scale gizmo
        // (use perspective for depth). Front→Z, Top→Y, Side→X.
        private bool AxisVisible(GizmoAxis a)
        {
            switch (_camera.Mode)
            {
                case ViewMode.Front: return a != GizmoAxis.Z;
                case ViewMode.Top:   return a != GizmoAxis.Y;
                case ViewMode.Side:  return a != GizmoAxis.X;
                default:             return true;
            }
        }

        private Vec3 SelectionCenter()
        {
            if (_selected.Count == 0) return Vec3.Zero;
            Vec3 sum = Vec3.Zero;
            foreach (var o in _selected) sum = sum + SceneGeometry.Center(o);
            return sum * (1f / _selected.Count);
        }

        // Stable origin during rotate/scale so the pivot does not drift mid-drag
        private Vec3 GizmoCenter()
        {
            if (_transformDrag && (_tool == ToolMode.Rotate || _tool == ToolMode.Scale))
                return _pivot;
            return SelectionCenter();
        }

        private bool SelectionLocked()
        {
            foreach (var o in _selected)
                if (o != null && o.IsLocked) return true;
            return false;
        }

        private bool IsLocked(SceneObject obj)
        {
            return obj != null && TopLevelOwner(obj).IsLocked;
        }

        // World-space local axes of the primary selected object, for the rotation
        // gizmo (rings hug the object's current orientation). Falls back to world axes.
        private void LocalBasis(out Vec3 lx, out Vec3 ly, out Vec3 lz)
        {
            var p = Primary;
            if (p == null)
            {
                lx = new Vec3(1, 0, 0); ly = new Vec3(0, 1, 0); lz = new Vec3(0, 0, 1);
                return;
            }
            lx = SceneGeometry.LocalAxisWorld(p, 0);
            ly = SceneGeometry.LocalAxisWorld(p, 1);
            lz = SceneGeometry.LocalAxisWorld(p, 2);
        }

        // ── Mouse ─────────────────────────────────────────────────
        private void Gl_MouseDown(object sender, MouseEventArgs e)
        {
            _lastMouse = e.Location;
            _gl.Focus();

            // The navigation gizmo (top-right) takes a left click before any tool.
            if (e.Button == MouseButtons.Left && BeginNavGizmoPress(e.Location)) return;

            if (e.Button == MouseButtons.Right)  { _orbitDrag = true; _rightDownPt = e.Location; }
            if (e.Button == MouseButtons.Middle) { _panDrag   = true; }

            // A live element takes the press first: handle / body → edit it;
            // elsewhere → it is committed and the tool carries on as usual.
            _liveTextJustEnded = false;
            if (LiveMouseDown(e)) return;
            if (RulerMouseDown(e)) return;   // the ruler's body / knobs take the press before any tool

            if (e.Button == MouseButtons.Left && _tool == ToolMode.Paint)
            {
                _lastPaintPt = e.Location;
                if (TellCanvasBack(e.Location)) return;   // the reverse of the sheet takes no paint
                if (PaintKind == BrushKind.Eyedropper) { DoEyedropper(e.Location); return; }
                if (PaintKind == BrushKind.Select2D)
                {
                    if (!PickPaintTarget(e.Location, out SceneObject selObj, out PointF selUv)) return;
                    if (selObj.PrimitiveType != "Canvas") { Notify?.Invoke(this, Loc.T("Выделение областей работает на холсте")); return; }
                    if (IsLocked(selObj)) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }
                    TransformStarted?.Invoke(this, EventArgs.Empty);   // undo before the cut
                    BeginSelectDrag(selObj, selUv, e.Location);
                    return;
                }
                if (PaintKind == BrushKind.Lasso) { BeginLasso(e.Location); return; }
                if (PaintKind == BrushKind.Text2D)
                {
                    if (_liveTextJustEnded) return;   // that click finished a text, not started one
                    if (PickPaintTarget(e.Location, out SceneObject tObj, out PointF tUv))
                    {
                        if (IsLocked(tObj)) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }
                        // Both the sheet and a 3-D surface support in-place text.
                        if (tObj.PrimitiveType == "Canvas") BeginTextAt(tObj, e.Location);
                        else BeginSurfaceText(tObj, e.Location);
                    }
                    return;
                }
                if (IsShapeTool(PaintKind))
                {
                    if (!PickPaintTarget(e.Location, out SceneObject sObj, out PointF sUv)) return;
                    if (IsLocked(sObj)) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }
                    BeginLiveShape(sObj, e.Location);
                    return;
                }

                if (PickPaintTarget(e.Location, out SceneObject pObj, out PointF _lockUv) && IsLocked(pObj))
                {
                    Notify?.Invoke(this, Loc.T("Объект заблокирован"));
                    return;
                }
                if (PaintKind == BrushKind.ObjectEraser) { BeginObjectErase(e.Location); return; }
                _painting  = true;
                EndCanvasStroke();          // new stroke — no previous point yet
                TransformStarted?.Invoke(this, EventArgs.Empty);   // one undo per stroke
                if (PaintKind == BrushKind.Fill) { DoFill(e.Location); _painting = false; }
                else { PaintDab(e.Location); Redraw(); ObjectTransformed?.Invoke(this, EventArgs.Empty); }
                return;
            }

            // Hand tool: left-drag pans the view.
            if (e.Button == MouseButtons.Left && _tool == ToolMode.Hand)
            {
                _panDrag = true;
                return;
            }

            if (e.Button == MouseButtons.Left && _tool == ToolMode.EditShape)
            {
                EditMouseDown(e);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                bool grabbed = false;
                if (_tool != ToolMode.Select && _tool != ToolMode.Paint && _tool != ToolMode.Hand && _selected.Count > 0 && !SelectionLocked())
                {
                    GizmoAxis hit = HitTestGizmo(e.Location);
                    if (hit != GizmoAxis.None)
                    {
                        // Alt + grab a gizmo → duplicate the selection first, then drag
                        // the clones (copy by pulling out with Alt held). The
                        // host clones and reselects, so we re-assert the grabbed axis.
                        bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt;
                        if (alt && (_tool == ToolMode.Move || _tool == ToolMode.Rotate || _tool == ToolMode.Scale))
                            DuplicateForDrag?.Invoke(this, EventArgs.Empty);

                        _activeAxis        = hit;
                        _pivot             = SelectionCenter();
                        _gizmoCenterScreen = Proj(_pivot);
                        _rotAccum          = 0f;
                        if (_tool == ToolMode.Rotate)
                            _rotAxisWorld = SceneGeometry.LocalAxisWorld(Primary, AxisIndex());
                        _transformDrag = true;
                        _undoFired     = false;
                        grabbed        = true;
                        Redraw();
                    }
                }
                if (!grabbed)
                {
                    _activeAxis    = GizmoAxis.None;
                    _transformDrag = false;
                    bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                    HandleClick(e.Location, ctrl);
                }
            }
        }

        private void Gl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) EndNavGizmoPress();
            LiveMouseUp(e);
            RulerMouseUp(e);
            if (e.Button == MouseButtons.Left && _lassoDragging) EndLasso();
            if (e.Button == MouseButtons.Left && _selDragging)
            {
                EndSelectDrag();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
            }

            if (e.Button == MouseButtons.Left) EditMouseUp();

            bool     wasTransform = _transformDrag;
            ToolMode toolAtEnd    = _tool;

            _orbitDrag     = false;
            _panDrag       = false;
            _transformDrag = false;
            _undoFired     = false;
            _painting      = false;
            _activeAxis    = GizmoAxis.None;
            _smartGuides.Clear();
            EndCanvasStroke();
            FlushPaintEncoding();   // undo snapshot for the next stroke, off the UI thread

            if (wasTransform && toolAtEnd == ToolMode.Move && SnapHeld)
            {
                SnapSelection();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
            }

            // Right-click without an orbit drag → object context menu.
            if (e.Button == MouseButtons.Right &&
                Math.Abs(e.X - _rightDownPt.X) <= 3 && Math.Abs(e.Y - _rightDownPt.Y) <= 3)
            {
                RightClickContext(e.Location);
            }
            Redraw();
            NotifyViewChanged();
        }

        // On a right-click (no drag): select the object under the cursor if it isn't
        // already part of the selection, then ask the host to show its context menu.
        private void RightClickContext(Point pt)
        {
            var hit = PickTopLevel(pt);
            if (hit != null && !_selected.Contains(hit))
            {
                ClearAllSel();
                _selected.Clear();
                hit.IsSelected = true;
                _selected.Add(hit);
                ObjectSelected?.Invoke(this, Primary);
            }
            if (_selected.Count > 0)
                ContextMenuRequested?.Invoke(pt);
        }

        private void Gl_MouseMove(object sender, MouseEventArgs e)
        {
            TrackHover(e.Location);
            float dx = e.X - _lastMouse.X;
            float dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            if (_navPress && (Control.MouseButtons & MouseButtons.Left) != 0) { NavGizmoDrag(e.Location, dx, dy); return; }

            // Editing a live element (move / resize / rotate / the creation drag).
            if (LiveMouseMove(e)) return;
            if (RulerMouseMove(e)) return;
            if (e.Button == MouseButtons.None) UpdateRulerCursor(e.Location);
            if (_live != null && e.Button == MouseButtons.None && _gl != null)
                _gl.Cursor = LiveCursorAt(e.Location) ?? ToolCursor(_tool);
            if (e.Button == MouseButtons.None) UpdateBackCursor(e.Location);

            if (_lassoDragging && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
            {
                LassoTo(e.Location);
                return;
            }

            // Rubber-band of the selection tool.
            if (_selDragging && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
            {
                if (PickPaintTarget(e.Location, out SceneObject sObj2, out PointF sUv2) &&
                    ReferenceEquals(sObj2, _selCanvas))
                {
                    _selCurUv = sUv2;
                    DrawSelectMarquee();
                }
                return;
            }

            if (_painting && (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
            {
                if (PaintKind == BrushKind.ObjectEraser) ObjectEraseTo(e.Location);
                else PaintLineTo(e.Location);    // continuous stroke between samples
                return;
            }

            if (_orbitDrag)
            {
                _camera.AngleY += dx * 0.85f;
                _camera.AngleX  = Math.Max(-89f, Math.Min(89f, _camera.AngleX + dy * 0.6f));
                Redraw();
                NotifyViewChanged();
                return;
            }
            if (_panDrag)
            {
                float s  = _camera.Distance * 0.0035f;
                float ay = _camera.AngleY * (float)Math.PI / 180f;
                var right = new Vec3((float)Math.Cos(ay), 0f, -(float)Math.Sin(ay));
                _camera.Target = _camera.Target - right * (dx * s) + new Vec3(0f, dy * s, 0f);
                Redraw();
                NotifyViewChanged();
                return;
            }

            if (_editDrag)   // not the live button state: a move queued before the release still counts
            {
                EditMouseMove(dx, dy);
                return;
            }

            if (_transformDrag && _selected.Count > 0 && _activeAxis != GizmoAxis.None)
            {
                if (SelectionLocked()) { _transformDrag = false; Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }
                if (!_undoFired)
                {
                    _undoFired = true;
                    TransformStarted?.Invoke(this, EventArgs.Empty);
                }
                ApplyTransform(dx, dy);
                if (_tool == ToolMode.Move) UpdateSmartGuides();
                Redraw();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_orbitDrag && !_panDrag && !_transformDrag &&
                _tool != ToolMode.Select && _tool != ToolMode.Paint && _tool != ToolMode.Hand &&
                _selected.Count > 0)
            {
                GizmoAxis newHover = HitTestGizmo(e.Location);
                if (newHover != _hoverAxis)
                {
                    _hoverAxis = newHover;
                    Redraw();
                }
            }
        }

        private void Gl_MouseWheel(object sender, MouseEventArgs e)
        {
            // Alt+wheel over the viewport resizes the brush without leaving the canvas.
            if (_tool == ToolMode.Paint && (ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                BrushWheel?.Invoke(this, e.Delta > 0 ? +4 : -4);
                return;
            }

            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            float f = e.Delta > 0 ? 0.88f : 1.14f;

            // Ctrl+wheel zooms in any mode. Plain wheel = fly speed in 3-D
            // (UE5-style); on the flat canvas/ortho views it just zooms.
            if (ctrl || _camera.Mode != ViewMode.Perspective)
            {
                _camera.Distance   = Math.Max(Camera.MinDistance, Math.Min(Camera.MaxDistance, _camera.Distance   * f));
                _camera.OrthoScale = Math.Max(Camera.MinDistance, Math.Min(Camera.MaxDistance, _camera.OrthoScale * f));
                Redraw();
                NotifyViewChanged();
            }
            else
            {
                float m = e.Delta > 0 ? 1.15f : 0.87f;
                _flySpeed = Math.Max(0.05f, Math.Min(30f, _flySpeed * m));
                Notify?.Invoke(this, string.Format(Loc.T("Скорость полёта: {0:0.0}×"), _flySpeed));
            }
        }

        // ── Keyboard camera ───────────────────────────────────────
        private void Gl_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            // Typing: arrows, Enter, Tab, Home / End … all go to the text editor.
            if (IsEditingText && (e.Modifiers & (Keys.Control | Keys.Alt)) == 0) { e.IsInputKey = true; return; }
            // With Ctrl / Alt these letters are commands (Ctrl+Q palette, Ctrl+S save,
            // Ctrl+A select all, Ctrl+D duplicate) — claiming them as input keys here
            // would skip the form's ProcessCmdKey and swallow the command.
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) != 0) return;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.W: case Keys.A: case Keys.S: case Keys.D:
                case Keys.Q: case Keys.E:
                    e.IsInputKey = true;
                    break;
            }
        }

        private int _lastNudgeTick;

        private void Gl_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsEditingText)
            {
                // Consumed keys never become characters; plain ones go on to KeyPress.
                if (TextKeyDown(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
                return;
            }
            // Arrow keys nudge the selection (works whenever the viewport has focus,
            // regardless of the form's command-key routing).
            if (IsArrow(e.KeyCode) && _selected.Count > 0)
            {
                int now = Environment.TickCount;
                if (now - _lastNudgeTick > 500) TransformStarted?.Invoke(this, EventArgs.Empty);
                _lastNudgeTick = now;

                int sx = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
                int sy = e.KeyCode == Keys.Up   ?  1 : e.KeyCode == Keys.Down  ? -1 : 0;
                NudgeSelection(sx, sy, (e.Modifiers & Keys.Shift) == Keys.Shift);
                e.Handled = true;
                return;
            }

            // Plain WASDQE fly the camera; Ctrl+S / Ctrl+A / … must not also start a
            // flight (the camera used to drift backwards while saving).
            if (IsFlyKey(e.KeyCode) && (e.Modifiers & (Keys.Control | Keys.Alt)) == 0)
            {
                _keysDown.Add(e.KeyCode);
                e.Handled = true;
            }
        }

        private static bool IsArrow(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down;
        }

        private void Gl_KeyUp(object sender, KeyEventArgs e)
        {
            _keysDown.Remove(e.KeyCode);
        }

        private static bool IsFlyKey(Keys k)
        {
            switch (k)
            {
                // Arrows intentionally excluded — they nudge the selection, not the camera.
                case Keys.W: case Keys.A: case Keys.S: case Keys.D:
                case Keys.Q: case Keys.E:
                    return true;
                default:
                    return false;
            }
        }

        // One auto-rotation step. While the user drags with the mouse (orbit, pan,
        // transform, brush) the rotation pauses — otherwise it fights the hand.
        private void SpinTick(float dt, bool mouseHeld)
        {
            if (!_spin || _spinPaused) return;
            if (mouseHeld || _orbitDrag || _panDrag || _transformDrag || _painting ||
                _selDragging || _lassoDragging || _liveDrag != LiveDrag.None) return;

            float step = _spinSpeed * dt;
            if (Math.Abs(step) < 1e-5f) return;

            if (_spinObjects)
            {
                // Selected objects each turn about their own centre. With nothing selected the whole scene
                // turns as one piece about its centre (the canvas sheet stays), so the mode always shows something.
                List<SceneObject> spun = _selected;
                bool together = false;
                Vec3 pivot = Vec3.Zero;
                if (spun.Count == 0)
                {
                    spun = new List<SceneObject>();
                    if (_scene != null)
                        foreach (var o in _scene.Objects)
                            if (o.IsVisible && o.PrimitiveType != "Canvas") spun.Add(o);
                    if (_spinTurned.Count == 0 || !_spinWhole)
                    {
                        if (spun.Count == 0 || !SpinPivot(out pivot)) return;
                        _spinPivot = pivot;
                    }
                    pivot = _spinPivot;
                    together = true;
                }
                _spinWhole = together;
                foreach (var obj in spun)
                {
                    if (IsLocked(obj)) continue;
                    SceneGeometry.RotateAroundAxis(obj, together ? pivot : SceneGeometry.Center(obj),
                        new Vec3(0f, 1f, 0f), step);
                    float turned;
                    _spinTurned.TryGetValue(obj, out turned);
                    _spinTurned[obj] = turned + step;
                }

                // The event fires at most 5 times a second: the properties panel and the title
                // must not be rebuilt 60 times a second just for a smooth picture.
                _spinNotify += dt;
                if (_spinNotify >= 0.2f)
                {
                    _spinNotify = 0f;
                    ObjectTransformed?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                if (_camera.Mode != ViewMode.Perspective) return;

                // The camera circles the object rather than an empty point: the target eases
                // onto the centre of the selection (or of the whole scene), so turning rotation
                // on causes no jolt and the orbit goes around the shape itself.
                Vec3 pivot;
                if (SpinPivot(out pivot))
                {
                    float t = 1f - (float)Math.Exp(-4f * dt);
                    _camera.Target = _camera.Target + (pivot - _camera.Target) * t;
                }

                _camera.AngleY += step;
                if (_camera.AngleY >  360f) _camera.AngleY -= 720f;
                if (_camera.AngleY < -360f) _camera.AngleY += 720f;
            }

            Redraw();
            NotifyViewChanged();
        }

        // The point it makes sense to orbit the camera around: the centre of the
        // selection or, with nothing selected, the centre of the scene geometry. The
        // canvas sheet is left out — nobody orbits the "paper", and it would pull the centre to itself.
        private bool SpinPivot(out Vec3 pivot)
        {
            pivot = Vec3.Zero;
            if (_selected.Count > 0)
            {
                Vec3 sum = Vec3.Zero;
                int n = 0;
                foreach (var o in _selected) { sum = sum + SceneGeometry.Center(o); n++; }
                if (n > 0) { pivot = sum * (1f / n); return true; }
            }

            if (_scene == null) return false;
            Vec3 min = new Vec3( float.MaxValue,  float.MaxValue,  float.MaxValue);
            Vec3 max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            bool any = false;
            foreach (var o in _scene.Objects)
            {
                if (!o.IsVisible || o.PrimitiveType == "Canvas") continue;
                if (!SceneGeometry.WorldBounds(o, out Vec3 bmin, out Vec3 bmax)) continue;
                if (bmin.X < min.X) min.X = bmin.X; if (bmax.X > max.X) max.X = bmax.X;
                if (bmin.Y < min.Y) min.Y = bmin.Y; if (bmax.Y > max.Y) max.Y = bmax.Y;
                if (bmin.Z < min.Z) min.Z = bmin.Z; if (bmax.Z > max.Z) max.Z = bmax.Z;
                any = true;
            }
            if (!any) return false;
            pivot = (min + max) * 0.5f;
            return true;
        }

        // Builds a smoothly accelerated / damped velocity from the held keys and
        // glides the camera target — independent of the OS key-repeat rate.
        private int _lastLightFrame;

        private void FlyTick(object sender, EventArgs e)
        {
            float dt = (float)_flyClock.Elapsed.TotalSeconds;
            _flyClock.Restart();
            if (dt <= 0f) return;
            if (dt > 0.05f) dt = 0.05f;

            bool mouseHeld = (Control.MouseButtons &
                (MouseButtons.Left | MouseButtons.Right | MouseButtons.Middle)) != 0;

            SpinTick(dt, mouseHeld);
            // The existing navigation timer also animates local light intensity at a modest 30 fps.
            if (_glr != null && _glr.HasAnimatedLights && Visible &&
                unchecked(Environment.TickCount - _lastLightFrame) >= 33)
            {
                _lastLightFrame = Environment.TickCount;
                Redraw();
            }

            Vec3 wish = Vec3.Zero;
            if (_keysDown.Count > 0 && mouseHeld)
            {
                if (_keysDown.Contains(Keys.W)) wish = wish + _camera.ForwardDir();
                if (_keysDown.Contains(Keys.S)) wish = wish - _camera.ForwardDir();
                if (_keysDown.Contains(Keys.D)) wish = wish + _camera.RightDir();
                if (_keysDown.Contains(Keys.A)) wish = wish - _camera.RightDir();
                if (_keysDown.Contains(Keys.E)) wish = wish + _camera.UpDir();
                if (_keysDown.Contains(Keys.Q)) wish = wish - _camera.UpDir();
            }

            float baseDist = _camera.Mode == ViewMode.Perspective ? _camera.Distance : _camera.OrthoScale;
            float maxSpeed = baseDist * 2.2f * _flySpeed;

            Vec3 targetVel = wish.Length > 1e-4f ? wish.Normalized() * maxSpeed : Vec3.Zero;

            float rate = wish.Length > 1e-4f ? 11f : 9f;
            float t = 1f - (float)Math.Exp(-rate * dt);
            _camVel = _camVel + (targetVel - _camVel) * t;

            if (_camVel.Length < maxSpeed * 0.001f)
            {
                _camVel = Vec3.Zero;
                return;
            }

            _camera.Target = _camera.Target + _camVel * dt;

            // Keep the fly camera from drifting infinitely far from the scene.
            float tlen = _camera.Target.Length;
            if (tlen > Camera.MaxDistance)
                _camera.Target = _camera.Target * (Camera.MaxDistance / tlen);

            Redraw();
            NotifyViewChanged();
        }

        // ── Transform ─────────────────────────────────────────────
        private void ApplyTransform(float dx, float dy)
        {
            switch (_tool)
            {
                case ToolMode.Move:   ApplyMove(dx, dy);   break;
                case ToolMode.Rotate: ApplyRotate(dx, dy); break;
                case ToolMode.Scale:  ApplyScale(dx, dy);  break;
            }
        }

        private void ApplyMove(float dx, float dy)
        {
            if (SelectionLocked()) return;
            Vec3   ctr   = GizmoCenter();
            PointF sc    = Proj(ctr);
            Vec3   wAxis = WorldAxis();
            PointF tip   = Proj(ctr + wAxis * GizmoLen);

            float axDx  = tip.X - sc.X;
            float axDy  = tip.Y - sc.Y;
            float axLen = (float)Math.Sqrt(axDx * axDx + axDy * axDy);
            float s     = (_camera.Mode == ViewMode.Perspective ? _camera.Distance : _camera.OrthoScale) * 0.005f;

            float proj;
            if (axLen < 6f)
            {
                // Axis points (almost) at the camera → it projects to a dot and the
                // normal mapping breaks. Drag vertically instead; sign from whether
                // the axis tip is nearer the camera than the centre.
                float depthCtr = _camera.GetDepth(ctr);
                float depthTip = _camera.GetDepth(ctr + wAxis * GizmoLen);
                float sign     = depthTip >= depthCtr ? 1f : -1f;   // tip toward camera → +
                proj = dy * sign * 1.4f;                            // drag down = pull toward you
            }
            else
            {
                proj = (dx * axDx + dy * axDy) / axLen;
            }

            // Shift — fine, precise dragging (¼ speed).
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) proj *= 0.25f;

            Vec3 delta = wAxis * (proj * s);
            foreach (var o in _selected)
                if (!o.IsLocked) SceneGeometry.Translate(o, delta);

            // Ctrl — live snap to the grid / other objects while the hand drags,
            // so "exactly" no longer requires the arrow keys.
            if (SnapHeld) SnapSelection();
        }

        private void ApplyRotate(float dx, float dy)
        {
            if (SelectionLocked()) return;
            float cx    = _gizmoCenterScreen.X;
            float cy    = _gizmoCenterScreen.Y;
            float curX  = _lastMouse.X, curY = _lastMouse.Y;
            float prevX = curX - dx,    prevY = curY - dy;

            float dAngle;
            float dist = (float)Math.Sqrt((prevX - cx) * (prevX - cx) + (prevY - cy) * (prevY - cy));
            if (dist > 5f)
            {
                float prevA = (float)Math.Atan2(prevY - cy, prevX - cx);
                float currA = (float)Math.Atan2(curY  - cy, curX  - cx);
                dAngle = (currA - prevA) * (180f / (float)Math.PI);
            }
            else
            {
                dAngle = (dx + dy) * 0.5f;
            }

            // Shift — fine rotation (¼ speed).
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) dAngle *= 0.25f;

            // Ctrl — rotate in clean 15° steps: accumulate the raw angle and only
            // release whole increments, like in Photoshop/Blender.
            if (SnapHeld)
            {
                const float step = 15f;
                _rotAccum += dAngle;
                dAngle = 0f;
                while (_rotAccum >=  step) { dAngle += step; _rotAccum -= step; }
                while (_rotAccum <= -step) { dAngle -= step; _rotAccum += step; }
                if (dAngle == 0f) return;
            }

            // Rotate about the object's local axis (locked in world space at drag start)
            // so the result matches the tilted ring the user grabbed.
            foreach (var o in _selected)
                if (!o.IsLocked) SceneGeometry.RotateAroundAxis(o, _pivot, _rotAxisWorld, dAngle);
        }

        private void ApplyScale(float dx, float dy)
        {
            if (SelectionLocked()) return;
            Vec3   ctr   = GizmoCenter();
            PointF sc    = Proj(ctr);
            Vec3   wAxis = WorldAxis();
            PointF tip   = Proj(ctr + wAxis * GizmoLen);

            float axDx  = tip.X - sc.X;
            float axDy  = tip.Y - sc.Y;
            float axLen = (float)Math.Sqrt(axDx * axDx + axDy * axDy);
            if (axLen < 1f) return;

            float proj = (dx * axDx + dy * axDy) / axLen;
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) proj *= 0.25f;   // fine
            float f    = 1f + proj * 0.013f;
            if (f < 0.5f) f = 0.5f;
            if (f > 2f)   f = 2f;

            int axis = AxisIndex();
            foreach (var o in _selected)
                if (!o.IsLocked) SceneGeometry.ScaleAround(o, _pivot, axis, f);
        }

        private Vec3 WorldAxis()
        {
            switch (_activeAxis)
            {
                case GizmoAxis.X: return new Vec3(1, 0, 0);
                case GizmoAxis.Y: return new Vec3(0, 1, 0);
                case GizmoAxis.Z: return new Vec3(0, 0, 1);
                default:          return Vec3.Zero;
            }
        }

        private int AxisIndex()
        {
            switch (_activeAxis)
            {
                case GizmoAxis.X: return 0;
                case GizmoAxis.Y: return 1;
                default:          return 2;
            }
        }

        // ── Gizmo hit testing ─────────────────────────────────────
        private GizmoAxis HitTestGizmo(Point pt)
        {
            if (_tool == ToolMode.EditShape) return EditHitGizmo(pt);
            if (_selected.Count == 0) return GizmoAxis.None;
            if (SelectionLocked()) return GizmoAxis.None;
            Vec3   ctr = GizmoCenter();
            PointF sc  = Proj(ctr);
            if (sc.X < -9000) return GizmoAxis.None;

            if (_tool == ToolMode.Move || _tool == ToolMode.Scale)
            {
                PointF px = Proj(ctr + new Vec3(GizmoLen, 0, 0));
                PointF py = Proj(ctr + new Vec3(0, GizmoLen, 0));
                PointF pz = Proj(ctr + new Vec3(0, 0, GizmoLen));

                if (AxisVisible(GizmoAxis.X) && px.X > -9000 && DistPP(px, pt) < HitTip) return GizmoAxis.X;
                if (AxisVisible(GizmoAxis.Y) && py.X > -9000 && DistPP(py, pt) < HitTip) return GizmoAxis.Y;
                if (AxisVisible(GizmoAxis.Z) && pz.X > -9000 && DistPP(pz, pt) < HitTip) return GizmoAxis.Z;
                if (AxisVisible(GizmoAxis.X) && px.X > -9000 && DistSeg(sc, px, pt) < HitShaft) return GizmoAxis.X;
                if (AxisVisible(GizmoAxis.Y) && py.X > -9000 && DistSeg(sc, py, pt) < HitShaft) return GizmoAxis.Y;
                if (AxisVisible(GizmoAxis.Z) && pz.X > -9000 && DistSeg(sc, pz, pt) < HitShaft) return GizmoAxis.Z;
            }
            else if (_tool == ToolMode.Rotate)
            {
                LocalBasis(out Vec3 lx, out Vec3 ly, out Vec3 lz);
                float dX = RingMinDist(pt, ctr, ly, lz);
                float dY = RingMinDist(pt, ctr, lx, lz);
                float dZ = RingMinDist(pt, ctr, lx, ly);

                float best = HitRing;
                GizmoAxis axis = GizmoAxis.None;
                if (dX < best) { best = dX; axis = GizmoAxis.X; }
                if (dY < best) { best = dY; axis = GizmoAxis.Y; }
                if (dZ < best) {             axis = GizmoAxis.Z; }
                return axis;
            }
            return GizmoAxis.None;
        }

        private float RingMinDist(Point pt, Vec3 ctr, Vec3 t1, Vec3 t2)
        {
            const int segs = 48;
            float   min  = float.MaxValue;
            PointF? prev = null;
            for (int i = 0; i <= segs; i++)
            {
                float  a  = i * 2f * (float)Math.PI / segs;
                Vec3   p3 = ctr
                          + t1 * (RingR * (float)Math.Cos(a))
                          + t2 * (RingR * (float)Math.Sin(a));
                PointF s  = Proj(p3);
                if (s.X < -9000) { prev = null; continue; }
                if (prev.HasValue)
                {
                    float d = DistSeg(prev.Value, s, pt);
                    if (d < min) min = d;
                }
                prev = s;
            }
            return min;
        }

        // ── Selection (face-accurate picking → top-level owner) ───
        private void HandleClick(Point pt, bool ctrl)
        {
            if (_scene == null) return;
            SceneObject hit = PickTopLevel(pt);

            if (ctrl)
            {
                if (hit != null)
                {
                    if (_selected.Contains(hit)) { _selected.Remove(hit); hit.IsSelected = false; }
                    else                         { _selected.Add(hit);    hit.IsSelected = true;  }
                }
            }
            else
            {
                ClearAllSel();
                _selected.Clear();
                if (hit != null) { hit.IsSelected = true; _selected.Add(hit); }
            }

            _activeAxis = GizmoAxis.None;
            _hoverAxis  = GizmoAxis.None;
            ObjectSelected?.Invoke(this, Primary);
            Redraw();
        }

        private SceneObject PickTopLevel(Point pt)
        {
            bool isPersp  = _camera.Mode == ViewMode.Perspective;
            Vec3 camPos   = isPersp ? _camera.GetPosition()   : Vec3.Zero;
            Vec3 orthoDir = isPersp ? Vec3.Zero               : _camera.GetOrthoViewDir();

            float       bestDepth = float.NegativeInfinity;
            SceneObject bestLeaf  = null;

            foreach (var top in _scene.Objects)
            {
                if (!top.IsVisible) continue;   // hidden objects can't be picked
                foreach (var leaf in SceneGeometry.Leaves(top))
                    PickLeaf(leaf, pt, isPersp, camPos, orthoDir, ref bestDepth, ref bestLeaf);
            }

            return bestLeaf == null ? null : TopLevelOwner(bestLeaf);
        }

        private void PickLeaf(SceneObject obj, Point pt, bool isPersp,
                              Vec3 camPos, Vec3 orthoDir, ref float bestDepth, ref SceneObject best)
        {
            if (obj.Mesh == null || !obj.IsVisible) return;
            var xf = SceneGeometry.Xform.For(obj);   // trig once per object, not per vertex
            foreach (var face in obj.Mesh.Faces)
            {
                if (face.Length < 3) continue;

                var wv = new Vec3[face.Length];
                for (int i = 0; i < face.Length; i++)
                    wv[i] = xf.Apply(obj.Mesh.Vertices[face[i]]);

                Vec3 n   = Vec3.Cross(wv[1] - wv[0], wv[2] - wv[0]).Normalized();
                Vec3 ftc = isPersp ? (camPos - wv[0]).Normalized() : orthoDir;
                if (Vec3.Dot(n, ftc) <= 0f) continue;

                var pts = new PointF[face.Length];
                var zs = new float[face.Length];
                bool valid = true;
                for (int i = 0; i < face.Length; i++)
                {
                    pts[i] = _camera.Project(wv[i], W, H);
                    if (pts[i].X < -9000 || float.IsNaN(pts[i].X)) { valid = false; break; }
                    zs[i] = _camera.GetDepth(wv[i]);
                }
                if (!valid || !PointInPoly(pts, pt)) continue;

                // Depth where the cursor actually is: a big face (the sheet) must not win over a small
                // thing lying on it just because its middle is nearer than the thing's middle.
                float depth = 0f;
                bool found = false;
                for (int i = 1; i + 1 < face.Length && !found; i++)
                {
                    if (!Bary(pts[0], pts[i], pts[i + 1], pt, out float la, out float lb, out float lc)) continue;
                    found = true;
                    if (isPersp)
                    {
                        float inv = la / zs[0] + lb / zs[i] + lc / zs[i + 1];
                        depth = Math.Abs(inv) < 1e-9f ? zs[0] : 1f / inv;   // 1/z is what is linear on the screen
                    }
                    else depth = la * zs[0] + lb * zs[i] + lc * zs[i + 1];
                }
                if (!found) { foreach (float z in zs) depth += z; depth /= zs.Length; }

                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best      = obj;
                }
            }
        }

        private SceneObject TopLevelOwner(SceneObject leaf)
        {
            foreach (var top in _scene.Objects)
            {
                if (ReferenceEquals(top, leaf)) return top;
                foreach (var l in SceneGeometry.Leaves(top))
                    if (ReferenceEquals(l, leaf)) return top;
            }
            return leaf;
        }

        // ── Snapping ──────────────────────────────────────────────
        private const float ObjectSnapDist = 0.35f;

        private void SnapSelection()
        {
            if (_selected.Count == 0) return;
            if (SelectionLocked()) return;
            Vec3 c = SelectionCenter();

            var others = new List<Vec3>();
            foreach (var top in _scene.Objects)
                if (!_selected.Contains(top))
                    others.Add(SceneGeometry.Center(top));

            Vec3 target = c;
            for (int a = 0; a < 3; a++)
            {
                float cur      = c.Get(a);
                float best     = cur;
                float bestDist = float.MaxValue;

                if (SnapGrid > 0f)
                {
                    float gridV = (float)Math.Round(cur / SnapGrid) * SnapGrid;
                    float gd    = Math.Abs(gridV - cur);
                    if (gd < bestDist && gd <= SnapGrid * 0.5f) { best = gridV; bestDist = gd; }
                }

                foreach (var o in others)
                {
                    float ov = o.Get(a);
                    float d  = Math.Abs(ov - cur);
                    if (d < bestDist && d <= ObjectSnapDist) { best = ov; bestDist = d; }
                }

                target = target.With(a, best);
            }

            Vec3 delta = target - c;
            if (delta.Length < 1e-5f) return;
            foreach (var o in _selected)
                if (!o.IsLocked) SceneGeometry.Translate(o, delta);
        }

        // ── Arrow-key nudging (pure world X/Z, oriented by camera) ─
        public bool NudgeSelection(int sx, int sy, bool fine)
        {
            if (_selected.Count == 0) return false;
            if (SelectionLocked()) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return false; }

            float step = fine ? 0.1f : (SnapHeld ? SnapGrid : 0.5f);

            // Move along whichever world axis the arrow points to ON SCREEN, so the
            // figure always slides visibly: horizontal stays on X/Z, vertical can be
            // Y (front/side/persp) or Z (top view).
            Vec3 right = DominantAxis(_camera.ScreenRight(), Vec3.Zero);
            Vec3 up    = DominantAxis(_camera.ScreenUp(),    right);

            Vec3 delta = right * (sx * step) + up * (sy * step);
            if (delta.Length < 1e-6f) return false;

            foreach (var o in _selected)
                if (!o.IsLocked) SceneGeometry.Translate(o, delta);
            if (SnapHeld) SnapSelection();
            Redraw();
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        // Snap a screen direction to the nearest signed world axis (±X/±Y/±Z),
        // skipping the axis already taken by the other arrow direction.
        private static Vec3 DominantAxis(Vec3 v, Vec3 avoid)
        {
            float ax = Math.Abs(v.X), ay = Math.Abs(v.Y), az = Math.Abs(v.Z);
            int avoidIdx = -1;
            if (Math.Abs(avoid.X) > 0.5f) avoidIdx = 0;
            else if (Math.Abs(avoid.Y) > 0.5f) avoidIdx = 1;
            else if (Math.Abs(avoid.Z) > 0.5f) avoidIdx = 2;

            if (avoidIdx == 0) ax = -1f;
            if (avoidIdx == 1) ay = -1f;
            if (avoidIdx == 2) az = -1f;

            if (ax >= ay && ax >= az) return new Vec3(v.X >= 0 ? 1 : -1, 0, 0);
            if (ay >= ax && ay >= az) return new Vec3(0, v.Y >= 0 ? 1 : -1, 0);
            return new Vec3(0, 0, v.Z >= 0 ? 1 : -1);
        }

        // ── Projection paint ──────────────────────────────────────
        // The size slider is one thing for every drawing tool: the stroke's width in px
        // (diameter), so 28 looks equally wide with the brush, pencil, pen, marker and
        // eraser. Tools differ only in character (soft halo, grain, crisp edge, flat
        // translucent band), not in how thick they are. `radius` is the half-width.
        private void BrushParams(out int radius, out bool soft)
        {
            radius = Math.Max(1, (int)Math.Round(BrushRadius / 2f));
            switch (PaintKind)
            {
                case BrushKind.Pencil: case BrushKind.Pen: case BrushKind.ObjectEraser: soft = false; break;
                default: soft = true; break;
            }
        }

        // The stroke radius as the ink journal stores it (layer px). The pen's journal
        // entry holds its full width instead of a radius (see StrokeOp.DrawStroke).
        private int CanvasStrokeRadius(Bitmap layer)
        {
            float d = BrushRadius * Material.PaintScale(layer);
            return Math.Max(1, (int)Math.Round(PaintKind == BrushKind.Pen ? d : d / 2f));
        }

        // Stamp a single dab (no repaint — callers repaint once per stroke segment).
        private void PaintDab(Point pt)
        {
            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv, out Vec3 P, out Vec3 n)) return;
            BrushParams(out int radius, out bool soft);
            bool erase = PaintKind == BrushKind.Eraser;
            Bitmap layer = PaintLayerFor(obj);
            _lastPaintMaterial = obj.Material;

            if (obj.PrimitiveType == "Canvas")
            {
                // Sheet maps 1:1 onto the texture. Draw a connected round-capped
                // segment from the stroke's previous point — separate dabs showed
                // up as a chain of circles, this is a solid line.
                // Brush sizes are in 1024-layer px; the sheet is finer, so scale.
                int r = CanvasStrokeRadius(layer);
                if (_strokeLayer != layer) { BeginCanvasStroke(layer, obj.Material); RulerBeginStroke(); }
                _strokeRadius = r;
                // Along the ruler's edge the pen runs dead straight (see SceneView.Ruler).
                PointF atPen = RulerConstrain(new PointF(uv.X * layer.Width, uv.Y * layer.Height), obj);
                if (AddCanvasStrokePoint(atPen, r, Material.PaintScale(layer)))
                    obj.Material.MarkPaintDirty(RenderCanvasStroke(layer, r));
                _strokeUv    = uv;
                _strokeLayer = layer;
                return;
            }
            else
            {
                // 3-D shapes: rasterize the screen-space brush footprint back into
                // each touched atlas triangle, so one dab stays continuous across edges.
                // Only the touched texels go to the GPU: a whole-atlas upload per dab made strokes crawl.
                Rectangle dirty = PaintBlob(obj, layer, P, n, pt, radius, soft, erase);
                if (!dirty.IsEmpty) obj.Material.MarkPaintDirty(dirty);
            }
        }

        // Rasterize one visible screen-space dab into every touched face-atlas triangle.
        // The stamp shape is tested after projecting the reconstructed surface point, so
        // adjacent triangles share one visual footprint instead of each getting its own
        // UV-space circle. Returns the changed texels (empty when nothing was touched).
        //
        // Cost follows the brush, not the mesh or the atlas: vertices are moved to camera
        // space once (lazily), the texel range of each triangle is clipped to the brush
        // circle, and only those texels are read and written in the locked bitmap. The
        // old version copied the whole 1024² layer out and back for every dab.
        private Vec3[]    _pbWorld = new Vec3[0], _pbCam = new Vec3[0];
        private PointF[]  _pbScr = new PointF[0];
        private int[]     _pbStamp = new int[0];
        private int       _pbEpoch;

        private Rectangle PaintBlob(SceneObject obj, Bitmap layer, Vec3 P, Vec3 n,
                                    Point screenCenter, int radiusPx, bool soft, bool erase)
        {
            var visibility = CaptureSurfaceMap();
            float worldR = WorldRadius(P, radiusPx);
            if (worldR < 1e-4f) worldR = 0.05f;

            var verts = obj.Mesh.Vertices;
            int faceCount = obj.Mesh.Faces.Count;
            int texW = layer.Width, texH = layer.Height;
            if (_pbStamp.Length < verts.Count)
            {
                int cap = Math.Max(verts.Count, _pbStamp.Length * 2);
                _pbWorld = new Vec3[cap]; _pbCam = new Vec3[cap]; _pbScr = new PointF[cap]; _pbStamp = new int[cap];
                _pbEpoch = 0;
            }
            if (++_pbEpoch == int.MaxValue) { Array.Clear(_pbStamp, 0, _pbStamp.Length); _pbEpoch = 1; }

            bool persp = _camera.Mode == ViewMode.Perspective;
            var xf = SceneGeometry.Xform.For(obj);   // trig once per object
            BitmapData data = null;
            int minX = texW, minY = texH, maxX = -1, maxY = -1;
            float cx = screenCenter.X, cy = screenCenter.Y;
            try
            {
                for (int fi = 0; fi < faceCount; fi++)
                {
                    var face = obj.Mesh.Faces[fi];
                    if (face == null || face.Length < 3) continue;
                    for (int i = 1; i + 1 < face.Length; i++)
                    {
                        int ia = face[0], ib = face[i], ic = face[i + 1];
                        PointF pa = BlobVertex(ia, verts, xf), pb = BlobVertex(ib, verts, xf), pc = BlobVertex(ic, verts, xf);
                        if (pa.X < -9000 || pb.X < -9000 || pc.X < -9000) continue;

                        if (cx < Math.Min(pa.X, Math.Min(pb.X, pc.X)) - radiusPx || cx > Math.Max(pa.X, Math.Max(pb.X, pc.X)) + radiusPx ||
                            cy < Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)) - radiusPx || cy > Math.Max(pa.Y, Math.Max(pb.Y, pc.Y)) + radiusPx) continue;

                        Vec3 a = _pbWorld[ia], b = _pbWorld[ib], c = _pbWorld[ic];
                        Vec3 fn = Vec3.Cross(b - a, c - a).Normalized();
                        if (Vec3.Dot(fn, n) < 0.2f) continue;          // skip faces facing away

                        Vec3 q = ClosestPointOnTri(P, a, b, c);
                        if ((q - P).Length > worldR * 1.35f) continue; // triangle out of range

                        PointF ua = SceneGeometry.PaintUV(obj, fi, faceCount, 0,     face.Length);
                        PointF ub = SceneGeometry.PaintUV(obj, fi, faceCount, i,     face.Length);
                        PointF uc = SceneGeometry.PaintUV(obj, fi, faceCount, i + 1, face.Length);
                        PointF ta = new PointF(ua.X * texW, ua.Y * texH);
                        PointF tb = new PointF(ub.X * texW, ub.Y * texH);
                        PointF tc = new PointF(uc.X * texW, uc.Y * texH);

                        // The triangle's texels, clipped to where the brush circle can fall on it.
                        float loX = Math.Min(ta.X, Math.Min(tb.X, tc.X)), hiX = Math.Max(ta.X, Math.Max(tb.X, tc.X));
                        float loY = Math.Min(ta.Y, Math.Min(tb.Y, tc.Y)), hiY = Math.Max(ta.Y, Math.Max(tb.Y, tc.Y));
                        BrushTexRange(pa, pb, pc, persp ? -_pbCam[ia].Z : 1f, persp ? -_pbCam[ib].Z : 1f, persp ? -_pbCam[ic].Z : 1f,
                                      ta, tb, tc, cx, cy, radiusPx, ref loX, ref hiX, ref loY, ref hiY);
                        int x0 = Math.Max(0, (int)Math.Floor(loX) - 1), x1 = Math.Min(texW - 1, (int)Math.Ceiling(hiX) + 1);
                        int y0 = Math.Max(0, (int)Math.Floor(loY) - 1), y1 = Math.Min(texH - 1, (int)Math.Ceiling(hiY) + 1);
                        if (x0 > x1 || y0 > y1) continue;

                        Vec3 ca = _pbCam[ia], cb = _pbCam[ib], cc = _pbCam[ic];
                        for (int y = y0; y <= y1; y++)
                        {
                            for (int x = x0; x <= x1; x++)
                            {
                                if (!PaintBary(ta, tb, tc, x + 0.5f, y + 0.5f, i, face.Length,
                                               out float l0, out float l1, out float l2)) continue;

                                // Camera space is an affine image of world space, so the barycentrics
                                // carry over: no per-texel rotation (and its trigonometry).
                                Vec3 camPoint = ca * l0 + cb * l1 + cc * l2;
                                PointF sp = _camera.ProjectCamera(camPoint, W, H);
                                if (sp.X < -9000 || !visibility.Visible(sp, camPoint.Z)) continue;

                                float dx = sp.X - cx;
                                float dy = sp.Y - cy;
                                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                                float cover = radiusPx + 0.5f - dist;
                                if (cover <= 0f) continue;
                                if (cover > 1f) cover = 1f;

                                int alpha = (int)((soft ? 235 : 255) * cover * BrushOpacity / 100.0);
                                if (alpha <= 0) continue;
                                if (data == null)
                                    data = layer.LockBits(new Rectangle(0, 0, texW, texH), ImageLockMode.ReadWrite,
                                                          System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                IntPtr at = data.Scan0 + y * data.Stride + x * 4;
                                Marshal.WriteInt32(at, BlendArgb(Marshal.ReadInt32(at), BrushColor, alpha, erase));
                                if (x < minX) minX = x; if (x > maxX) maxX = x;
                                if (y < minY) minY = y; if (y > maxY) maxY = y;
                            }
                        }
                    }
                }
            }
            finally { if (data != null) layer.UnlockBits(data); }
            return maxX < minX ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        // World, camera-space and screen position of a mesh vertex for the current dab, computed once.
        private PointF BlobVertex(int vi, List<Vec3> verts, SceneGeometry.Xform xf)
        {
            if (_pbStamp[vi] != _pbEpoch)
            {
                _pbStamp[vi] = _pbEpoch;
                Vec3 w = xf.Apply(verts[vi]);
                _pbWorld[vi] = w;
                _pbCam[vi] = _camera.ToCameraSpace(w);
                _pbScr[vi] = _camera.ProjectCamera(_pbCam[vi], W, H);
            }
            return _pbScr[vi];
        }

        // Narrows a triangle's texel box to the part under the brush circle: points of the circle
        // are carried back into the triangle with perspective-correct barycentrics (each weight
        // divided by its vertex depth). Any doubt (edge-on, behind the camera) keeps the full box.
        private static void BrushTexRange(PointF pa, PointF pb, PointF pc, float wa, float wb, float wc,
                                          PointF ta, PointF tb, PointF tc, float cx, float cy, float r,
                                          ref float loX, ref float hiX, ref float loY, ref float hiY)
        {
            float det = (pb.Y - pc.Y) * (pa.X - pc.X) + (pc.X - pb.X) * (pa.Y - pc.Y);
            if (Math.Abs(det) < 1e-3f || wa <= 1e-4f || wb <= 1e-4f || wc <= 1e-4f) return;
            float rr = r * 1.04f + 1f;   // the 12-gon sampled below sits inside the circle by up to 4 %
            float nx0 = float.MaxValue, nx1 = float.MinValue, ny0 = float.MaxValue, ny1 = float.MinValue;
            for (int k = 0; k < 12; k++)
            {
                double t = k * (2 * Math.PI / 12);
                float x = cx + rr * (float)Math.Cos(t), y = cy + rr * (float)Math.Sin(t);
                float l0 = ((pb.Y - pc.Y) * (x - pc.X) + (pc.X - pb.X) * (y - pc.Y)) / det;
                float l1 = ((pc.Y - pa.Y) * (x - pc.X) + (pa.X - pc.X) * (y - pc.Y)) / det;
                float a0 = l0 / wa, a1 = l1 / wb, a2 = (1f - l0 - l1) / wc;
                float sum = a0 + a1 + a2;
                if (!(sum > 1e-9f)) return;   // the circle reaches the plane's vanishing line
                float tx = (a0 * ta.X + a1 * tb.X + a2 * tc.X) / sum, ty = (a0 * ta.Y + a1 * tb.Y + a2 * tc.Y) / sum;
                if (tx < nx0) nx0 = tx; if (tx > nx1) nx1 = tx;
                if (ty < ny0) ny0 = ty; if (ty > ny1) ny1 = ty;
            }
            if (nx0 > loX) loX = nx0; if (nx1 < hiX) hiX = nx1;
            if (ny0 > loY) loY = ny0; if (ny1 < hiY) hiY = ny1;
        }

        private static bool BaryF(PointF a, PointF b, PointF c, float x, float y,
                                  out float la, out float lb, out float lc)
        {
            la = lb = lc = 0f;
            float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(det) < 1e-5f) return false;
            la = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det;
            lb = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det;
            lc = 1f - la - lb;
            const float e = -0.001f;
            return la >= e && lb >= e && lc >= e;
        }

        // `dst` with a dab of `alpha` over it (or taken off it, for the eraser).
        private static int BlendArgb(int dst, Color color, int alpha, bool erase)
        {
            if (alpha <= 0) return dst;
            if (alpha > 255) alpha = 255;

            int da = (dst >> 24) & 0xFF;
            int dr = (dst >> 16) & 0xFF;
            int dg = (dst >>  8) & 0xFF;
            int db =  dst        & 0xFF;

            if (erase)
            {
                int na = da * (255 - alpha) / 255;
                return na == 0 ? 0 : PackArgb(na, dr, dg, db);
            }

            int sa = alpha;
            int outA = sa + da * (255 - sa) / 255;
            if (outA <= 0) return 0;

            int premulDst = da * (255 - sa) / 255;
            int rr = (color.R * sa + dr * premulDst) / outA;
            int gg = (color.G * sa + dg * premulDst) / outA;
            int bb = (color.B * sa + db * premulDst) / outA;
            return PackArgb(outA, rr, gg, bb);
        }

        private static int PackArgb(int a, int r, int g, int b)
        {
            return unchecked((int)(
                ((uint)(a & 0xFF) << 24) |
                ((uint)(r & 0xFF) << 16) |
                ((uint)(g & 0xFF) <<  8) |
                 (uint)(b & 0xFF)));
        }

        // World-space radius matching a screen-pixel brush radius at the hit depth.
        private float WorldRadius(Vec3 P, int radiusPx)
        {
            if (_camera.Mode == ViewMode.Perspective)
            {
                float focal = Math.Min(W, H) * 0.72f;
                float depth = Math.Abs(_camera.GetDepth(P));
                return radiusPx * depth / Math.Max(1e-3f, focal);
            }
            float scale = Math.Min(W, H) / (_camera.OrthoScale * 2f);
            return radiusPx / Math.Max(1e-3f, scale);
        }

        // Closest point on triangle a,b,c to p (Ericson, Real-Time Collision Detection).
        private static Vec3 ClosestPointOnTri(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
        {
            Vec3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vec3.Dot(ab, ap), d2 = Vec3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vec3 bp = p - b;
            float d3 = Vec3.Dot(ab, bp), d4 = Vec3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float vv = d1 / (d1 - d3); return a + ab * vv; }

            Vec3 cp = p - c;
            float d5 = Vec3.Dot(ab, cp), d6 = Vec3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float ww = d2 / (d2 - d6); return a + ac * ww; }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float ww = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + (c - b) * ww;
            }

            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        // Stamp dabs densely along last→to so fast strokes stay continuous
        // (independent of the render frame rate).
        private void PaintLineTo(Point to)
        {
            // On the canvas the stroke is a smoothed path through the real mouse
            // samples: no interpolated dabs, one sample → one point.
            if (_strokeOnCanvas)
            {
                PaintDab(to);
                _lastPaintPt = to;
                Redraw();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
                return;
            }

            BrushParams(out int radius, out bool _);
            float spacing = Math.Max(1.5f, radius * 0.3f);
            float dx = to.X - _lastPaintPt.X, dy = to.Y - _lastPaintPt.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)(dist / spacing));
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                PaintDab(new Point((int)(_lastPaintPt.X + dx * t), (int)(_lastPaintPt.Y + dy * t)));
            }
            _lastPaintPt = to;
            Redraw();
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
        }

        private void DoEyedropper(Point pt)
        {
            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv)) return;
            Color c = obj.Material.Color;
            Bitmap layer = obj.Material.PaintLayer;
            if (layer != null)
            {
                int px = Math.Min(layer.Width  - 1, Math.Max(0, (int)(uv.X * layer.Width)));
                int py = Math.Min(layer.Height - 1, Math.Max(0, (int)(uv.Y * layer.Height)));
                Color p = layer.GetPixel(px, py);
                if (p.A > 8) c = Color.FromArgb(255, p.R, p.G, p.B);   // painted texel wins
            }
            BrushColor = c;
            BrushColorPicked?.Invoke(this, c);
            Notify?.Invoke(this, Loc.T("Цвет выбран"));
        }

        // Paint-bucket flood fill: replace the connected region of similar colour
        // under the cursor with the brush colour, stopping at drawn strokes (like
        // the fill tool in Paint).
        private void DoFill(Point pt)
        {
            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv)) return;
            Bitmap layer = PaintLayerFor(obj);
            _lastPaintMaterial = obj.Material;
            int w = layer.Width, h = layer.Height;
            int sx = Math.Min(w - 1, Math.Max(0, (int)(uv.X * w)));
            int sy = Math.Min(h - 1, Math.Max(0, (int)(uv.Y * h)));
            FillOp fill = FloodFill(layer, sx, sy, Color.FromArgb(255, BrushColor.R, BrushColor.G, BrushColor.B),
                (int)Math.Round(FillStrength * 2.55));
            // On the canvas a fill is an object of its own: erase it and what it covered returns.
            var ink = obj.PrimitiveType == "Canvas" ? obj.Material.Ink : null;
            if (fill != null && ink != null)
            {
                ink.Add(fill);
                if (ink.NeedsBake) ink.Bake(layer.Size);
            }
            obj.Material.MarkPaintDirty();
            Redraw();
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
        }

        // Returns the pixels it recoloured as a journal entry (null: nothing changed).
        private static FillOp FloodFill(Bitmap bmp, int sx, int sy, Color fill, int tol)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int[] px = new int[w * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);

            int target = px[sy * w + sx];
            int repl   = fill.ToArgb();
            byte[] mask = null;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            if (target != repl)
            {
                mask = new byte[w * h];
                var q = new Queue<int>();
                int start = sy * w + sx;
                px[start] = repl;
                mask[start] = 1;
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    int p = q.Dequeue();
                    int x = p % w, y = p / w;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (x > 0)     TryFill(px, mask, (y) * w + (x - 1), target, repl, tol, q);
                    if (x < w - 1) TryFill(px, mask, (y) * w + (x + 1), target, repl, tol, q);
                    if (y > 0)     TryFill(px, mask, (y - 1) * w + x,   target, repl, tol, q);
                    if (y < h - 1) TryFill(px, mask, (y + 1) * w + x,   target, repl, tol, q);
                }
            }

            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);
            if (mask == null) return null;
            return FillOp.FromMask(mask, w, h, Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1), repl);
        }

        private static void TryFill(int[] px, byte[] mask, int idx, int target, int repl, int tol, Queue<int> q)
        {
            if (px[idx] == repl) return;
            if (!ColorClose(px[idx], target, tol)) return;
            px[idx] = repl;
            mask[idx] = 1;
            q.Enqueue(idx);
        }

        private static bool ColorClose(int a, int b, int tol)
        {
            return Math.Abs(((a >> 24) & 0xFF) - ((b >> 24) & 0xFF)) <= tol
                && Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= tol
                && Math.Abs(((a >>  8) & 0xFF) - ((b >>  8) & 0xFF)) <= tol
                && Math.Abs(( a        & 0xFF) - ( b        & 0xFF)) <= tol;
        }

        private bool PickPaintTarget(Point pt, out SceneObject hitObj, out PointF uv)
        {
            return PickPaintTarget(pt, out hitObj, out uv, out _, out _);
        }

        // Full pick: also returns the 3-D surface point and the hit face normal, which
        // the brush uses to bleed a dab across all coplanar triangles of a face.
        private bool PickPaintTarget(Point pt, out SceneObject hitObj, out PointF uv,
                                     out Vec3 hitPoint, out Vec3 hitNormal)
        {
            hitObj = null; uv = PointF.Empty; hitPoint = Vec3.Zero; hitNormal = Vec3.Zero;
            if (_scene == null) return false;

            bool isPersp  = _camera.Mode == ViewMode.Perspective;
            Vec3 camPos   = isPersp ? _camera.GetPosition() : Vec3.Zero;
            Vec3 orthoDir = isPersp ? Vec3.Zero : _camera.GetOrthoViewDir();

            float bestDepth = float.NegativeInfinity;
            foreach (var top in _scene.Objects)
            {
                if (!top.IsVisible) continue;   // can't paint hidden objects
                foreach (var leaf in SceneGeometry.Leaves(top))
                    PaintPickLeaf(leaf, pt, isPersp, camPos, orthoDir, ref bestDepth,
                                  ref hitObj, ref uv, ref hitPoint, ref hitNormal);
            }
            return hitObj != null;
        }

        // Per-vertex scratch of a pick (camera space and screen), grown as needed.
        private Vec3[]   _ppCam = new Vec3[0];
        private PointF[] _ppScr = new PointF[0];
        private Vec3[]   _ppWorld = new Vec3[0];

        // The nearest surface point under the cursor on this object. Every triangle of every face
        // is judged on its own: facing, hit and — for "nearest" — the depth of the hit itself.
        // Face-wide shortcuts (the first three vertices for the normal, the average vertex depth)
        // are only right for flat convex faces; on a shape deformed in Shape mode a bent or
        // stretched face made them cull a visible face or let a far one win.
        private void PaintPickLeaf(SceneObject obj, Point pt, bool isPersp, Vec3 camPos, Vec3 orthoDir,
                                   ref float bestDepth, ref SceneObject best, ref PointF bestUv,
                                   ref Vec3 bestP, ref Vec3 bestN)
        {
            if (obj.Mesh == null || !obj.IsVisible || obj.PrimitiveType == Aplat.Primitives.RulerFactory.Type) return;   // the ruler is no canvas
            var verts = obj.Mesh.Vertices;
            int faceCount = obj.Mesh.Faces.Count;
            if (_ppCam.Length < verts.Count)
            {
                int cap = Math.Max(verts.Count, _ppCam.Length * 2);
                _ppCam = new Vec3[cap]; _ppScr = new PointF[cap]; _ppWorld = new Vec3[cap];
            }
            var xf = SceneGeometry.Xform.For(obj);   // trig once per object, not per vertex
            for (int i = 0; i < verts.Count; i++)
            {
                Vec3 w = xf.Apply(verts[i]);
                _ppWorld[i] = w;
                _ppCam[i] = _camera.ToCameraSpace(w);
                _ppScr[i] = _camera.ProjectCamera(_ppCam[i], W, H);
            }

            for (int fi = 0; fi < faceCount; fi++)
            {
                var face = obj.Mesh.Faces[fi];
                if (face == null || face.Length < 3) continue;
                int i0 = face[0];
                PointF p0 = _ppScr[i0];
                if (p0.X < -9000 || float.IsNaN(p0.X)) continue;

                for (int i = 1; i + 1 < face.Length; i++)
                {
                    int i1 = face[i], i2 = face[i + 1];
                    PointF p1 = _ppScr[i1], p2 = _ppScr[i2];
                    if (p1.X < -9000 || p2.X < -9000 || float.IsNaN(p1.X + p2.X)) continue;
                    if (pt.X < Math.Min(p0.X, Math.Min(p1.X, p2.X)) - 1f || pt.X > Math.Max(p0.X, Math.Max(p1.X, p2.X)) + 1f ||
                        pt.Y < Math.Min(p0.Y, Math.Min(p1.Y, p2.Y)) - 1f || pt.Y > Math.Max(p0.Y, Math.Max(p1.Y, p2.Y)) + 1f) continue;
                    if (!Bary(p0, p1, p2, pt, out float l0, out float l1, out float l2)) continue;

                    Vec3 a = _ppWorld[i0], b = _ppWorld[i1], c = _ppWorld[i2];
                    Vec3 n = Vec3.Cross(b - a, c - a);                      // this triangle's own facing

                    // Perspective-correct interpolation: weight each vertex by 1/w
                    // (w ∝ -camZ). Screen-space bary alone is affine and drifts off
                    // the cursor on tilted faces; this lands the dab under the crosshair
                    // (and makes painting on angled 3-D faces accurate). Ortho → w=1.
                    float z0 = _ppCam[i0].Z, z1 = _ppCam[i1].Z, z2 = _ppCam[i2].Z;
                    float w0 = isPersp ? Math.Max(1e-4f, -z0) : 1f;
                    float w1 = isPersp ? Math.Max(1e-4f, -z1) : 1f;
                    float w2 = isPersp ? Math.Max(1e-4f, -z2) : 1f;
                    float a0 = l0 / w0, a1 = l1 / w1, a2 = l2 / w2;
                    float inv = a0 + a1 + a2;
                    if (Math.Abs(inv) < 1e-9f) inv = 1f;
                    a0 /= inv; a1 /= inv; a2 /= inv;

                    float depth = a0 * z0 + a1 * z1 + a2 * z2;               // camera-space depth at the hit (larger = nearer)
                    if (depth <= bestDepth) continue;
                    Vec3 hit = a * a0 + b * a1 + c * a2;
                    // Facing is judged along the ray to the hit, not towards a corner: near the
                    // silhouette the two differ and a grazing triangle flickered in and out.
                    if (Vec3.Dot(n, isPersp ? camPos - hit : orthoDir) <= 0f) continue;

                    PointF u0 = SceneGeometry.PaintUV(obj, fi, faceCount, 0,     face.Length);
                    PointF u1 = SceneGeometry.PaintUV(obj, fi, faceCount, i,     face.Length);
                    PointF u2 = SceneGeometry.PaintUV(obj, fi, faceCount, i + 1, face.Length);
                    bestUv = new PointF(a0 * u0.X + a1 * u1.X + a2 * u2.X,
                                        a0 * u0.Y + a1 * u1.Y + a2 * u2.Y);
                    bestP = hit;
                    bestN = n.Normalized();
                    bestDepth = depth;
                    best      = obj;
                }
            }
        }

        private static bool Bary(PointF a, PointF b, PointF c, Point p,
                                 out float la, out float lb, out float lc)
        {
            la = lb = lc = 0f;
            float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(det) < 1e-5f) return false;
            la = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
            lb = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
            lc = 1f - la - lb;
            const float e = -0.002f;
            return la >= e && lb >= e && lc >= e;
        }

        // ── Canvas stroke engine ──────────────────────────────────
        // The layer a tool paints into: the canvas sheet at its own (finer)
        // resolution, 3-D shapes at the atlas resolution.
        private static Bitmap PaintLayerFor(SceneObject o)
        {
            if (o.PrimitiveType != "Canvas") return o.Material.EnsurePaintLayer(Material.SurfacePaintSize);
            var layer = o.Material.EnsurePaintLayer(Material.CanvasPaintSize);
            o.Material.EnsureInk();   // before anything is drawn: the journal must see the layer's first state
            return layer;
        }

        // The material last painted on; its PNG is encoded in the background when
        // the gesture ends (see Material.EncodePaintInBackground).
        private Material _lastPaintMaterial;

        private void FlushPaintEncoding()
        {
            if (_lastPaintMaterial == null) return;
            _lastPaintMaterial.EncodePaintInBackground();
            _lastPaintMaterial = null;
        }

        private void BeginCanvasStroke(Bitmap layer, Material material)
        {
            if (_strokeOnCanvas) EndCanvasStroke();   // a stroke carried onto another layer: close the first
            _strokeMaterial = material;
            _strokeStyle    = StyleOf(PaintKind);
            _strokeArgb     = BrushColor.ToArgb();
            _strokeOpacity  = BrushOpacity;
            if (_strokeBase == null || _strokeBase.Size != layer.Size)
            {
                _strokeBase?.Dispose();
                _strokeBase = new Bitmap(layer.Width, layer.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            int tx = (layer.Width + StrokeTile - 1) / StrokeTile, ty = (layer.Height + StrokeTile - 1) / StrokeTile;
            if (_strokeTiles == null || _strokeTiles.GetLength(0) != tx || _strokeTiles.GetLength(1) != ty)
                _strokeTiles = new bool[tx, ty];
            else
                Array.Clear(_strokeTiles, 0, _strokeTiles.Length);
            _strokePts.Clear();
            _strokeOnCanvas = true;
            _strokeSeed = Environment.TickCount;
        }

        // Copies into the snapshot every not-yet-saved tile under `area`, before the
        // stroke first draws there (the layer is still pristine in those tiles).
        private void SnapshotTiles(Bitmap layer, Rectangle area)
        {
            int x0 = area.Left / StrokeTile, x1 = (area.Right - 1) / StrokeTile;
            int y0 = area.Top / StrokeTile,  y1 = (area.Bottom - 1) / StrokeTile;
            using (var g = Graphics.FromImage(_strokeBase))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                for (int ty = y0; ty <= y1; ty++)
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        if (_strokeTiles[tx, ty]) continue;
                        var tile = Rectangle.Intersect(
                            new Rectangle(tx * StrokeTile, ty * StrokeTile, StrokeTile, StrokeTile),
                            new Rectangle(Point.Empty, layer.Size));
                        g.DrawImage(layer, tile, tile, GraphicsUnit.Pixel);
                        _strokeTiles[tx, ty] = true;
                    }
            }
        }

        private void EndCanvasStroke()
        {
            if (_strokeOnCanvas && _strokeMaterial != null && _strokeLayer != null && _strokePts.Count > 0)
                RecordStroke();
            // The snapshot bitmap is kept and reused by the next stroke.
            _strokePts.Clear();
            _strokeOnCanvas = false;
            _strokeUv       = null;
            _strokeLayer    = null;
            _strokeMaterial = null;
        }

        private static StrokeStyle StyleOf(BrushKind k)
        {
            switch (k)
            {
                case BrushKind.Pencil: return StrokeStyle.Pencil;
                case BrushKind.Pen:    return StrokeStyle.Pen;
                case BrushKind.Marker: return StrokeStyle.Marker;
                case BrushKind.Eraser: return StrokeStyle.Erase;
                default:               return StrokeStyle.Brush;
            }
        }

        // The finished stroke joins the layer's object journal.
        private void RecordStroke()
        {
            var ink = _strokeMaterial.Ink;
            if (ink == null) return;
            var layer = _strokeLayer;
            var pts = _strokePts.ToArray();
            ink.Add(new StrokeOp
            {
                Style = _strokeStyle, Radius = _strokeRadius, Scale = Material.PaintScale(layer),
                Argb = _strokeArgb, Opacity = _strokeOpacity, Seed = _strokeSeed, Pts = pts,
                Bounds = StrokeOp.BoundsFor(pts, _strokeRadius, layer.Size),
            });
            if (ink.NeedsBake) ink.Bake(layer.Size);
        }

        // Input conditioning, so the spline follows the gesture and not the hand's
        // tremor or the tiny twitch as the button is released:
        //  • samples closer than ~2 screen px / a fraction of the brush are skipped;
        //  • a short step that doubles back on the previous one (> ~110°) is dropped —
        //    through such a pair a Catmull-Rom spline draws a loop ("hook") at the end;
        //  • each kept point is pulled 40% toward the previous one (a light stabiliser).
        private bool AddCanvasStrokePoint(PointF p, int radius, float scale)
        {
            int n = _strokePts.Count;
            if (n > 0)
            {
                PointF last = _strokePts[n - 1];
                float dx = p.X - last.X, dy = p.Y - last.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                float min = Math.Max(2f * scale, radius * 0.3f);
                if (len < min) return false;

                if (n > 1)
                {
                    PointF prev = _strokePts[n - 2];
                    float px = last.X - prev.X, py = last.Y - prev.Y;
                    float plen = (float)Math.Sqrt(px * px + py * py);
                    float cos = (dx * px + dy * py) / Math.Max(1e-3f, len * plen);
                    if (cos < -0.35f && len < Math.Max(radius * 2f, 8f * scale)) return false;
                }

                p = new PointF(last.X + dx * 0.6f, last.Y + dy * 0.6f);
            }
            _strokePts.Add(p);
            return true;
        }

        // What a new point can change: the spline's last segments depend on the
        // last four points (Catmull-Rom support), plus the pen's half-width and a
        // margin for the curve bulging past its control points.
        private Rectangle StrokeDirtyArea(Bitmap layer, int radius)
        {
            int n = _strokePts.Count;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, seg = 0;
            for (int i = Math.Max(0, n - 4); i < n; i++)
            {
                PointF p = _strokePts[i];
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                if (i > 0)
                {
                    PointF q = _strokePts[i - 1];
                    seg = Math.Max(seg, (float)Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)));
                }
            }
            float pad = radius * 1.25f + 4f + seg * 0.3f;
            var r = Rectangle.FromLTRB((int)Math.Floor(x0 - pad), (int)Math.Floor(y0 - pad),
                                       (int)Math.Ceiling(x1 + pad), (int)Math.Ceiling(y1 + pad));
            r.Intersect(new Rectangle(0, 0, layer.Width, layer.Height));
            return r;
        }

        // Re-renders the stroke inside the area the newest point can affect and
        // returns that area: the snapshot is restored there, then the whole path is
        // drawn clipped to it — identical pixels to a full redraw, at local cost.
        private Rectangle RenderCanvasStroke(Bitmap layer, int radius)
        {
            if (_strokeBase == null || _strokePts.Count == 0) return Rectangle.Empty;
            if (radius < 1) radius = 1;
            Rectangle area = StrokeDirtyArea(layer, radius);
            if (area.IsEmpty) return area;
            SnapshotTiles(layer, area);
            using (var g = Graphics.FromImage(layer))
            {
                g.SetClip(area);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_strokeBase, area, area, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                StrokeOp.DrawStroke(g, _strokeStyle, _strokePts, radius, Material.PaintScale(layer),
                                    Color.FromArgb(_strokeArgb), _strokeOpacity, _strokeSeed, area);
            }
            return area;
        }

        // ── Rectangular selection on the canvas (Select2D tool) ───
        private void BeginSelectDrag(SceneObject canvas, PointF uv, Point screen)
        {
            _selTexelScale = TexelsPerScreenPixel(canvas, uv, screen);
            _selDragging  = true;
            _selStartUv   = uv;
            _selCurUv     = uv;
            _selCanvas  = canvas;
            var layer     = PaintLayerFor(canvas);   // also readies the journal, before the marquee marks the layer
            _selBaseLayer = new Bitmap(layer);
            DrawSelectMarquee();
        }

        // Paint-layer texels covered by one screen pixel at the given canvas point:
        // the marquee is drawn into the layer, so its line widths are scaled by this
        // to stay a constant, crisp thickness on screen at any zoom.
        private float _selTexelScale = 1f;

        private float TexelsPerScreenPixel(SceneObject canvas, PointF uv, Point screen)
        {
            const int probe = 16;
            var layer = canvas.Material.EnsurePaintLayer(Material.CanvasPaintSize);
            foreach (int dx in new[] { probe, -probe })
            {
                if (!PickPaintTarget(new Point(screen.X + dx, screen.Y), out SceneObject o, out PointF uv2) ||
                    !ReferenceEquals(o, canvas)) continue;
                double tx = (uv2.X - uv.X) * layer.Width, ty = (uv2.Y - uv.Y) * layer.Height;
                double texels = Math.Sqrt(tx * tx + ty * ty) / probe;
                if (texels > 0.01) return (float)Math.Max(0.5, Math.Min(16.0, texels));
            }
            return 1f;
        }

        private void DrawSelectMarquee()
        {
            if (_selCanvas == null || _selBaseLayer == null) return;
            var layer = _selCanvas.Material.EnsurePaintLayer(Material.CanvasPaintSize);
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_selBaseLayer, 0, 0, layer.Width, layer.Height);
                g.CompositingMode = CompositingMode.SourceOver;
                var r = UvRectToPx(layer, _selStartUv, _selCurUv);
                // Accent dashes over a solid white under-stroke: the marquee stays
                // readable over dark paint, light paint and the bare sheet alike.
                // Widths are in screen pixels (see TexelsPerScreenPixel); the dash
                // pattern is relative to the pen width, so it scales along.
                using (var under = new Pen(Aplat.UI.Theme.SelectionUnder, 3f * _selTexelScale))
                    g.DrawRectangle(under, r);
                using (var pen = new Pen(Aplat.UI.Theme.Selection, 1.5f * _selTexelScale))
                {
                    pen.DashPattern = new[] { 4f, 3f };
                    g.DrawRectangle(pen, r);
                }
            }
            _selCanvas.Material.MarkPaintDirty();
            Redraw();
        }

        private static Rectangle UvRectToPx(Bitmap layer, PointF a, PointF b)
        {
            int x1 = (int)(Math.Min(a.X, b.X) * layer.Width);
            int y1 = (int)(Math.Min(a.Y, b.Y) * layer.Height);
            int x2 = (int)(Math.Max(a.X, b.X) * layer.Width);
            int y2 = (int)(Math.Max(a.Y, b.Y) * layer.Height);
            return Rectangle.FromLTRB(
                Math.Max(0, x1), Math.Max(0, y1),
                Math.Min(layer.Width - 1, x2), Math.Min(layer.Height - 1, y2));
        }

        // Mouse-up of the rubber band: cut the region out into a floating fragment.
        private void EndSelectDrag()
        {
            _selDragging = false;
            if (_selCanvas == null || _selBaseLayer == null) return;
            var layer = _selCanvas.Material.EnsurePaintLayer(Material.CanvasPaintSize);
            var r = UvRectToPx(layer, _selStartUv, _selCurUv);

            // Restore the clean layer first (drop the marquee pixels).
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_selBaseLayer, 0, 0, layer.Width, layer.Height);
            }

            if (r.Width < 4 || r.Height < 4)
            {
                _selBaseLayer.Dispose(); _selBaseLayer = null;
                _selCanvas.Material.MarkPaintDirty();
                _selCanvas = null;
                LiveCancelled?.Invoke(this, EventArgs.Empty);   // nothing cut: no undo step
                Redraw();
                return;
            }

            // The region becomes a live image: move, resize, rotate; Esc puts it back.
            var canvas = _selCanvas;
            var preCut = _selBaseLayer;
            _selBaseLayer = null;
            _selCanvas    = null;
            BeginLiveCut(canvas, r, preCut);
        }

        // ── Shape tools ───────────────────────────────────────────
        private static bool IsShapeTool(BrushKind k)
        {
            return k == BrushKind.ShapeLine || k == BrushKind.ShapeRect ||
                   k == BrushKind.ShapeEllipse || k == BrushKind.ShapeTriangle;
        }

        // ── Frame capture (GL ReadPixels) ─────────────────────────
        public Bitmap CaptureFrame()
        {
            if (!_glLoaded || _scene == null) return null;
            _gl.MakeCurrent();
            int w = _gl.ClientSize.Width, h = _gl.ClientSize.Height;
            if (w <= 0 || h <= 0) return null;

            // Render into the back buffer and read it back without swapping, so the
            // on-screen view doesn't flicker.
            GL.ReadBuffer(ReadBufferMode.Back);
            _glr.Render(_scene, w, h, _renderer);
            if (_selected.Count > 0) DrawGizmoGL();
            GL.Finish();

            // Read into a 32-bit surface: its stride is always width*4 (a multiple of 4),
            // so it matches OpenGL's packed rows with no padding mismatch (which is what
            // produced the diagonal/RGB-striped corruption when reading into 24bpp).
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var bd  = bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
            GL.ReadPixels(0, 0, w, h, OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
                PixelType.UnsignedByte, bd.Scan0);
            bmp.UnlockBits(bd);
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);   // GL origin is bottom-left
            return bmp;
        }

        // A full camera turn around the scene, frame by frame. Each snapshot is handed
        // to the caller (who saves and disposes it); the camera angle and mode are
        // restored at the end — even if saving failed with an error.
        public void CaptureTurntable(int frames, Action<int, Bitmap> onFrame)
        {
            if (onFrame == null || !_glLoaded || _scene == null) return;
            if (frames < 2) frames = 2;
            if (frames > 720) frames = 720;

            ViewMode mode0 = _camera.Mode;
            float angle0 = _camera.AngleY;
            Vec3 target0 = _camera.Target;
            bool spin0 = _spin;
            _spin = false;
            _camera.Mode = ViewMode.Perspective;   // a full turn only makes sense in perspective

            // The frames circle the object (or the scene), not an arbitrary look-at point.
            Vec3 pivot;
            if (SpinPivot(out pivot)) _camera.Target = pivot;
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    _camera.AngleY = angle0 + 360f * i / frames;
                    var bmp = CaptureFrameClean();
                    if (bmp == null) break;
                    onFrame(i, bmp);
                }
            }
            finally
            {
                _camera.Mode  = mode0;
                _camera.AngleY = angle0;
                _camera.Target = target0;
                _spin = spin0;
                Redraw();
                NotifyViewChanged();
            }
        }

        // A frame free of selection marks — no gizmo, no selection outlines: they must
        // not appear in the exported series. The flags are restored in the finally block.
        private Bitmap CaptureFrameClean()
        {
            var keep  = new List<SceneObject>(_selected);
            var flags = new List<SceneObject>();
            CollectSelected(_scene.Objects, flags);
            foreach (var o in flags) o.IsSelected = false;
            _selected.Clear();
            try { return CaptureFrame(); }
            finally
            {
                foreach (var o in flags) o.IsSelected = true;
                _selected.AddRange(keep);
            }
        }

        private static void CollectSelected(IEnumerable<SceneObject> objs, List<SceneObject> into)
        {
            foreach (var o in objs)
            {
                if (o.IsSelected) into.Add(o);
                CollectSelected(o.Children, into);
            }
        }

        // ── Helpers ───────────────────────────────────────────────
        private PointF Proj(Vec3 v) => _camera.Project(v, W, H);

        private void ClearAllSel()
        {
            if (_scene == null) return;
            foreach (var o in _scene.Objects) ClearSel(o);
        }

        private static void ClearSel(SceneObject o)
        {
            o.IsSelected = false;
            foreach (var c in o.Children) ClearSel(c);
        }

        private static bool PointInPoly(PointF[] poly, Point p)
        {
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                    (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) /
                           (poly[j].Y - poly[i].Y) + poly[i].X))
                    inside = !inside;
            }
            return inside;
        }

        private static float DistPP(PointF a, Point b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DistPP(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DistSeg(PointF a, PointF b, Point p)
        {
            float dx   = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float cx, cy;
            if (len2 < 0.01f) { cx = a.X; cy = a.Y; }
            else
            {
                float t = Math.Max(0f, Math.Min(1f,
                    ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
                cx = a.X + t * dx;
                cy = a.Y + t * dy;
            }
            return (float)Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        }

        // ── View mode ─────────────────────────────────────────────
        public void SetViewMode(ViewMode mode)
        {
            _camera.Mode = mode;
            switch (mode)
            {
                case ViewMode.Top:   _camera.AngleX =  89f; _camera.AngleY =   0f; break;
                case ViewMode.Front: _camera.AngleX =   0f; _camera.AngleY =   0f; break;
                case ViewMode.Side:  _camera.AngleX =   0f; _camera.AngleY =  90f; break;
                default:             _camera.AngleX =  25f; _camera.AngleY = -40f; break;
            }
            Redraw();
            NotifyViewChanged();
        }

        public void Refresh3D() { Redraw(); }

        // Frame the selected object(s) — centre the camera on them and pull back just
        // enough to fit (like F in UE5). With nothing selected, frames the whole scene.
        public void FocusSelection()
        {
            if (_scene == null) return;
            FocusOn(_selected.Count > 0 ? _selected : (IEnumerable<SceneObject>)_scene.Objects);
        }

        // Frame the whole scene regardless of the current selection (Home key).
        public void FocusAll()
        {
            if (_scene == null) return;
            FocusOn(_scene.Objects);
        }

        private void FocusOn(IEnumerable<SceneObject> targets)
        {
            Vec3 min = new Vec3( float.MaxValue,  float.MaxValue,  float.MaxValue);
            Vec3 max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            bool any = false;
            foreach (var o in targets)
            {
                if (o == null || !o.IsVisible) continue;
                if (!SceneGeometry.WorldBounds(o, out Vec3 bmin, out Vec3 bmax)) continue;
                if (bmin.X < min.X) min.X = bmin.X; if (bmax.X > max.X) max.X = bmax.X;
                if (bmin.Y < min.Y) min.Y = bmin.Y; if (bmax.Y > max.Y) max.Y = bmax.Y;
                if (bmin.Z < min.Z) min.Z = bmin.Z; if (bmax.Z > max.Z) max.Z = bmax.Z;
                any = true;
            }
            if (!any) return;

            Vec3 center = (min + max) * 0.5f;
            float radius = ((max - min) * 0.5f).Length;
            _camera.Frame(center, radius);
            Redraw();
            NotifyViewChanged();
        }
    }
}
