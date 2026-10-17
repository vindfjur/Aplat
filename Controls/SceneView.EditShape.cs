using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    // The "Edit shape" mode: the selected object shows its mesh points and edges; a click picks
    // a point, an edge or a face and a drag moves it (see Models.MeshEditor for the logic).
    public partial class SceneView
    {
        private const float EditPointRadius = 10f, EditEdgeRadius = 7f;   // pick distances, px

        private MeshEditor _edit;
        private SceneObject _editObj;
        private SceneGeometry.Xform _editXf;
        private Vec3[] _editWorld = new Vec3[0];   // world position of every welded point, refilled each frame

        private bool   _editDrag, _editMoved;
        private float  _editVx, _editVy;           // the pointer's travel since the press (Shift slows it)
        private Vec3   _editCtrW, _editRefW;       // picked centre / first picked point in world space at the press
        private Vec3   _editRight, _editUp;        // camera axes in world space
        private float  _editWpp;                   // world units per pixel at the picked centre

        [DllImport("user32.dll")] private static extern short GetKeyState(int vKey);

        // The object the mode edits: exactly one plain, visible mesh object. Null otherwise.
        private SceneObject EditTarget()
        {
            if (_tool != ToolMode.EditShape || _selected.Count != 1) return null;
            var o = _selected[0];
            if (o == null || o.IsGroup || !o.IsVisible || o.Children.Count > 0) return null;
            if (o.PrimitiveType == "Canvas" || o.PrimitiveType == "Text") return null;
            if (o.Mesh == null || o.Mesh.Vertices.Count == 0 || o.Mesh.Faces.Count == 0) return null;
            return o;
        }

        private MeshEditor EnsureEditor(SceneObject o)
        {
            if (_edit == null || !ReferenceEquals(_editObj, o) || !_edit.Matches(o.Mesh))
            {
                _edit = new MeshEditor(o.Mesh);
                _editObj = o;
                _editWorld = new Vec3[_edit.PointCount];
            }
            return _edit;
        }

        private void EditReset()
        {
            if (_edit != null) _edit.ClearSelection();
            _editDrag = false;
            _edit = null;
            _editObj = null;
        }

        private Vec3 EditScreen(Vec3 local)
        {
            Vec3 cam = _camera.ToCameraSpace(_editXf.Apply(local));
            PointF s = _camera.ProjectCamera(cam, W, H);
            return new Vec3(s.X, s.Y, cam.Z);
        }

        private GizmoAxis EditHitGizmo(Point pt)
        {
            var o = EditTarget();
            if (o == null || o.IsLocked || _edit == null || !_edit.HasSelection || !_edit.Matches(o.Mesh)) return GizmoAxis.None;
            _editXf = SceneGeometry.Xform.For(o);
            Vec3 ctr = _editXf.Apply(_edit.PickedCenter());
            PointF sc = Proj(ctr);
            if (sc.X < -9000) return GizmoAxis.None;
            if (DistPP(sc, pt) < EditPointRadius) return GizmoAxis.None;   // on the picked point itself: a new pick, not a handle
            PointF px = Proj(ctr + new Vec3(GizmoLen, 0, 0));
            PointF py = Proj(ctr + new Vec3(0, GizmoLen, 0));
            PointF pz = Proj(ctr + new Vec3(0, 0, GizmoLen));
            if (AxisVisible(GizmoAxis.X) && px.X > -9000 && DistPP(px, pt) < HitTip) return GizmoAxis.X;
            if (AxisVisible(GizmoAxis.Y) && py.X > -9000 && DistPP(py, pt) < HitTip) return GizmoAxis.Y;
            if (AxisVisible(GizmoAxis.Z) && pz.X > -9000 && DistPP(pz, pt) < HitTip) return GizmoAxis.Z;
            if (AxisVisible(GizmoAxis.X) && px.X > -9000 && DistSeg(sc, px, pt) < HitShaft) return GizmoAxis.X;
            if (AxisVisible(GizmoAxis.Y) && py.X > -9000 && DistSeg(sc, py, pt) < HitShaft) return GizmoAxis.Y;
            if (AxisVisible(GizmoAxis.Z) && pz.X > -9000 && DistSeg(sc, pz, pt) < HitShaft) return GizmoAxis.Z;
            return GizmoAxis.None;
        }

        // ── Mouse ─────────────────────────────────────────────────

        private void EditMouseDown(MouseEventArgs e)
        {
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            var o = EditTarget();
            if (o == null)
            {
                if (_selected.Count == 1 && !_selected[0].IsGroup && _selected[0].Children.Count == 0 &&
                    (_selected[0].PrimitiveType == "Canvas" || _selected[0].PrimitiveType == "Text") &&
                    PickTopLevel(e.Location) == _selected[0])
                {
                    Notify?.Invoke(this, Loc.T("Правка формы: этот объект править нельзя"));
                    return;
                }
                if (_selected.Count > 1 || (_selected.Count == 1 && (_selected[0].IsGroup || _selected[0].Children.Count > 0)))
                    Notify?.Invoke(this, Loc.T("Правка формы: выберите один объект, не группу"));
                _edit = null;
                HandleClick(e.Location, ctrl);
                return;
            }
            if (o.IsLocked) { Notify?.Invoke(this, Loc.T("Объект заблокирован")); return; }

            var ed = EnsureEditor(o);
            _editXf = SceneGeometry.Xform.For(o);

            GizmoAxis hit = ed.HasSelection ? EditHitGizmo(e.Location) : GizmoAxis.None;
            if (hit == GizmoAxis.None)
            {
                int index;
                MeshPick kind = ed.Pick(EditScreen, e.Location.X, e.Location.Y, EditPointRadius, EditEdgeRadius, out index);
                if (kind == MeshPick.Vertex)    ed.SelectPoint(index);
                else if (kind == MeshPick.Edge) ed.SelectEdge(index);
                else if (kind == MeshPick.Face) ed.SelectFace(index);
                else
                {
                    ed.ClearSelection();
                    HandleClick(e.Location, ctrl);
                    Redraw();
                    return;
                }
            }
            BeginEditDrag(o, ed, hit);
            Redraw();
        }

        private void BeginEditDrag(SceneObject o, MeshEditor ed, GizmoAxis axis)
        {
            if (!ed.BeginDrag()) return;
            _editDrag  = true;
            _editMoved = false;
            _editVx = _editVy = 0f;
            _activeAxis = axis;
            _editCtrW = _editXf.Apply(ed.PickedCenter());
            _editRefW = _editXf.Apply(ed.DragStartOfFirst);

            // Camera axes in world space (rows of the view rotation), and the size of a pixel at the picked centre.
            Vec3 t = _camera.Target;
            Vec3 c0 = _camera.ToCameraSpace(t);
            Vec3 cx = _camera.ToCameraSpace(t + new Vec3(1, 0, 0)) - c0;
            Vec3 cy = _camera.ToCameraSpace(t + new Vec3(0, 1, 0)) - c0;
            Vec3 cz = _camera.ToCameraSpace(t + new Vec3(0, 0, 1)) - c0;
            _editRight = new Vec3(cx.X, cy.X, cz.X).Normalized();
            _editUp    = new Vec3(cx.Y, cy.Y, cz.Y).Normalized();
            float min = Math.Min(W, H);
            if (_camera.Mode == ViewMode.Perspective)
            {
                float depth = -_camera.ToCameraSpace(_editCtrW).Z;
                _editWpp = Math.Max(depth, 0.05f) / (min * 0.72f);
            }
            else _editWpp = _camera.OrthoScale * 2f / min;
        }

        private void EditMouseMove(float dx, float dy)
        {
            var ed = _edit;
            var o = _editObj;
            if (ed == null || o == null || o.IsLocked || !ed.Dragging) { EditMouseUp(); return; }

            // Shift: precise — the pointer's travel counts a quarter.
            float k = (Control.ModifierKeys & Keys.Shift) == Keys.Shift ? 0.25f : 1f;
            _editVx += dx * k;
            _editVy += dy * k;

            // Constrained to an axis by the handle that was grabbed, or by a held X / Y / Z key.
            GizmoAxis axis = _activeAxis != GizmoAxis.None ? _activeAxis : HeldAxisKey();
            Vec3 wd;
            if (axis == GizmoAxis.None)
                wd = _editRight * (_editVx * _editWpp) - _editUp * (_editVy * _editWpp);
            else
            {
                Vec3 a = axis == GizmoAxis.X ? new Vec3(1, 0, 0) : axis == GizmoAxis.Y ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1);
                PointF sc  = Proj(_editCtrW);
                PointF tip = Proj(_editCtrW + a * GizmoLen);
                float axDx = tip.X - sc.X, axDy = tip.Y - sc.Y;
                float axLen = (float)Math.Sqrt(axDx * axDx + axDy * axDy);
                float t;
                if (axLen < 6f || sc.X < -9000 || tip.X < -9000)
                {
                    // The axis points at the camera: drag vertically instead (down = toward you).
                    float sign = _camera.GetDepth(_editCtrW + a * GizmoLen) >= _camera.GetDepth(_editCtrW) ? 1f : -1f;
                    t = _editVy * sign * 1.4f * _editWpp;
                }
                else t = (_editVx * axDx + _editVy * axDy) / axLen * (GizmoLen / axLen);   // the point follows the pointer along the axis
                wd = a * t;
            }

            // Ctrl: snap the picked point to the grid (only the dragged axis when constrained).
            if (SnapHeld && SnapGrid > 1e-6f)
            {
                Vec3 p = _editRefW + wd;
                if (axis == GizmoAxis.None) p = new Vec3(MeshEditor.Snap(p.X, SnapGrid), MeshEditor.Snap(p.Y, SnapGrid), MeshEditor.Snap(p.Z, SnapGrid));
                else { int i = axis == GizmoAxis.X ? 0 : axis == GizmoAxis.Y ? 1 : 2; p = p.With(i, MeshEditor.Snap(p.Get(i), SnapGrid)); }
                wd = p - _editRefW;
            }

            Vec3 ld;
            if (!MeshEditor.TryLocalDelta(o, wd, out ld)) return;
            if (!_editMoved)
            {
                if (ld.Length < 1e-7f) return;
                _editMoved = true;
                TransformStarted?.Invoke(this, EventArgs.Empty);   // one undo step per drag, taken before the mesh changes
            }
            if (ed.MoveBy(ld))
            {
                _glr.InvalidateNormals(o.Mesh);
                Redraw();
                ObjectTransformed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void EditMouseUp()
        {
            if (!_editDrag) return;
            _editDrag = false;
            _activeAxis = GizmoAxis.None;
            if (_edit != null) _edit.EndDrag();
            Redraw();
        }

        private static GizmoAxis HeldAxisKey()
        {
            if ((GetKeyState(0x58) & 0x8000) != 0) return GizmoAxis.X;
            if ((GetKeyState(0x59) & 0x8000) != 0) return GizmoAxis.Y;
            if ((GetKeyState(0x5A) & 0x8000) != 0) return GizmoAxis.Z;
            return GizmoAxis.None;
        }

        // ── Drawing ───────────────────────────────────────────────

        private void DrawEditShapeGL()
        {
            var o = EditTarget();
            if (o == null) return;
            var ed = EnsureEditor(o);
            var xf = SceneGeometry.Xform.For(o);

            int n = ed.PointCount;
            var w = _editWorld;
            for (int p = 0; p < n; p++) w[p] = xf.Apply(ed.PointAt(p));

            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Lighting);
            // Depth-tested, pulled a little towards the viewer: the far side of the shape stays hidden
            // (and is not pickable either), the near side does not flicker against its own surface.
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.DepthMask(false);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.Enable(EnableCap.PolygonOffsetLine);
            GL.Enable(EnableCap.PolygonOffsetPoint);
            GL.PolygonOffset(-1f, -6f);
            GL.Enable(EnableCap.Blend);

            // The picked face (all of its flat patch).
            if (ed.Kind == MeshPick.Face)
            {
                GL.Color4(1f, 0.55f, 0.1f, 0.35f);
                for (int pi = 0; pi < ed.PatchCount; pi++)
                {
                    GL.Begin(PrimitiveType.TriangleFan);
                    foreach (int v in o.Mesh.Faces[ed.PatchFace(pi)]) { Vec3 q = w[ed.PointOf(v)]; GL.Vertex3(q.X, q.Y, q.Z); }
                    GL.End();
                }
            }

            // Edges.
            GL.LineWidth(1f);
            GL.Color4(1f, 1f, 1f, 0.35f);
            GL.Begin(PrimitiveType.Lines);
            for (int e = 0, ec = ed.EdgeCount; e < ec; e++)
            {
                if (!ed.EdgeShown(e)) continue;
                Vec3 a = w[ed.EdgeA(e)], b = w[ed.EdgeB(e)];
                GL.Vertex3(a.X, a.Y, a.Z);
                GL.Vertex3(b.X, b.Y, b.Z);
            }
            GL.End();

            if (ed.Kind == MeshPick.Edge && ed.PickedCount == 2)
            {
                Vec3 a = w[ed.PickedPoint(0)], b = w[ed.PickedPoint(1)];
                GL.LineWidth(3f);
                GL.Color4(1f, 0.55f, 0.1f, 1f);
                GL.Begin(PrimitiveType.Lines);
                GL.Vertex3(a.X, a.Y, a.Z);
                GL.Vertex3(b.X, b.Y, b.Z);
                GL.End();
            }

            // Points: white, the picked ones larger and orange.
            GL.PointSize(5f);
            GL.Color4(1f, 1f, 1f, 1f);
            GL.Begin(PrimitiveType.Points);
            for (int p = 0; p < n; p++)
                if (!ed.IsPicked(p)) GL.Vertex3(w[p].X, w[p].Y, w[p].Z);
            GL.End();
            GL.PointSize(9f);
            GL.Color4(1f, 0.55f, 0.1f, 1f);
            GL.Begin(PrimitiveType.Points);
            for (int i = 0; i < ed.PickedCount; i++) { Vec3 q = w[ed.PickedPoint(i)]; GL.Vertex3(q.X, q.Y, q.Z); }
            GL.End();

            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.Disable(EnableCap.PolygonOffsetLine);
            GL.Disable(EnableCap.PolygonOffsetPoint);
            GL.DepthMask(true);

            // The axis handles at the picked centre (a locked object has none), always on top.
            if (ed.HasSelection && !o.IsLocked)
            {
                GL.Disable(EnableCap.DepthTest);
                GL.LineWidth(2.5f);
                Vec3 ctr = xf.Apply(ed.PickedCenter());
                if (AxisVisible(GizmoAxis.X)) ArmGL(ctr, new Vec3(GizmoLen, 0, 0), 1f, 0f, 0f, GizmoAxis.X);
                if (AxisVisible(GizmoAxis.Y)) ArmGL(ctr, new Vec3(0, GizmoLen, 0), 0f, 1f, 0f, GizmoAxis.Y);
                if (AxisVisible(GizmoAxis.Z)) ArmGL(ctr, new Vec3(0, 0, GizmoLen), 0.2f, 0.5f, 1f, GizmoAxis.Z);
            }

            GL.LineWidth(1f);
            GL.PointSize(1f);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
