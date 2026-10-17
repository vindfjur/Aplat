using System;
using System.Collections.Generic;
using System.Drawing;
using Aplat.Models;

namespace Aplat.Rendering
{
    // App-wide geometry / transform / UV library plus the live render settings.
    // (The GDI software draw path is gone: the live viewport is rendered fully on
    // the GPU by GLRenderer; only the geometry/UV math and the shared settings live
    // here. Used by GLRenderer, SceneView, ExportManager, PropertiesPanel and the
    // canvas dialog.)
    public class SceneGeometry
    {
        public bool  ShowWireframe { get; set; }
        public bool  ShowVertices  { get; set; }

        // Whether to outline every face of the selected object. On a dense mesh (sphere,
        // torus) that turns into a yellow mess, so the outline can be switched off —
        // the selection is then shown as a bounding rectangle.
        public bool  OutlineSelection { get; set; } = true;
        public float Brightness    { get; set; }

        // ── Canvas / grid settings (changeable from the "View" menu) ──
        public static readonly Color DefaultBackground = Color.FromArgb(50, 50, 53);
        public static readonly Color DefaultGridColor  = Color.FromArgb(55, 100, 100, 100);

        public bool  TileTextures    { get; set; } = true;   // false → fit image once, no tiling
        public bool  ShowGrid        { get; set; } = true;
        public bool  FixedGrid       { get; set; } = false;
        public float GridExtent      { get; set; } = 5f;     // canvas size — half-size of the floor
        public float GridSpacing     { get; set; } = 0.5f;   // grid format — distance between lines
        public Color BackgroundColor { get; set; } = DefaultBackground;
        public Color GridColor       { get; set; } = DefaultGridColor;

        // Camera is accepted for call-site compatibility; the geometry helpers are
        // static and the live drawing now lives in GLRenderer, so it isn't stored.
        public SceneGeometry(Camera cam)
        {
            Brightness = 1f;
        }

        // ── UV mapping ────────────────────────────────────────────

        // Planar ("box") UV for a vertex, chosen by the face's dominant axis.
        // Uses local coords so the mapping is fixed on the surface. +0.5 centres
        // unit-sized primitives into the 0..1 range; tiling handles the rest.
        public static PointF PlanarUV(Vec3 local, Vec3 nLocal)
        {
            // V is flipped (0.5 - coord) because world Y points up while texture rows
            // increase downward — otherwise the image shows upside-down.
            float ax = Math.Abs(nLocal.X), ay = Math.Abs(nLocal.Y), az = Math.Abs(nLocal.Z);
            if (ax >= ay && ax >= az) return new PointF(local.Z + 0.5f, 0.5f - local.Y);
            if (ay >= ax && ay >= az) return new PointF(local.X + 0.5f, 0.5f - local.Z);
            return new PointF(local.X + 0.5f, 0.5f - local.Y);
        }

        // Atlas UV for the paint layer: every face gets its own cell in a square
        // grid, so painting one face never bleeds onto another (unlike planar UVs,
        // where opposite faces of a cube share the same 0..1 region). Inside a cell
        // the face's vertices are placed on a regular polygon — deterministic, so
        // the renderer and the brush agree on where a hit lands in the texture.
        public static PointF AtlasUV(int faceIndex, int faceCount, int vertIndex, int vertCount)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, faceCount)));
            if (cols < 1) cols = 1;
            float cell = 1f / cols;
            int cx = faceIndex % cols;
            int cy = faceIndex / cols;

            const float pad = 0.12f;                  // inset to stop cross-cell bleeding
            float r = (0.5f - pad) ;
            float a = (vertCount <= 0 ? 0f : (2f * (float)Math.PI * vertIndex / vertCount)) - (float)Math.PI / 2f;
            float ux = 0.5f + r * (float)Math.Cos(a);
            float uy = 0.5f + r * (float)Math.Sin(a);

            return new PointF((cx + ux) * cell, (cy + uy) * cell);
        }

        // Paint-layer UV for a face vertex. A flat "Canvas" sheet maps 1:1 onto the
        // whole texture (corners → corners) so drawing on it feels like a 2-D paper;
        // every other object uses the per-face atlas so paint never bleeds across faces.
        public static PointF PaintUV(SceneObject obj, int faceIndex, int faceCount,
                                     int vertIndex, int vertCount)
        {
            if (obj != null && obj.PrimitiveType == "Canvas" && vertCount == 4)
            {
                switch (vertIndex)
                {
                    case 0:  return new PointF(0f, 1f);   // bottom-left
                    case 1:  return new PointF(1f, 1f);   // bottom-right
                    case 2:  return new PointF(1f, 0f);   // top-right
                    default: return new PointF(0f, 0f);   // top-left
                }
            }
            return AtlasUV(faceIndex, faceCount, vertIndex, vertCount);
        }

        // ── Transforms & bounds ───────────────────────────────────

        // Precomputed local→world transform of one object. The Euler trig is
        // evaluated once per object instead of per vertex — the old TransformVertex
        // did 6 sin/cos for every vertex, which dominated per-frame CPU cost on
        // dense meshes (spheres, 3-D text). Hot loops grab an Xform and Apply it.
        public struct Xform
        {
            private Vec3 _cx, _cy, _cz;     // columns of Rz·Ry·Rx (world images of local axes)
            private Vec3 _pos, _scale;

            public static Xform For(SceneObject obj)
            {
                var x = new Xform();
                EulerToBasis(obj.Rotation, out x._cx, out x._cy, out x._cz);
                x._pos   = obj.Position;
                x._scale = obj.Scale;
                return x;
            }

            // Local normal → world. The scale enters as its inverse (a normal is transformed
            // by the inverse transpose of the model matrix), otherwise light falls
            // at an angle on non-uniformly stretched objects.
            public Vec3 ApplyNormal(Vec3 n)
            {
                float nx = n.X / (Math.Abs(_scale.X) < 1e-5f ? 1e-5f : _scale.X);
                float ny = n.Y / (Math.Abs(_scale.Y) < 1e-5f ? 1e-5f : _scale.Y);
                float nz = n.Z / (Math.Abs(_scale.Z) < 1e-5f ? 1e-5f : _scale.Z);
                return new Vec3(
                    _cx.X * nx + _cy.X * ny + _cz.X * nz,
                    _cx.Y * nx + _cy.Y * ny + _cz.Y * nz,
                    _cx.Z * nx + _cy.Z * ny + _cz.Z * nz).Normalized();
            }

            public Vec3 Apply(Vec3 local)
            {
                float sx = local.X * _scale.X;
                float sy = local.Y * _scale.Y;
                float sz = local.Z * _scale.Z;
                return new Vec3(
                    _cx.X * sx + _cy.X * sy + _cz.X * sz + _pos.X,
                    _cx.Y * sx + _cy.Y * sy + _cz.Y * sz + _pos.Y,
                    _cx.Z * sx + _cy.Z * sy + _cz.Z * sz + _pos.Z);
            }
        }

        public static Vec3 TransformVertex(Vec3 local, SceneObject obj)
        {
            return Xform.For(obj).Apply(local);
        }

        public static IEnumerable<SceneObject> Leaves(SceneObject obj)
        {
            if (obj.IsGroup)
            {
                foreach (var ch in obj.Children)
                    foreach (var leaf in Leaves(ch))
                        yield return leaf;
            }
            else
            {
                yield return obj;
            }
        }

        public static Vec3 Center(SceneObject obj)
        {
            if (obj.IsGroup)
            {
                Vec3 sum = Vec3.Zero;
                int n = 0;
                foreach (var leaf in Leaves(obj)) { sum = sum + LeafCenter(leaf); n++; }
                return n > 0 ? sum * (1f / n) : obj.Position;
            }
            return LeafCenter(obj);
        }

        // World-space axis-aligned bounding box of an object (incl. group children).
        // Returns false when there is no geometry. Used to avoid spawning overlaps.
        public static bool WorldBounds(SceneObject obj, out Vec3 min, out Vec3 max)
        {
            min = new Vec3( float.MaxValue,  float.MaxValue,  float.MaxValue);
            max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            bool any = false;
            foreach (var leaf in Leaves(obj))
            {
                if (leaf.Mesh == null) continue;
                var xf = Xform.For(leaf);
                foreach (var v in leaf.Mesh.Vertices)
                {
                    Vec3 w = xf.Apply(v);
                    if (w.X < min.X) min.X = w.X; if (w.X > max.X) max.X = w.X;
                    if (w.Y < min.Y) min.Y = w.Y; if (w.Y > max.Y) max.Y = w.Y;
                    if (w.Z < min.Z) min.Z = w.Z; if (w.Z > max.Z) max.Z = w.Z;
                    any = true;
                }
            }
            return any;
        }

        private static Vec3 LeafCenter(SceneObject obj)
        {
            if (obj.Mesh != null && obj.Mesh.Vertices.Count > 0)
            {
                var xf = Xform.For(obj);
                Vec3 sum = Vec3.Zero;
                foreach (var v in obj.Mesh.Vertices)
                    sum = sum + xf.Apply(v);
                return sum * (1f / obj.Mesh.Vertices.Count);
            }
            return obj.Position;
        }

        public static void Translate(SceneObject obj, Vec3 delta)
        {
            if (obj.IsGroup)
            {
                foreach (var leaf in Leaves(obj))
                    leaf.Position = leaf.Position + delta;
            }
            obj.Position = obj.Position + delta;

        }

        public static void RotateAround(SceneObject obj, Vec3 pivot, int axis, float deg)
        {
            if (obj.IsGroup)
            {
                foreach (var leaf in Leaves(obj))
                    RotateLeaf(leaf, pivot, axis, deg);
                obj.Rotation = obj.Rotation.With(axis, obj.Rotation.Get(axis) + deg);
            }
            else
            {
                RotateLeaf(obj, pivot, axis, deg);
            }
        }

        public static void ScaleAround(SceneObject obj, Vec3 pivot, int axis, float factor)
        {
            if (obj.IsGroup)
            {
                foreach (var leaf in Leaves(obj))
                    ScaleLeaf(leaf, pivot, axis, factor);
                obj.Scale = obj.Scale.With(axis, obj.Scale.Get(axis) * factor);
            }
            else
            {
                ScaleLeaf(obj, pivot, axis, factor);
            }
        }

        private static void RotateLeaf(SceneObject obj, Vec3 pivot, int axis, float deg)
        {
            obj.Position = RotatePoint(obj.Position, pivot, axis, deg);
            obj.Rotation = obj.Rotation.With(axis, obj.Rotation.Get(axis) + deg);
        }

        private static void ScaleLeaf(SceneObject obj, Vec3 pivot, int axis, float factor)
        {
            Vec3 rel = obj.Position - pivot;
            rel = rel.With(axis, rel.Get(axis) * factor);
            obj.Position = pivot + rel;
            obj.Scale = obj.Scale.With(axis, Math.Max(0.02f, obj.Scale.Get(axis) * factor));
        }

        private static Vec3 RotatePoint(Vec3 p, Vec3 pivot, int axis, float deg)
        {
            float a = deg * (float)Math.PI / 180f;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            Vec3 r = p - pivot;
            float x = r.X, y = r.Y, z = r.Z;
            if (axis == 0)
            {
                float ny = y * c - z * s;
                float nz = y * s + z * c;
                y = ny; z = nz;
            }
            else if (axis == 1)
            {
                float nx = x * c + z * s;
                float nz = -x * s + z * c;
                x = nx; z = nz;
            }
            else
            {
                float nx = x * c - y * s;
                float ny = x * s + y * c;
                x = nx; y = ny;
            }
            return pivot + new Vec3(x, y, z);
        }

        // ── Local-axis rotation (gizmo rings follow the object) ────
        // The render builds world = Rz(rz)·Ry(ry)·Rx(rx)·local, so an object's local
        // X/Y/Z axes are the columns of that matrix. We expose them as world-space
        // directions, rotate the whole orientation about an arbitrary world axis, and
        // re-extract Euler angles — all without changing the Euler-based TransformVertex.

        // Local axis (0=X,1=Y,2=Z) of an object as a unit world-space direction.
        public static Vec3 LocalAxisWorld(SceneObject obj, int axis)
        {
            EulerToBasis(obj != null ? obj.Rotation : Vec3.Zero, out Vec3 cx, out Vec3 cy, out Vec3 cz);
            Vec3 v = axis == 0 ? cx : axis == 1 ? cy : cz;
            return v.Normalized();
        }

        // Rotate an object (and group leaves) about an arbitrary world-space axis
        // through a pivot. Positions move with Rodrigues; orientation is composed and
        // re-extracted so the Euler fields stay consistent with the visible rotation.
        public static void RotateAroundAxis(SceneObject obj, Vec3 pivot, Vec3 axisDir, float deg)
        {
            if (axisDir.Length < 1e-6f) return;
            if (obj.IsGroup)
            {
                foreach (var leaf in Leaves(obj)) RotateLeafAxis(leaf, pivot, axisDir, deg);
                obj.Rotation = ComposeEuler(obj.Rotation, axisDir, deg);
            }
            else
            {
                RotateLeafAxis(obj, pivot, axisDir, deg);
            }
        }

        private static void RotateLeafAxis(SceneObject o, Vec3 pivot, Vec3 axisDir, float deg)
        {
            o.Position = pivot + RotateVecAxis(o.Position - pivot, axisDir, deg);
            o.Rotation = ComposeEuler(o.Rotation, axisDir, deg);
        }

        // New Euler after pre-rotating an orientation by a world-axis rotation.
        private static Vec3 ComposeEuler(Vec3 euler, Vec3 axisDir, float deg)
        {
            EulerToBasis(euler, out Vec3 cx, out Vec3 cy, out Vec3 cz);
            cx = RotateVecAxis(cx, axisDir, deg);
            cy = RotateVecAxis(cy, axisDir, deg);
            cz = RotateVecAxis(cz, axisDir, deg);
            return EulerFromBasis(cx, cy, cz);
        }

        // Rodrigues rotation of a vector about a unit axis (axis is normalized here).
        private static Vec3 RotateVecAxis(Vec3 v, Vec3 axis, float deg)
        {
            Vec3 k = axis.Normalized();
            float a = deg * (float)Math.PI / 180f;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            Vec3 cross = Vec3.Cross(k, v);
            float dot = Vec3.Dot(k, v);
            return v * c + cross * s + k * (dot * (1f - c));
        }

        // Euler degrees → the three columns of Rz·Ry·Rx (object local axes in world).
        private static void EulerToBasis(Vec3 deg, out Vec3 cx, out Vec3 cy, out Vec3 cz)
        {
            float a = deg.X * (float)Math.PI / 180f;
            float b = deg.Y * (float)Math.PI / 180f;
            float g = deg.Z * (float)Math.PI / 180f;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            float cb = (float)Math.Cos(b), sb = (float)Math.Sin(b);
            float cg = (float)Math.Cos(g), sg = (float)Math.Sin(g);

            cx = new Vec3(cg * cb, sg * cb, -sb);
            cy = new Vec3(cg * sb * sa - sg * ca, sg * sb * sa + cg * ca, cb * sa);
            cz = new Vec3(cg * sb * ca + sg * sa, sg * sb * ca - cg * sa, cb * ca);
        }

        // Inverse of EulerToBasis: recover (rx,ry,rz) degrees from the basis columns.
        private static Vec3 EulerFromBasis(Vec3 cx, Vec3 cy, Vec3 cz)
        {
            float r00 = cx.X, r10 = cx.Y, r20 = cx.Z;
            float r21 = cy.Z, r22 = cz.Z;
            float r11 = cy.Y, r12 = cz.Y;

            float sy = (float)Math.Sqrt(r00 * r00 + r10 * r10);   // = |cos(ry)|
            float rx, ry, rz;
            if (sy > 1e-6f)
            {
                rx = (float)Math.Atan2(r21, r22);
                ry = (float)Math.Atan2(-r20, sy);
                rz = (float)Math.Atan2(r10, r00);
            }
            else
            {
                rx = (float)Math.Atan2(-r12, r11);
                ry = (float)Math.Atan2(-r20, sy);
                rz = 0f;
            }
            const float k = 180f / (float)Math.PI;
            return new Vec3(rx * k, ry * k, rz * k);
        }
    }
}
