using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    // Screen-space overlays drawn over the rendered scene:
    //  • the brush cursor — a ring the size of the stroke the tool will lay down;
    //  • the navigation gizmo — Blender-style axis widget in the top-right corner,
    //    turning with the camera; clicking an axis snaps to that view.
    public partial class SceneView
    {
        // ── Shared: 2-D pixel projection for overlay drawing ──────
        private void BeginScreenOverlay()
        {
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, W, H, 0, -1, 1);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();
        }

        private static void EndScreenOverlay()
        {
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.Enable(EnableCap.DepthTest);
        }

        private static void Circle(PrimitiveType type, float cx, float cy, float r, int segs = 48)
        {
            GL.Begin(type);
            if (type == PrimitiveType.TriangleFan) GL.Vertex2(cx, cy);
            int n = type == PrimitiveType.TriangleFan ? segs + 1 : segs;
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / segs;
                GL.Vertex2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
            }
            GL.End();
        }

        // ── Brush cursor ──────────────────────────────────────────
        private Point? _hoverPt;

        private bool BrushCursorActive
        {
            get
            {
                if (_tool != ToolMode.Paint || !_hoverPt.HasValue) return false;
                switch (PaintKind)
                {
                    case BrushKind.Brush: case BrushKind.Pencil: case BrushKind.Pen:
                    case BrushKind.Marker: case BrushKind.Eraser: case BrushKind.ObjectEraser:
                        return true;
                    default:
                        return false;
                }
            }
        }

        // Half-width of the stroke on screen, in px, where the cursor is; 0 = not over paint.
        private float BrushCursorRadius(Point pt)
        {
            // Half of the size in px, the same for every tool (matches RenderCanvasStroke / PaintBlob).
            float half = BrushRadius / 2f;

            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv)) return 0f;
            if (obj.PrimitiveType != "Canvas") return half;   // 3-D shapes: radius is in screen px

            // Canvas: sizes are in 1024-layer px, i.e. uv * 1024. Measure how much uv one
            // screen pixel covers here (probe 16px aside) and convert.
            const int probe = 16;
            foreach (int dx in new[] { probe, -probe })
            {
                if (!PickPaintTarget(new Point(pt.X + dx, pt.Y), out SceneObject o2, out PointF uv2) ||
                    !ReferenceEquals(o2, obj)) continue;
                double du = (uv2.X - uv.X) * Material.PaintSize, dv = (uv2.Y - uv.Y) * Material.PaintSize;
                double texPerPx = Math.Sqrt(du * du + dv * dv) / probe;
                if (texPerPx > 1e-4) return (float)(half / texPerPx);
            }
            return half;
        }

        private void DrawBrushCursorGL()
        {
            if (!BrushCursorActive) return;
            Point pt = _hoverPt.Value;
            float r = BrushCursorRadius(pt);
            if (r <= 0f) return;
            r = Math.Max(1.5f, r);

            BeginScreenOverlay();
            GL.Enable(EnableCap.LineSmooth);
            // Dark ring under a light ring: readable on white paper, dark paint and
            // the Graphite background alike.
            GL.LineWidth(3f);
            GL.Color4(0f, 0f, 0f, 0.45f);
            Circle(PrimitiveType.LineLoop, pt.X + 0.5f, pt.Y + 0.5f, r);
            GL.LineWidth(1.25f);
            GL.Color4(1f, 1f, 1f, 0.95f);
            Circle(PrimitiveType.LineLoop, pt.X + 0.5f, pt.Y + 0.5f, r);
            GL.Disable(EnableCap.LineSmooth);
            EndScreenOverlay();
        }

        private void TrackHover(Point pt)
        {
            bool had = BrushCursorActive || _navHover >= 0;
            _hoverPt = pt;
            int hover = NavGizmoHit(pt);
            bool changed = hover != _navHover;
            _navHover = hover;
            if (changed && _gl != null) _gl.Cursor = hover >= 0 ? Cursors.Hand : ToolCursor(_tool);
            if (had || changed || BrushCursorActive) Redraw();
        }

        private void ClearHover()
        {
            bool had = BrushCursorActive || _navHover >= 0;
            _hoverPt = null;
            _navHover = -1;
            if (had) Redraw();
        }

        // ── Navigation gizmo ──────────────────────────────────────
        // Axis order: +X, +Y, +Z, −X, −Y, −Z. Colours as in Blender (X red, Y green,
        // Z blue); positive ends are solid discs with a letter, negative ones rings.
        private const float GizmoRadius = 42f, GizmoBubble = 9f, GizmoMargin = 16f;
        private int _navHover = -1;   // hovered axis index, 6 = centre, -1 = none

        public event EventHandler<ViewMode> ViewAxisClicked;

        private static readonly Vec3[] NavGizmoAxes =
        {
            new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1),
            new Vec3(-1, 0, 0), new Vec3(0, -1, 0), new Vec3(0, 0, -1)
        };

        private static readonly float[][] NavGizmoColors =
        {
            new[] { 0.96f, 0.26f, 0.34f }, new[] { 0.53f, 0.80f, 0.16f }, new[] { 0.26f, 0.55f, 0.96f }
        };

        private PointF NavGizmoCenter { get { return new PointF(W - GizmoMargin - GizmoRadius, GizmoMargin + GizmoRadius); } }

        // Screen offset (px) and depth of each axis end; depth > 0 = towards the viewer.
        private void NavGizmoLayout(out PointF[] pos, out float[] depth)
        {
            pos = new PointF[6];
            depth = new float[6];
            Vec3 o = _camera.ToCameraSpace(_camera.Target);
            PointF c = NavGizmoCenter;
            for (int i = 0; i < 6; i++)
            {
                Vec3 d = _camera.ToCameraSpace(_camera.Target + NavGizmoAxes[i]) - o;
                float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                if (len < 1e-6f) len = 1f;
                float reach = GizmoRadius - GizmoBubble - 2f;
                pos[i]   = new PointF(c.X + d.X / len * reach, c.Y - d.Y / len * reach);
                depth[i] = d.Z / len;
            }
        }

        private int NavGizmoHit(Point pt)
        {
            PointF c = NavGizmoCenter;
            float dx = pt.X - c.X, dy = pt.Y - c.Y;
            if (dx * dx + dy * dy > GizmoRadius * GizmoRadius) return -1;
            NavGizmoLayout(out PointF[] pos, out float[] depth);
            // Front-most bubble under the cursor wins.
            int best = -1; float bestDepth = float.MinValue;
            for (int i = 0; i < 6; i++)
            {
                float ex = pt.X - pos[i].X, ey = pt.Y - pos[i].Y;
                if (ex * ex + ey * ey <= (GizmoBubble + 2) * (GizmoBubble + 2) && depth[i] > bestDepth)
                { best = i; bestDepth = depth[i]; }
            }
            return best >= 0 ? best : 6;   // inside the disc but on no bubble = centre
        }

        // Axis → the app's standard views (the camera has no "back" views, so both
        // ends of an axis snap to its view); the centre is handled by the caller.
        private static ViewMode NavGizmoView(int hit)
        {
            switch (hit % 3)
            {
                case 0:  return ViewMode.Side;
                case 1:  return ViewMode.Top;
                default: return ViewMode.Front;
            }
        }

        // The gizmo is a handle, as in Blender: a click on an axis snaps to its view (the
        // middle: perspective), dragging anywhere on it orbits the camera. A press waits
        // for a few pixels of movement to tell the two apart.
        public event EventHandler NavLeftOrthographic;   // a drag took the camera out of a flat view

        private bool _navPress, _navDragging;
        private Point _navPressPt;
        private int _navPressHit;

        // Press on the gizmo; true if it took the press.
        private bool BeginNavGizmoPress(Point pt)
        {
            int hit = NavGizmoHit(pt);
            if (hit < 0) return false;
            _navPress = true; _navDragging = false;
            _navPressPt = pt; _navPressHit = hit;
            return true;
        }

        // Moves while the press is the gizmo's; true if it was.
        private bool NavGizmoDrag(Point pt, float dx, float dy)
        {
            if (!_navPress) return false;
            if (!_navDragging)
            {
                if (Math.Abs(pt.X - _navPressPt.X) + Math.Abs(pt.Y - _navPressPt.Y) < 4) return true;
                _navDragging = true;
                // A flat view cannot be turned: leave it for perspective, same orientation.
                if (_camera.Mode != ViewMode.Perspective)
                {
                    _camera.Mode = ViewMode.Perspective;
                    NavLeftOrthographic?.Invoke(this, EventArgs.Empty);
                }
                dx = pt.X - _navPressPt.X; dy = pt.Y - _navPressPt.Y;   // the movement that decided it counts too
            }
            _camera.AngleY += dx * 0.85f;
            _camera.AngleX  = Math.Max(-89f, Math.Min(89f, _camera.AngleX + dy * 0.6f));
            Redraw();
            NotifyViewChanged();
            return true;
        }

        // Release: without a drag it was a click.
        private void EndNavGizmoPress()
        {
            if (!_navPress) return;
            bool click = !_navDragging;
            int hit = _navPressHit;
            _navPress = false; _navDragging = false;
            if (!click) return;
            var h = ViewAxisClicked;
            if (h != null) h(this, hit == 6 ? ViewMode.Perspective : NavGizmoView(hit));
        }

        private void DrawNavGizmoGL()
        {
            NavGizmoLayout(out PointF[] pos, out float[] depth);
            PointF c = NavGizmoCenter;

            BeginScreenOverlay();
            GL.Enable(EnableCap.LineSmooth);

            // Backdrop disc only while hovered — the widget stays out of the way.
            if (_navHover >= 0)
            {
                bool dark = Aplat.UI.Theme.IsDark;
                GL.Color4(dark ? 1f : 0f, dark ? 1f : 0f, dark ? 1f : 0f, 0.08f);
                Circle(PrimitiveType.TriangleFan, c.X, c.Y, GizmoRadius);
            }

            // Painter's order: far axes first.
            var order = new[] { 0, 1, 2, 3, 4, 5 };
            Array.Sort(order, (a, b) => depth[a].CompareTo(depth[b]));

            foreach (int i in order)
            {
                float[] col = NavGizmoColors[i % 3];
                bool positive = i < 3, hot = _navHover == i;
                if (positive)
                {
                    GL.LineWidth(2f);
                    GL.Color4(col[0], col[1], col[2], 0.9f);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex2(c.X, c.Y);
                    GL.Vertex2(pos[i].X, pos[i].Y);
                    GL.End();
                }

                float r = GizmoBubble * (positive ? 1f : 0.8f);
                if (positive || hot)
                {
                    GL.Color4(col[0], col[1], col[2], positive ? 1f : 0.55f);
                    Circle(PrimitiveType.TriangleFan, pos[i].X, pos[i].Y, r, 24);
                }
                GL.LineWidth(1.5f);
                GL.Color4(col[0], col[1], col[2], positive ? 1f : 0.85f);
                Circle(PrimitiveType.LineLoop, pos[i].X, pos[i].Y, r, 24);
                if (hot)
                {
                    GL.Color4(1f, 1f, 1f, 0.95f);
                    Circle(PrimitiveType.LineLoop, pos[i].X, pos[i].Y, r + 1.5f, 24);
                }
                if (positive) DrawAxisLetter(i, pos[i]);
            }

            GL.Disable(EnableCap.LineSmooth);
            EndScreenOverlay();
        }

        // X / Y / Z as tiny stroked glyphs (no GL text rendering needed).
        private static void DrawAxisLetter(int axis, PointF p)
        {
            const float s = 3.2f;
            GL.LineWidth(1.6f);
            GL.Color4(0.08f, 0.08f, 0.1f, 1f);
            GL.Begin(PrimitiveType.Lines);
            switch (axis)
            {
                case 0: // X
                    GL.Vertex2(p.X - s, p.Y - s); GL.Vertex2(p.X + s, p.Y + s);
                    GL.Vertex2(p.X + s, p.Y - s); GL.Vertex2(p.X - s, p.Y + s);
                    break;
                case 1: // Y
                    GL.Vertex2(p.X - s, p.Y - s); GL.Vertex2(p.X, p.Y);
                    GL.Vertex2(p.X + s, p.Y - s); GL.Vertex2(p.X, p.Y);
                    GL.Vertex2(p.X, p.Y);         GL.Vertex2(p.X, p.Y + s);
                    break;
                default: // Z
                    GL.Vertex2(p.X - s, p.Y - s); GL.Vertex2(p.X + s, p.Y - s);
                    GL.Vertex2(p.X + s, p.Y - s); GL.Vertex2(p.X - s, p.Y + s);
                    GL.Vertex2(p.X - s, p.Y + s); GL.Vertex2(p.X + s, p.Y + s);
                    break;
            }
            GL.End();
        }
    }
}
