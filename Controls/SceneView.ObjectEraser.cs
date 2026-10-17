using System;
using System.Drawing;
using Aplat.Models;

namespace Aplat.Controls
{
    // The object eraser (the iPhone way): touch a stroke, a shape, a line of text or
    // a fill and the whole thing goes — not the pixels under the brush. What it was
    // covering comes back, because the canvas keeps its objects (InkJournal) and
    // rebuilds the sheet under the removed one from the rest.
    //
    // One drag is one undo step, taken only when something is really removed.
    public partial class SceneView
    {
        private PointF?     _objErasePrev;     // previous sample of the drag, in layer px
        private SceneObject _objEraseCanvas;
        private bool        _objEraseUndo;     // the undo step of this drag is taken
        private bool        _objEraseWarned;

        // Press: the first touch.
        private void BeginObjectErase(Point pt)
        {
            _painting = true;
            EndCanvasStroke();
            _objErasePrev = null;
            _objEraseCanvas = null;
            _objEraseUndo = false;
            _objEraseWarned = false;
            ObjectEraseTo(pt);
        }

        // Drag: everything the tip crossed since the last sample. The path between two
        // samples is walked in small steps, so a quick swipe cannot jump over a hairline.
        private void ObjectEraseTo(Point pt)
        {
            if (!PickPaintTarget(pt, out SceneObject obj, out PointF uv)) { _objErasePrev = null; return; }
            if (obj.PrimitiveType != "Canvas")
            {
                if (!_objEraseWarned) { _objEraseWarned = true; Notify?.Invoke(this, Loc.T("Ластик объектов работает на холсте")); }
                return;
            }
            var layer = PaintLayerFor(obj);
            var ink = obj.Material.Ink;
            if (ink == null) return;

            BrushParams(out int radius, out bool _);
            float tol = Math.Max(2f, radius * Material.PaintScale(layer));
            var p = new PointF(uv.X * layer.Width, uv.Y * layer.Height);
            PointF from = _objErasePrev.HasValue && ReferenceEquals(_objEraseCanvas, obj) ? _objErasePrev.Value : p;

            float dx = p.X - from.X, dy = p.Y - from.Y;
            int steps = Math.Max(1, (int)Math.Ceiling((float)Math.Sqrt(dx * dx + dy * dy) / Math.Max(2f, tol * 0.5f)));
            bool any = false;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                var s = new PointF(from.X + dx * t, from.Y + dy * t);
                InkOp hit = ink.HitTest(layer, s, tol);
                if (hit == null) continue;
                if (!_objEraseUndo) { _objEraseUndo = true; TransformStarted?.Invoke(this, EventArgs.Empty); }
                obj.Material.MarkPaintDirty(ink.Remove(hit, layer));
                any = true;
            }
            _objErasePrev = p;
            _objEraseCanvas = obj;
            if (!any) return;

            _lastPaintMaterial = obj.Material;
            if (_scene != null) _scene.IsModified = true;
            Redraw();
            ObjectTransformed?.Invoke(this, EventArgs.Empty);
        }
    }
}
