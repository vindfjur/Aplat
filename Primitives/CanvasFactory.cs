using Aplat.Models;

namespace Aplat.Primitives
{
    // A flat sheet ("Canvas") that stands in the XY plane facing the camera. It is
    // the starting surface of the canvas-first flow: you draw on it in 2-D and
    // can later add 3-D objects on top. Its single quad face maps 1:1 onto the
    // paint texture (see SceneGeometry.PaintUV), so painting feels like paper.
    public static class CanvasFactory
    {
        public static Mesh CreateSheet(float width, float height)
        {
            float w = width  / 2f;
            float h = height / 2f;
            var mesh = new Mesh();
            mesh.Vertices.Add(new Vec3(-w, -h, 0f));   // 0 bottom-left
            mesh.Vertices.Add(new Vec3( w, -h, 0f));   // 1 bottom-right
            mesh.Vertices.Add(new Vec3( w,  h, 0f));   // 2 top-right
            mesh.Vertices.Add(new Vec3(-w,  h, 0f));   // 3 top-left
            mesh.Faces.Add(new[] { 0, 1, 2, 3 });      // +Z normal
            return mesh;
        }
    }
}
