using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.IO
{
    // Result of an OBJ export: the .obj text, the companion .mtl text, and the
    // texture images (filename → bitmap) that the caller writes next to them.
    public class ObjExport
    {
        public string Obj;
        public string Mtl;
        public readonly Dictionary<string, Bitmap> Textures = new Dictionary<string, Bitmap>();
    }

    public static class ExportManager
    {
        // OBJ/STL/FBX require '.' as the decimal separator. On a Russian Windows
        // locale string.Format would emit commas ("2,95"), which breaks every
        // importer (Assimp stops at the comma). Always format invariantly.
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Build the OBJ + MTL + textures. Drawings painted on shapes (and the canvas)
        // are baked into a per-object PNG referenced from the MTL (map_Kd) with proper
        // texture coordinates, so the image survives in any OBJ viewer.
        public static ObjExport BuildOBJ(IEnumerable<SceneObject> objects, string mtlFileName)
        {
            var res = new ObjExport();
            var obj = new StringBuilder();
            var mtl = new StringBuilder();

            obj.AppendLine("# Aplat OBJ Export");
            if (!string.IsNullOrEmpty(mtlFileName)) obj.AppendLine("mtllib " + mtlFileName);
            mtl.AppendLine("# Aplat MTL Export");

            int vo = 1, vtBase = 0, vnBase = 0, mi = 0;
            foreach (var o in FlatObjects(objects))
            {
                if (o.Mesh == null || o.Mesh.Vertices == null || o.Mesh.Faces == null ||
                    o.Mesh.Vertices.Count == 0 || o.Mesh.Faces.Count == 0) continue;

                Color col = o.Material != null ? o.Material.Color : Color.Gray;
                float r = col.R / 255f, g = col.G / 255f, b = col.B / 255f;
                string matName = MatName(o, mi);

                bool hasPaint = o.Material != null && o.Material.HasPaint;
                bool hasFile  = o.Material != null && o.Material.HasTexture;
                string texFile = null;

                // ── material ──
                mtl.AppendLine("newmtl " + matName);
                mtl.AppendLine(string.Format(Inv, "Kd {0:F6} {1:F6} {2:F6}", r, g, b));
                mtl.AppendLine(string.Format(Inv, "Ka {0:F6} {1:F6} {2:F6}", r * 0.2f, g * 0.2f, b * 0.2f));
                WriteMtlSurface(mtl, o.Material, col);

                // Bake the surface (colour / photo texture + painted drawing) into a
                // single image laid out the same way the renderer samples it, so it maps
                // 1:1 with the exported texture coords. Canvas → 1:1 sheet; everything
                // else → per-face atlas (matches SceneGeometry.PaintUV / AtlasUV).
                Bitmap baked = null;
                if (hasPaint || hasFile)
                    baked = o.PrimitiveType == "Canvas" ? BakeCanvas(o) : BakeAtlas(o);
                if (baked != null)
                {
                    texFile = matName + ".png";
                    res.Textures[texFile] = baked;
                    mtl.AppendLine("map_Kd " + texFile);
                }
                mtl.AppendLine();

                // ── geometry ──
                obj.AppendLine("o " + ObjectName(o, mi));
                obj.AppendLine("usemtl " + matName);

                var xfO = SceneGeometry.Xform.For(o);
                foreach (var v in o.Mesh.Vertices)
                {
                    var w = xfO.Apply(v);
                    obj.AppendLine(string.Format(Inv, "v {0:F6} {1:F6} {2:F6}", w.X, w.Y, w.Z));
                }

                // Texture coords and normals are written per exported triangle corner. Many
                // importers are picky about quads/ngons and missing vn records, so the
                // exporter fan-triangulates every face and emits explicit v/vt/vn (or
                // v//vn for untextured geometry). Normals are the renderer's smooth ones
                // (shared across a smooth surface, split at hard edges) — equal normals
                // are written once and reused.
                int faceCount = o.Mesh.Faces.Count;
                var smooth = SmoothNormals(o.Mesh);
                var vnIndex = new Dictionary<long, int>();
                int localVt = 0, localVn = 0;
                var attrs = new StringBuilder();
                var faces = new StringBuilder();
                for (int fi = 0; fi < faceCount; fi++)
                {
                    var face = o.Mesh.Faces[fi];
                    if (!ValidFace(o.Mesh, face)) continue;

                    var wv = new Vec3[face.Length];
                    for (int k = 0; k < face.Length; k++)
                        wv[k] = xfO.Apply(o.Mesh.Vertices[face[k]]);

                    for (int i = 1; i + 1 < face.Length; i++)
                    {
                        Vec3 n = Vec3.Cross(wv[i] - wv[0], wv[i + 1] - wv[0]).Normalized();
                        if (n.Length < 1e-6f) continue;

                        int n0 = ObjNormal(attrs, vnIndex, xfO, smooth[fi][0],     vnBase, ref localVn);
                        int n1 = ObjNormal(attrs, vnIndex, xfO, smooth[fi][i],     vnBase, ref localVn);
                        int n2 = ObjNormal(attrs, vnIndex, xfO, smooth[fi][i + 1], vnBase, ref localVn);

                        int triVtStart = vtBase + localVt + 1;
                        if (texFile != null)
                        {
                            WriteObjVt(attrs, SceneGeometry.PaintUV(o, fi, faceCount, 0,     face.Length));
                            WriteObjVt(attrs, SceneGeometry.PaintUV(o, fi, faceCount, i,     face.Length));
                            WriteObjVt(attrs, SceneGeometry.PaintUV(o, fi, faceCount, i + 1, face.Length));
                            localVt += 3;
                        }

                        faces.Append("f");
                        AppendObjCorner(faces, face[0]     + vo, texFile != null ? triVtStart     : 0, n0);
                        AppendObjCorner(faces, face[i]     + vo, texFile != null ? triVtStart + 1 : 0, n1);
                        AppendObjCorner(faces, face[i + 1] + vo, texFile != null ? triVtStart + 2 : 0, n2);
                        faces.AppendLine();
                    }
                }

                obj.Append(attrs.ToString());
                obj.Append(faces.ToString());

                vo     += o.Mesh.Vertices.Count;
                vtBase += localVt;
                vnBase += localVn;
                mi++;
            }

            res.Obj = obj.ToString();
            res.Mtl = mtl.ToString();
            return res;
        }

        // Material in MTL terms. Opacity → d; Metallic/Roughness → Ks/Ns (a metal's
        // highlight takes the surface colour, a dielectric reflects ~4%; Ns follows the
        // usual Ns = (1 − roughness)² · 1000); emission → Ke.
        private static void WriteMtlSurface(StringBuilder mtl, Material m, Color col)
        {
            float metal = 0f, rough = 0.55f, opacity = 1f, emit = 0f;
            Color ec = Color.Black;
            if (m != null)
            {
                metal = Clamp01(m.Metallic); rough = Clamp01(m.Roughness); opacity = Clamp01(m.Opacity);
                emit = Clamp01(m.EmissionStrength); ec = m.EmissionColor;
            }
            float ks(float c) { return 0.04f * (1f - metal) + c * metal; }
            float ns = Math.Max(1f, (1f - rough) * (1f - rough) * 1000f);
            mtl.AppendLine(string.Format(Inv, "Ks {0:F6} {1:F6} {2:F6}", ks(col.R / 255f), ks(col.G / 255f), ks(col.B / 255f)));
            mtl.AppendLine(string.Format(Inv, "Ns {0:F6}", ns));
            mtl.AppendLine(string.Format(Inv, "Ke {0:F6} {1:F6} {2:F6}", ec.R / 255f * emit, ec.G / 255f * emit, ec.B / 255f * emit));
            mtl.AppendLine(string.Format(Inv, "d {0:F6}", opacity));
            mtl.AppendLine("illum 2");
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        // A face the exporters can use: at least a triangle, every index inside the mesh.
        private static bool ValidFace(Mesh mesh, int[] face)
        {
            if (face == null || face.Length < 3) return false;
            int vc = mesh.Vertices.Count;
            foreach (int k in face) if (k < 0 || k >= vc) return false;
            return true;
        }

        // Writes the (world-space) normal once per distinct value; returns its 1-based OBJ index.
        private static int ObjNormal(StringBuilder attrs, Dictionary<long, int> seen, SceneGeometry.Xform xf,
                                     Vec3 local, int vnBase, ref int localVn)
        {
            Vec3 n = xf.ApplyNormal(local);
            long key = ((long)Math.Round(n.X * 2000f) & 0x1FFFFF) | (((long)Math.Round(n.Y * 2000f) & 0x1FFFFF) << 21)
                     | (((long)Math.Round(n.Z * 2000f) & 0x1FFFFF) << 42);
            int idx;
            if (!seen.TryGetValue(key, out idx))
            {
                idx = vnBase + ++localVn;
                seen[key] = idx;
                attrs.AppendLine(string.Format(Inv, "vn {0:F6} {1:F6} {2:F6}", n.X, n.Y, n.Z));
            }
            return idx;
        }

        // Per face, the normal at each of its corners, shaded the way the renderer does it
        // (GLRenderer.SmoothNormals): a corner averages the faces that share its position
        // when they lean less than 50° from this face, so a sphere comes out smooth while
        // cube, pyramid and tetrahedron edges stay hard. Local space; invalid faces get a
        // dummy entry so indices line up with Mesh.Faces.
        public static Vec3[][] SmoothNormals(Mesh mesh)
        {
            int fc = mesh.Faces.Count, vc = mesh.Vertices.Count;
            var faceN = new Vec3[fc];
            for (int f = 0; f < fc; f++)
            {
                var face = mesh.Faces[f];
                if (!ValidFace(mesh, face)) { faceN[f] = Vec3.Zero; continue; }
                // Newell's method: right for non-planar and concave faces too.
                float nx = 0, ny = 0, nz = 0;
                for (int i = 0; i < face.Length; i++)
                {
                    Vec3 c = mesh.Vertices[face[i]], d = mesh.Vertices[face[(i + 1) % face.Length]];
                    nx += (c.Y - d.Y) * (c.Z + d.Z);
                    ny += (c.Z - d.Z) * (c.X + d.X);
                    nz += (c.X - d.X) * (c.Y + d.Y);
                }
                var nn = new Vec3(nx, ny, nz);
                faceN[f] = nn.Length < 1e-12f ? Vec3.Zero : nn.Normalized();
            }

            // Vertices at the same position are one (a sphere's seam and poles are duplicated).
            var weld = new int[vc];
            var seen = new Dictionary<long, int>(vc);
            for (int i = 0; i < vc; i++)
            {
                Vec3 v = mesh.Vertices[i];
                long key = ((long)Math.Round(v.X * 10000f) & 0x1FFFFF) | (((long)Math.Round(v.Y * 10000f) & 0x1FFFFF) << 21)
                         | (((long)Math.Round(v.Z * 10000f) & 0x1FFFFF) << 42);
                int id;
                if (!seen.TryGetValue(key, out id)) { id = seen.Count; seen[key] = id; }
                weld[i] = id;
            }
            var adj = new List<int>[seen.Count];
            for (int f = 0; f < fc; f++)
            {
                if (!ValidFace(mesh, mesh.Faces[f])) continue;
                foreach (int vi in mesh.Faces[f])
                {
                    int w = weld[vi];
                    if (adj[w] == null) adj[w] = new List<int>();
                    adj[w].Add(f);
                }
            }

            const float crease = 0.64f;    // cos(50°)
            var result = new Vec3[fc][];
            for (int f = 0; f < fc; f++)
            {
                var face = mesh.Faces[f];
                if (!ValidFace(mesh, face)) { result[f] = new Vec3[face != null ? face.Length : 0]; continue; }
                var ns = new Vec3[face.Length];
                for (int k = 0; k < face.Length; k++)
                {
                    Vec3 sum = Vec3.Zero;
                    foreach (int g in adj[weld[face[k]]])
                    {
                        if (faceN[g].Length < 1e-6f) continue;
                        if (Vec3.Dot(faceN[g], faceN[f]) >= crease) sum = sum + faceN[g];
                    }
                    ns[k] = sum.Length > 1e-5f ? sum.Normalized() : faceN[f];
                }
                result[f] = ns;
            }
            return result;
        }

        // Public wrapper used by MainForm for canvas-image export.
        public static Bitmap BakeCanvasImage(SceneObject canvasObj) { return BakeCanvas(canvasObj); }

        // Canvas: the sheet maps 1:1 onto the texture, so just colour + the 1:1 drawing.
        private static Bitmap BakeCanvas(SceneObject o)
        {
            // Export at the sheet's own resolution (2048 today; older projects 1024).
            var paint = o.Material.PaintLayer;
            int s = paint != null ? paint.Width : Material.CanvasPaintSize;
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var br = new SolidBrush(o.Material.Color))
                    g.FillRectangle(br, 0, 0, s, s);
                if (paint != null) g.DrawImage(paint, new Rectangle(0, 0, s, s));
            }
            return bmp;
        }

        // Non-canvas: bake into the per-face atlas the renderer uses. Each face's cell
        // gets the base colour, then the file/photo texture sampled via its planar UVs
        // (the same mapping the live view shows), then the painted drawing (which is
        // already stored in the atlas). Texture coords are emitted as the atlas UVs, so
        // the result maps exactly in any OBJ viewer.
        private static Bitmap BakeAtlas(SceneObject o)
        {
            int s = Material.PaintSize;
            int faceCount = o.Mesh.Faces.Count;
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                using (var br = new SolidBrush(o.Material.Color))
                    g.FillRectangle(br, 0, 0, s, s);

                Bitmap tex = o.Material.HasTexture ? o.Material.Texture : null;
                if (tex != null)
                {
                    for (int fi = 0; fi < faceCount; fi++)
                    {
                        var face = o.Mesh.Faces[fi];
                        if (face.Length < 3) continue;
                        Vec3 nLocal = LocalNormal(o, face);
                        for (int i = 1; i + 1 < face.Length; i++)
                        {
                            PointF a0 = SceneGeometry.AtlasUV(fi, faceCount, 0,     face.Length);
                            PointF a1 = SceneGeometry.AtlasUV(fi, faceCount, i,     face.Length);
                            PointF a2 = SceneGeometry.AtlasUV(fi, faceCount, i + 1, face.Length);
                            PointF d0 = new PointF(a0.X * s, a0.Y * s);
                            PointF d1 = new PointF(a1.X * s, a1.Y * s);
                            PointF d2 = new PointF(a2.X * s, a2.Y * s);
                            PointF u0 = SceneGeometry.PlanarUV(o.Mesh.Vertices[face[0]],     nLocal);
                            PointF u1 = SceneGeometry.PlanarUV(o.Mesh.Vertices[face[i]],     nLocal);
                            PointF u2 = SceneGeometry.PlanarUV(o.Mesh.Vertices[face[i + 1]], nLocal);
                            FillTexTri(g, tex, d0, d1, d2, u0, u1, u2);
                        }
                    }
                }

                var paint = o.Material.PaintLayer;
                if (paint != null) g.DrawImage(paint, new Rectangle(0, 0, s, s));
            }
            return bmp;
        }

        // Fill the destination triangle (atlas px) with the texture sampled over the
        // source UV triangle (planar UV → texel px), tiling like the live renderer.
        private static void FillTexTri(Graphics g, Bitmap tex, PointF d0, PointF d1, PointF d2,
                                       PointF uv0, PointF uv1, PointF uv2)
        {
            float s0x = uv0.X * tex.Width, s0y = uv0.Y * tex.Height;
            float s1x = uv1.X * tex.Width, s1y = uv1.Y * tex.Height;
            float s2x = uv2.X * tex.Width, s2y = uv2.Y * tex.Height;

            float u1x = s1x - s0x, u1y = s1y - s0y, u2x = s2x - s0x, u2y = s2y - s0y;
            float det = u1x * u2y - u1y * u2x;
            if (Math.Abs(det) < 1e-3f) return;

            float v1x = d1.X - d0.X, v1y = d1.Y - d0.Y, v2x = d2.X - d0.X, v2y = d2.Y - d0.Y;
            if (Math.Abs(v1x * v2y - v1y * v2x) < 0.5f) return;

            float a = (v1x * u2y - u1y * v2x) / det;
            float c = (u1x * v2x - v1x * u2x) / det;
            float b = (v1y * u2y - u1y * v2y) / det;
            float d = (u1x * v2y - v1y * u2x) / det;
            float e = d0.X - (a * s0x + c * s0y);
            float f = d0.Y - (b * s0x + d * s0y);

            try
            {
                using (var path = new GraphicsPath())
                using (var brush = new TextureBrush(tex, WrapMode.Tile))
                {
                    path.AddPolygon(new[] { d0, d1, d2 });
                    brush.Transform = new Matrix(a, b, c, d, e, f);
                    g.FillPath(brush, path);
                }
            }
            catch { /* skip degenerate slivers */ }
        }

        private static Vec3 LocalNormal(SceneObject o, int[] face)
        {
            var v0 = o.Mesh.Vertices[face[0]];
            var v1 = o.Mesh.Vertices[face[1]];
            var v2 = o.Mesh.Vertices[face[2]];
            return Vec3.Cross(v1 - v0, v2 - v0).Normalized();
        }

        private static void WriteObjVt(StringBuilder obj, PointF uv)
        {
            obj.AppendLine(string.Format(Inv, "vt {0:F6} {1:F6}", uv.X, 1f - uv.Y));
        }

        private static void AppendObjCorner(StringBuilder obj, int vIdx, int vtIdx, int vnIdx)
        {
            if (vtIdx > 0)
                obj.Append(string.Format(Inv, " {0}/{1}/{2}", vIdx, vtIdx, vnIdx));
            else
                obj.Append(string.Format(Inv, " {0}//{1}", vIdx, vnIdx));
        }

        private static string MatName(SceneObject obj, int index)
        {
            return string.Format(Inv, "mat_{0:000}_{1}", index, SafeAssetName(obj.Name, "object"));
        }

        private static string ObjectName(SceneObject obj, int index)
        {
            return string.Format(Inv, "obj_{0:000}_{1}", index, SafeAssetName(obj.Name, "object"));
        }

        public static string SafeAssetName(string raw, string fallback)
        {
            string n = string.IsNullOrEmpty(raw) ? fallback : raw;
            var clean = new StringBuilder();
            foreach (char c in n)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')
                    clean.Append(c);
                else
                    clean.Append('_');
            }

            string s = clean.ToString().Trim('_', '-');
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        // World-space triangles of every visible object, fan-triangulated, wound the way
        // the mesh is (outward for the primitives). The canvas sheet is a zero-thickness
        // plane — useless for printing — so STL can leave it out.
        private static IEnumerable<Vec3[]> WorldTriangles(IEnumerable<SceneObject> objects, bool includeCanvas)
        {
            foreach (var obj in FlatObjects(objects))
            {
                if (obj.Mesh == null || obj.Mesh.Vertices == null || obj.Mesh.Faces == null) continue;
                if (!includeCanvas && obj.PrimitiveType == "Canvas") continue;
                var xf = SceneGeometry.Xform.For(obj);
                foreach (var face in obj.Mesh.Faces)
                {
                    if (!ValidFace(obj.Mesh, face)) continue;
                    // Fan-triangulate: STL is triangles-only, so quads/n-gons (cube
                    // faces, cylinder caps…) must be split or the model gets holes.
                    var v0 = xf.Apply(obj.Mesh.Vertices[face[0]]);
                    for (int i = 1; i + 1 < face.Length; i++)
                    {
                        var v1 = xf.Apply(obj.Mesh.Vertices[face[i]]);
                        var v2 = xf.Apply(obj.Mesh.Vertices[face[i + 1]]);
                        if (Vec3.Cross(v1 - v0, v2 - v0).Length < 1e-6f) continue;   // degenerate sliver
                        yield return new[] { v0, v1, v2 };
                    }
                }
            }
        }

        public static bool HasCanvas(IEnumerable<SceneObject> objects)
        {
            foreach (var obj in FlatObjects(objects))
                if (obj.PrimitiveType == "Canvas") return true;
            return false;
        }

        // ASCII STL (kept for tests and for anyone who wants text).
        public static string ToSTL(IEnumerable<SceneObject> objects) { return ToSTL(objects, true); }

        public static string ToSTL(IEnumerable<SceneObject> objects, bool includeCanvas)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid Aplat");
            foreach (var t in WorldTriangles(objects, includeCanvas))
            {
                var n = Vec3.Cross(t[1] - t[0], t[2] - t[0]).Normalized();
                sb.AppendLine(string.Format(Inv, "  facet normal {0:F6} {1:F6} {2:F6}", n.X, n.Y, n.Z));
                sb.AppendLine("    outer loop");
                foreach (var v in t) sb.AppendLine(string.Format(Inv, "      vertex {0:F6} {1:F6} {2:F6}", v.X, v.Y, v.Z));
                sb.AppendLine("    endloop");
                sb.AppendLine("  endfacet");
            }
            sb.AppendLine("endsolid Aplat");
            return sb.ToString();
        }

        // Binary STL: 80-byte header, uint32 triangle count, then 50 bytes per triangle
        // (normal, three vertices as float32, uint16 attribute = 0). About 5× smaller than ASCII.
        public static byte[] ToBinarySTL(IEnumerable<SceneObject> objects, bool includeCanvas)
        {
            var tris = WorldTriangles(objects, includeCanvas).ToList();
            var ms = new System.IO.MemoryStream(84 + tris.Count * 50);
            using (var w = new System.IO.BinaryWriter(ms, Encoding.ASCII, true))
            {
                var head = new byte[80];
                var text = Encoding.ASCII.GetBytes("Aplat binary STL");
                Array.Copy(text, head, text.Length);
                w.Write(head);
                w.Write((uint)tris.Count);
                foreach (var t in tris)
                {
                    var n = Vec3.Cross(t[1] - t[0], t[2] - t[0]).Normalized();
                    w.Write(n.X); w.Write(n.Y); w.Write(n.Z);
                    foreach (var v in t) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
                    w.Write((ushort)0);
                }
            }
            return ms.ToArray();
        }

        // ── FBX ───────────────────────────────────────────────────────────
        // Binary FBX 7.4 (Blender and others do not read the ASCII flavour): header,
        // global settings (Y up, right-handed, 1 unit = 1 m), definitions, objects and the
        // connections that tie them together. Each visible object is a Model (carrying
        // position / rotation / scale) holding a Geometry (local-space vertices, smooth/hard
        // normals, UVs) and a Material (colour, specular, emission, opacity). Textures are
        // not embedded. Property values: int → 'I', long → 'L', double → 'D', bool → 'C',
        // string → 'S', double[] → 'd', int[] → 'i'.
        private sealed class FbxNode
        {
            public readonly string Name;
            public readonly List<object> Props = new List<object>();
            public readonly List<FbxNode> Kids = new List<FbxNode>();
            public FbxNode(string name, params object[] props) { Name = name; Props.AddRange(props); }
            public FbxNode Add(FbxNode k) { Kids.Add(k); return this; }
            public FbxNode Add(string name, params object[] props) { Kids.Add(new FbxNode(name, props)); return this; }
        }

        private static FbxNode FbxP(string name, string type, string label, string flags, params object[] vals)
        {
            var n = new FbxNode("P", name, type, label, flags);
            n.Props.AddRange(vals);
            return n;
        }

        public static byte[] ToFBX(IEnumerable<SceneObject> objects)
        {
            var items = new List<SceneObject>();
            foreach (var o in FlatObjects(objects))
                if (o.Mesh != null && o.Mesh.Vertices != null && o.Mesh.Faces != null && o.Mesh.Vertices.Count > 0) items.Add(o);
            int n = items.Count;

            var roots = new List<FbxNode>();
            roots.Add(new FbxNode("FBXHeaderExtension")
                .Add("FBXHeaderVersion", 1003).Add("FBXVersion", 7400).Add("Creator", "Aplat"));
            roots.Add(new FbxNode("FileId", new byte[] { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 }));
            roots.Add(new FbxNode("CreationTime", "1970-01-01 10:00:00:000"));
            roots.Add(new FbxNode("Creator", "Aplat"));
            roots.Add(new FbxNode("GlobalSettings").Add("Version", 1000).Add(new FbxNode("Properties70")
                .Add(FbxP("UpAxis", "int", "Integer", "", 1))
                .Add(FbxP("UpAxisSign", "int", "Integer", "", 1))
                .Add(FbxP("FrontAxis", "int", "Integer", "", 2))
                .Add(FbxP("FrontAxisSign", "int", "Integer", "", 1))
                .Add(FbxP("CoordAxis", "int", "Integer", "", 0))
                .Add(FbxP("CoordAxisSign", "int", "Integer", "", 1))
                .Add(FbxP("UnitScaleFactor", "double", "Number", "", 100.0))
                .Add(FbxP("OriginalUnitScaleFactor", "double", "Number", "", 100.0))));

            var defs = new FbxNode("Definitions").Add("Version", 100).Add("Count", 3 * n + 1);
            defs.Add(new FbxNode("ObjectType", "GlobalSettings").Add("Count", 1));
            if (n > 0)
                foreach (string type in new[] { "Model", "Geometry", "Material" })
                    defs.Add(new FbxNode("ObjectType", type).Add("Count", n));
            roots.Add(defs);

            var objs = new FbxNode("Objects");
            var conns = new FbxNode("Connections");
            for (int k = 0; k < n; k++)
            {
                var o = items[k];
                long geoId = 100000L + k * 3, modelId = geoId + 1, matId = geoId + 2;
                string name = FbxName(o.Name);
                objs.Add(FbxGeometry(o, geoId, name));

                var pos = o.Position; var rot = o.Rotation; var sc = o.Scale;
                objs.Add(new FbxNode("Model", modelId, name + "\0\u0001Model", "Mesh")
                    .Add("Version", 232)
                    .Add(new FbxNode("Properties70")
                        .Add(FbxP("RotationActive", "bool", "", "", 1))
                        .Add(FbxP("InheritType", "enum", "", "", 1))
                        .Add(FbxP("DefaultAttributeIndex", "int", "Integer", "", 0))
                        .Add(FbxP("Lcl Translation", "Lcl Translation", "", "A", (double)pos.X, (double)pos.Y, (double)pos.Z))
                        .Add(FbxP("Lcl Rotation", "Lcl Rotation", "", "A", (double)rot.X, (double)rot.Y, (double)rot.Z))
                        .Add(FbxP("Lcl Scaling", "Lcl Scaling", "", "A", (double)sc.X, (double)sc.Y, (double)sc.Z)))
                    .Add("Shading", true)
                    .Add("Culling", "CullingOff"));
                objs.Add(FbxMaterial(o.Material, matId, name));

                conns.Add("C", "OO", modelId, 0L);
                conns.Add("C", "OO", geoId, modelId);
                conns.Add("C", "OO", matId, modelId);
            }
            roots.Add(objs);
            roots.Add(conns);
            return WriteFbxBinary(roots, 7400);
        }

        private static double[] D(params float[] v) { var d = new double[v.Length]; for (int i = 0; i < v.Length; i++) d[i] = v[i]; return d; }

        private static FbxNode FbxGeometry(SceneObject o, long id, string name)
        {
            var mesh = o.Mesh;
            var smooth = SmoothNormals(mesh);
            int faceCount = mesh.Faces.Count;
            var idx = new List<int>();
            var nrm = new List<double>();
            var uv  = new List<double>();
            for (int fi = 0; fi < faceCount; fi++)
            {
                var face = mesh.Faces[fi];
                if (!ValidFace(mesh, face)) continue;
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    Vec3 a = mesh.Vertices[face[0]], b = mesh.Vertices[face[i]], c = mesh.Vertices[face[i + 1]];
                    if (Vec3.Cross(b - a, c - a).Length < 1e-6f) continue;
                    int[] corner = { 0, i, i + 1 };
                    for (int q = 0; q < 3; q++)
                    {
                        int vi = face[corner[q]];
                        idx.Add(q == 2 ? ~vi : vi);          // the last index of a polygon is stored as ~index
                        Vec3 nn = smooth[fi][corner[q]];
                        nrm.Add(nn.X); nrm.Add(nn.Y); nrm.Add(nn.Z);
                        var t = SceneGeometry.PaintUV(o, fi, faceCount, corner[q], face.Length);
                        uv.Add(t.X); uv.Add(1f - t.Y);
                    }
                }
            }

            var verts = new double[mesh.Vertices.Count * 3];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                verts[3 * i] = v.X; verts[3 * i + 1] = v.Y; verts[3 * i + 2] = v.Z;
            }

            var g = new FbxNode("Geometry", id, name + "\0\u0001Geometry", "Mesh");
            g.Add(new FbxNode("Properties70"));
            g.Add("Vertices", verts);
            g.Add("PolygonVertexIndex", idx.ToArray());
            g.Add("GeometryVersion", 124);
            g.Add(new FbxNode("LayerElementNormal", 0).Add("Version", 101).Add("Name", "")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "Direct")
                .Add("Normals", nrm.ToArray()));
            g.Add(new FbxNode("LayerElementUV", 0).Add("Version", 101).Add("Name", "UVMap")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "Direct")
                .Add("UV", uv.ToArray()));
            g.Add(new FbxNode("LayerElementMaterial", 0).Add("Version", 101).Add("Name", "")
                .Add("MappingInformationType", "AllSame").Add("ReferenceInformationType", "IndexToDirect")
                .Add("Materials", new[] { 0 }));
            var layer = new FbxNode("Layer", 0).Add("Version", 100);
            foreach (string t in new[] { "LayerElementNormal", "LayerElementMaterial", "LayerElementUV" })
                layer.Add(new FbxNode("LayerElement").Add("Type", t).Add("TypedIndex", 0));
            g.Add(layer);
            return g;
        }

        private static FbxNode FbxMaterial(Material m, long id, string name)
        {
            Color col = m != null ? m.Color : Color.Gray;
            float metal = m != null ? Clamp01(m.Metallic) : 0f;
            float rough = m != null ? Clamp01(m.Roughness) : 0.55f;
            float opacity = m != null ? Clamp01(m.Opacity) : 1f;
            float emit = m != null ? Clamp01(m.EmissionStrength) : 0f;
            Color ec = m != null ? m.EmissionColor : Color.Black;
            float r = col.R / 255f, g = col.G / 255f, b = col.B / 255f;
            Func<float, float> ks = c => 0.04f * (1f - metal) + c * metal;
            float shin = Math.Max(1f, (1f - rough) * (1f - rough) * 1000f);

            return new FbxNode("Material", id, name + "\0\u0001Material", "")
                .Add("Version", 102)
                .Add("ShadingModel", "phong")
                .Add("MultiLayer", 0)
                .Add(new FbxNode("Properties70")
                    .Add(FbxP("ShadingModel", "KString", "", "", "phong"))
                    .Add(FbxP("DiffuseColor", "Color", "", "A", (double)r, (double)g, (double)b))
                    .Add(FbxP("DiffuseFactor", "Number", "", "A", 1.0))
                    .Add(FbxP("AmbientColor", "Color", "", "A", (double)(r * 0.2f), (double)(g * 0.2f), (double)(b * 0.2f)))
                    .Add(FbxP("EmissiveColor", "Color", "", "A", (double)(ec.R / 255f * emit), (double)(ec.G / 255f * emit), (double)(ec.B / 255f * emit)))
                    .Add(FbxP("EmissiveFactor", "Number", "", "A", 1.0))
                    .Add(FbxP("SpecularColor", "Color", "", "A", (double)ks(r), (double)ks(g), (double)ks(b)))
                    .Add(FbxP("SpecularFactor", "Number", "", "A", 1.0))
                    .Add(FbxP("Shininess", "Number", "", "A", (double)shin))
                    .Add(FbxP("TransparencyFactor", "Number", "", "A", (double)(1f - opacity)))
                    .Add(FbxP("Opacity", "Number", "", "A", (double)opacity)));
        }

        // Binary layout (version < 7500): "Kaydara FBX Binary  \0", 0x1A 0x00, uint32 version, then
        // node records {uint32 end offset, uint32 property count, uint32 property bytes, byte name
        // length, name, properties, children, 13 zero bytes when there are children}, a closing
        // 13-zero-byte record, the footer.
        private static byte[] WriteFbxBinary(List<FbxNode> roots, int version)
        {
            var ms = new System.IO.MemoryStream();
            using (var w = new System.IO.BinaryWriter(ms, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0"));
                w.Write(new byte[] { 0x1A, 0x00 });
                w.Write((uint)version);
                foreach (var r in roots) WriteFbxNode(w, r);
                w.Write(new byte[13]);

                // footer
                w.Write(new byte[] { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e });
                w.Write(new byte[4]);
                long pos = ms.Position;
                long pad = ((pos + 15) & ~15L) - pos;
                if (pad == 0) pad = 16;
                w.Write(new byte[pad]);
                w.Write((uint)version);
                w.Write(new byte[120]);
                w.Write(new byte[] { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b });
            }
            return ms.ToArray();
        }

        private static void WriteFbxNode(System.IO.BinaryWriter w, FbxNode node)
        {
            long start = w.BaseStream.Position;
            byte[] name = Encoding.ASCII.GetBytes(node.Name);
            w.Write(0u);                       // end offset, patched below
            w.Write((uint)node.Props.Count);
            w.Write(0u);                       // property bytes, patched below
            w.Write((byte)name.Length);
            w.Write(name);
            long propStart = w.BaseStream.Position;
            foreach (object p in node.Props) WriteFbxProp(w, p);
            long propEnd = w.BaseStream.Position;
            foreach (var k in node.Kids) WriteFbxNode(w, k);
            if (node.Kids.Count > 0) w.Write(new byte[13]);
            long end = w.BaseStream.Position;
            w.Seek((int)start, System.IO.SeekOrigin.Begin);
            w.Write((uint)end);
            w.Write((uint)node.Props.Count);
            w.Write((uint)(propEnd - propStart));
            w.Seek((int)end, System.IO.SeekOrigin.Begin);
        }

        private static void WriteFbxProp(System.IO.BinaryWriter w, object p)
        {
            if (p is int)         { w.Write((byte)'I'); w.Write((int)p); }
            else if (p is long)   { w.Write((byte)'L'); w.Write((long)p); }
            else if (p is double) { w.Write((byte)'D'); w.Write((double)p); }
            else if (p is bool)   { w.Write((byte)'C'); w.Write((byte)((bool)p ? 1 : 0)); }
            else if (p is string)
            {
                byte[] s = Encoding.ASCII.GetBytes((string)p);
                w.Write((byte)'S'); w.Write((uint)s.Length); w.Write(s);
            }
            else if (p is byte[]) { var r = (byte[])p; w.Write((byte)'R'); w.Write((uint)r.Length); w.Write(r); }
            else if (p is double[])
            {
                var a = (double[])p;
                w.Write((byte)'d'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 8));
                foreach (double v in a) w.Write(v);
            }
            else if (p is int[])
            {
                var a = (int[])p;
                w.Write((byte)'i'); w.Write((uint)a.Length); w.Write(0u); w.Write((uint)(a.Length * 4));
                foreach (int v in a) w.Write(v);
            }
            else throw new ArgumentException("Unsupported FBX property " + p.GetType().Name);
        }

        // Write a file atomically: into a temp file next to it, then swap it in, so a
        // failure half-way never leaves a truncated export (or destroys the old one).
        public static void WriteFileAtomic(string path, Action<System.IO.Stream> write)
        {
            string temp = path + ".tmp";
            try
            {
                using (var fs = new System.IO.FileStream(temp, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    write(fs);
                if (!System.IO.File.Exists(path)) { System.IO.File.Move(temp, path); return; }
                try { System.IO.File.Replace(temp, path, null); }
                catch (PlatformNotSupportedException) { System.IO.File.Delete(path); System.IO.File.Move(temp, path); }
            }
            catch
            {
                try { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); } catch { /* the original error matters */ }
                throw;
            }
        }

        // A name goes between quotes in the file: keep it on one line and free of quotes.
        private static string FbxName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Object";
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(c == '"' || c == '\\' || c < ' ' || c > '~' ? '_' : c);
            return sb.ToString();
        }

        private static IEnumerable<SceneObject> FlatObjects(IEnumerable<SceneObject> objects)
        {
            foreach (var obj in objects)
            {
                if (!obj.IsVisible) continue;   // hidden objects aren't exported
                if (obj.PrimitiveType == Aplat.Primitives.RulerFactory.Type) continue;   // the ruler is a helper, not a shape
                if (obj.IsGroup)
                    foreach (var c in FlatObjects(obj.Children)) yield return c;
                else
                    yield return obj;
            }
        }
    }
}
