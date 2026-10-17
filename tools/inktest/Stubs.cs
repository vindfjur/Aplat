// The journal code is compiled on its own here (no WinForms / OpenTK), but LiveItem
// refers to the tool enum that lives next to SceneView. Same members, same order.
namespace Aplat.Controls
{
    public enum BrushKind { Brush, Pencil, Pen, Marker, Eyedropper, Fill, Eraser, Text2D, Select2D, ShapeLine, ShapeRect, ShapeEllipse, ShapeTriangle, ObjectEraser, Lasso }
}
