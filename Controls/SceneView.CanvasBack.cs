using System;
using System.Drawing;
using System.Windows.Forms;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    // The back of the drawing sheet is drawn as the reverse of a stretched canvas
    // (CanvasBack), and nothing can be painted there. These make that plain: over it
    // the cursor says "no", and a press says why instead of silently doing nothing.
    public partial class SceneView
    {
        private bool _overCanvasBack;

        // Is the point over the reverse side of a canvas sheet (and not over anything paintable)?
        private bool CanvasBackAt(Point pt)
        {
            if (_scene == null) return false;
            bool persp = _camera.Mode == ViewMode.Perspective;
            Vec3 camPos = persp ? _camera.GetPosition() : Vec3.Zero;
            Vec3 ortho = persp ? Vec3.Zero : _camera.GetOrthoViewDir();
            foreach (var top in _scene.Objects)
            {
                if (!top.IsVisible) continue;
                foreach (var leaf in SceneGeometry.Leaves(top))
                {
                    if (leaf.PrimitiveType != "Canvas" || !leaf.IsVisible || leaf.Mesh == null || leaf.Mesh.Faces.Count == 0) continue;
                    var face = leaf.Mesh.Faces[0];
                    if (face.Length != 4) continue;
                    var xf = SceneGeometry.Xform.For(leaf);
                    var wv = new Vec3[4];
                    var sp = new PointF[4];
                    bool ok = true;
                    for (int i = 0; i < 4; i++)
                    {
                        wv[i] = xf.Apply(leaf.Mesh.Vertices[face[i]]);
                        sp[i] = _camera.Project(wv[i], W, H);
                        if (float.IsNaN(sp[i].X) || sp[i].X < -9000) { ok = false; break; }
                    }
                    if (!ok) continue;
                    Vec3 n = Vec3.Cross(wv[1] - wv[0], wv[2] - wv[0]).Normalized();
                    Vec3 toCam = persp ? (camPos - wv[0]).Normalized() : ortho;
                    if (Vec3.Dot(n, toCam) >= 0f) continue;   // the paper side faces us
                    float a, b, c;
                    if (Bary(sp[0], sp[1], sp[2], pt, out a, out b, out c) ||
                        Bary(sp[0], sp[2], sp[3], pt, out a, out b, out c))
                        return true;
                }
            }
            return false;
        }

        // Hover: the "no" cursor over the back with a paint tool; restored when it leaves.
        private void UpdateBackCursor(Point pt)
        {
            if (_gl == null) return;
            bool over = _tool == ToolMode.Paint && _live == null && !PickPaintTarget(pt, out SceneObject pickedObj, out PointF pickedUv) && CanvasBackAt(pt);
            if (over == _overCanvasBack) return;
            _overCanvasBack = over;
            _gl.Cursor = over ? Cursors.No : ToolCursor(_tool);
        }

        // A press with a paint tool on the back: say why nothing happens. True = consumed.
        private bool TellCanvasBack(Point pt)
        {
            if (PickPaintTarget(pt, out SceneObject pickedObj, out PointF pickedUv) || !CanvasBackAt(pt)) return false;
            Notify?.Invoke(this, Loc.T("Это обратная сторона холста — рисуйте с лицевой"));
            return true;
        }
    }
}
