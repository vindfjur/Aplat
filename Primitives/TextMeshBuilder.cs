using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Aplat.Models;

namespace Aplat.Primitives
{
    // Builds a real volumetric (extruded) mesh from text. The glyph outline is
    // flattened to polygons, the caps are triangulated with proper holes (ear
    // clipping with hole bridging — so the counters of O, A, e … are open), and
    // side walls are extruded along Z. The result is a solid 3-D object you can
    // rotate, scale, texture and paint like any other shape.
    public static class TextMeshBuilder
    {
        // size  = desired text height in world units (e.g. 1.5 ≈ medium cube).
        // depth = extrusion thickness: size × depthRatio (0.28 unless the object says otherwise).
        public const float DefaultDepthRatio = 0.28f;

        // depthRatio: extrusion thickness as a fraction of the height (0.28 by default).
        public static Mesh Build(string text, string fontFamily, float size, float depthRatio = DefaultDepthRatio)
        {
            var mesh = new Mesh();
            if (string.IsNullOrEmpty(text)) text = "Text";
            if (size < 0.1f) size = 1.5f;
            if (depthRatio <= 0f) depthRatio = DefaultDepthRatio;

            float depth = size * Math.Max(0.02f, Math.Min(3f, depthRatio));

            var raw = OutlineContours(text, fontFamily, out RectangleF bounds);
            if (raw.Count == 0) return mesh;

            // Scale so the glyph is exactly `size` world units tall, centred at origin.
            float h     = Math.Max(bounds.Height, 1e-3f);
            float scale = size / h;
            float bcx   = bounds.X + bounds.Width  / 2f;
            float bcy   = bounds.Y + bounds.Height / 2f;
            float d2    = depth / 2f;

            // To model space (flip Y: screen rows go down, world Y goes up).
            var polys = new List<List<PointF>>();
            foreach (var c in raw)
            {
                var p = new List<PointF>(c.Count);
                foreach (var q in c) p.Add(new PointF((q.X - bcx) * scale, -(q.Y - bcy) * scale));
                if (p.Count >= 3) polys.Add(p);
            }
            if (polys.Count == 0) return mesh;

            // Nesting depth → even = solid contour, odd = hole.
            int n = polys.Count;
            int[] depthOf = new int[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i != j && Contains(polys[j], polys[i][0])) depthOf[i]++;

            // Force winding: solids CCW, holes CW (required by the hole-bridging step
            // and gives correct wall normals: outward for solids, into the cavity for holes).
            for (int i = 0; i < n; i++)
            {
                bool hole = (depthOf[i] & 1) == 1;
                float area = SignedArea(polys[i]);
                if (hole && area > 0) polys[i].Reverse();
                if (!hole && area < 0) polys[i].Reverse();
            }

            // Group each solid with the holes whose immediate parent it is.
            for (int i = 0; i < n; i++)
            {
                if ((depthOf[i] & 1) == 1) continue;          // skip holes here
                var outer = polys[i];
                var holes = new List<List<PointF>>();
                for (int j = 0; j < n; j++)
                {
                    if (j == i || (depthOf[j] & 1) == 0) continue;
                    if (ImmediateParent(polys, depthOf, j) == i) holes.Add(polys[j]);
                }

                BuildCaps(mesh, outer, holes, d2);
                AddWalls(mesh, outer, d2);
                foreach (var hole in holes) AddWalls(mesh, hole, d2);
            }

            return mesh;
        }

        // ── Caps: triangulate (with holes) and add front (+Z) / back (−Z) ──
        private static void BuildCaps(Mesh mesh, List<PointF> outer,
                                      List<List<PointF>> holes, float d2)
        {
            List<PointF> merged = holes.Count > 0 ? MergeHoles(outer, holes)
                                                  : new List<PointF>(outer);
            var tris = EarClip(merged);

            if (tris.Count == 0)
            {
                // Fallback: at least cap the outer contour as a single n-gon so the
                // glyph is never see-through (holes filled in this rare case).
                AddNgonCap(mesh, outer, d2);
                return;
            }

            int baseF = mesh.Vertices.Count;
            foreach (var p in merged) mesh.Vertices.Add(new Vec3(p.X, p.Y,  d2));
            int baseB = mesh.Vertices.Count;
            foreach (var p in merged) mesh.Vertices.Add(new Vec3(p.X, p.Y, -d2));

            foreach (var t in tris)
            {
                mesh.Faces.Add(new[] { baseF + t[0], baseF + t[1], baseF + t[2] });   // front +Z
                mesh.Faces.Add(new[] { baseB + t[2], baseB + t[1], baseB + t[0] });   // back  −Z
            }
        }

        private static void AddNgonCap(Mesh mesh, List<PointF> poly, float d2)
        {
            int nn = poly.Count;
            int bf = mesh.Vertices.Count;
            foreach (var p in poly) mesh.Vertices.Add(new Vec3(p.X, p.Y,  d2));
            int bb = mesh.Vertices.Count;
            foreach (var p in poly) mesh.Vertices.Add(new Vec3(p.X, p.Y, -d2));

            var front = new int[nn];
            for (int i = 0; i < nn; i++) front[i] = bf + i;
            mesh.Faces.Add(front);
            var back = new int[nn];
            for (int i = 0; i < nn; i++) back[i] = bb + (nn - 1 - i);
            mesh.Faces.Add(back);
        }

        private static void AddWalls(Mesh mesh, List<PointF> poly, float d2)
        {
            int nn = poly.Count;
            int bf = mesh.Vertices.Count;
            foreach (var p in poly) mesh.Vertices.Add(new Vec3(p.X, p.Y,  d2));
            int bb = mesh.Vertices.Count;
            foreach (var p in poly) mesh.Vertices.Add(new Vec3(p.X, p.Y, -d2));

            for (int i = 0; i < nn; i++)
            {
                int j = (i + 1) % nn;
                mesh.Faces.Add(new[] { bf + i, bb + i, bb + j, bf + j });
            }
        }

        // ── Hole bridging (Eberly): splice each CW hole into the CCW outer ──
        private static List<PointF> MergeHoles(List<PointF> outer, List<List<PointF>> holes)
        {
            var poly = new List<PointF>(outer);
            // Process holes right-to-left so bridges don't cross later holes.
            foreach (var hole in holes.OrderByDescending(hh => hh.Max(p => p.X)))
            {
                int mi = 0;
                for (int i = 1; i < hole.Count; i++) if (hole[i].X > hole[mi].X) mi = i;
                PointF m = hole[mi];

                int bridge = FindBridge(poly, m);
                if (bridge < 0) continue;       // unreachable hole → leave it (its walls still show)

                var insert = new List<PointF> { m };                 // B → M
                for (int k = 1; k < hole.Count; k++)
                    insert.Add(hole[(mi + k) % hole.Count]);          // around the hole
                insert.Add(m);                                       // close hole at M
                insert.Add(poly[bridge]);                            // M → B
                poly.InsertRange(bridge + 1, insert);
            }
            return poly;
        }

        // Nearest outer vertex visible from M along +X (approximate Eberly bridge).
        private static int FindBridge(List<PointF> poly, PointF m)
        {
            int np = poly.Count;
            float bestX = float.MaxValue;
            int ea = -1, eb = -1;
            for (int i = 0; i < np; i++)
            {
                PointF p1 = poly[i], p2 = poly[(i + 1) % np];
                if ((p1.Y > m.Y) == (p2.Y > m.Y)) continue;          // edge must straddle M.Y
                float t  = (m.Y - p1.Y) / (p2.Y - p1.Y);
                float ix = p1.X + t * (p2.X - p1.X);
                if (ix < m.X - 1e-6f) continue;                      // only to the right
                if (ix < bestX) { bestX = ix; ea = i; eb = (i + 1) % np; }
            }
            if (ea < 0) return -1;

            int p = poly[ea].X > poly[eb].X ? ea : eb;               // edge endpoint with larger X
            PointF ipt = new PointF(bestX, m.Y);
            PointF pp  = poly[p];

            int best = p;
            float bestAng = float.MaxValue;
            for (int i = 0; i < np; i++)
            {
                if (i == p) continue;
                if (!PointInTri(poly[i], m, ipt, pp, true)) continue;
                float ang = Math.Abs((float)Math.Atan2(poly[i].Y - m.Y, poly[i].X - m.X));
                if (ang < bestAng) { bestAng = ang; best = i; }
            }
            return best;
        }

        // ── Ear clipping for a simple CCW polygon → triangle index triples ──
        private static List<int[]> EarClip(List<PointF> poly)
        {
            var result = new List<int[]>();
            int n = poly.Count;
            if (n < 3) return result;

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            if (PolyArea(poly, idx) < 0) idx.Reverse();   // ensure CCW

            int guard = n * n + 16;
            while (idx.Count > 3 && guard-- > 0)
            {
                int cnt = idx.Count;
                bool clipped = false;
                int  fbK = -1; float fbCross = -1f;                  // fallback: best convex vertex

                for (int k = 0; k < cnt; k++)
                {
                    int i0 = idx[(k + cnt - 1) % cnt];
                    int i1 = idx[k];
                    int i2 = idx[(k + 1) % cnt];
                    PointF a = poly[i0], b = poly[i1], c = poly[i2];
                    float cr = Cross(a, b, c);
                    if (cr <= 1e-7f) continue;                        // reflex / collinear
                    if (cr > fbCross) { fbCross = cr; fbK = k; }      // remember most convex

                    bool ear = true;
                    for (int mm = 0; mm < cnt; mm++)
                    {
                        int im = idx[mm];
                        if (im == i0 || im == i1 || im == i2) continue;
                        PointF pm = poly[im];
                        // Skip vertices coincident with a corner (hole bridges add
                        // duplicate vertices; comparing by index alone would let such
                        // a duplicate falsely block a valid ear and stall the clip).
                        if (Coincident(pm, a) || Coincident(pm, b) || Coincident(pm, c)) continue;
                        if (PointInTri(pm, a, b, c, false)) { ear = false; break; }
                    }
                    if (!ear) continue;

                    result.Add(new[] { i0, i1, i2 });
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    // No clean ear this pass: force-clip the most convex vertex so the
                    // cap fills completely instead of falling back to a solid n-gon
                    // (which would plug the letter's hole).
                    if (fbK < 0) break;                              // truly degenerate
                    int cntF = idx.Count;
                    int j0 = idx[(fbK + cntF - 1) % cntF];
                    int j1 = idx[fbK];
                    int j2 = idx[(fbK + 1) % cntF];
                    result.Add(new[] { j0, j1, j2 });
                    idx.RemoveAt(fbK);
                }
            }
            if (idx.Count == 3) result.Add(new[] { idx[0], idx[1], idx[2] });
            return result;
        }

        // ── Geometry helpers ──────────────────────────────────────
        private static float Cross(PointF a, PointF b, PointF c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        // Two points at (almost) the same spot — used to ignore duplicate bridge
        // vertices during ear clipping. Model space is ~1 unit tall, so 1e-5 is tiny.
        private static bool Coincident(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) < 1e-5f && Math.Abs(a.Y - b.Y) < 1e-5f;
        }

        private static bool PointInTri(PointF p, PointF a, PointF b, PointF c, bool onBoundary)
        {
            float d1 = Cross(a, b, p);
            float d2 = Cross(b, c, p);
            float d3 = Cross(c, a, p);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            bool strictlyInside = !(hasNeg && hasPos);
            if (strictlyInside) return true;
            if (!onBoundary) return false;
            const float e = 1e-6f;
            return Math.Abs(d1) < e || Math.Abs(d2) < e || Math.Abs(d3) < e;
        }

        private static float SignedArea(List<PointF> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                PointF u = p[i], v = p[(i + 1) % p.Count];
                a += u.X * v.Y - v.X * u.Y;
            }
            return a * 0.5f;
        }

        private static float PolyArea(List<PointF> p, List<int> idx)
        {
            float a = 0f;
            for (int i = 0; i < idx.Count; i++)
            {
                PointF u = p[idx[i]], v = p[idx[(i + 1) % idx.Count]];
                a += u.X * v.Y - v.X * u.Y;
            }
            return a * 0.5f;
        }

        private static bool Contains(List<PointF> poly, PointF pt)
        {
            bool inside = false;
            int np = poly.Count;
            for (int i = 0, j = np - 1; i < np; j = i++)
            {
                if (((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)) &&
                    (pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) /
                            (poly[j].Y - poly[i].Y) + poly[i].X))
                    inside = !inside;
            }
            return inside;
        }

        private static int ImmediateParent(List<List<PointF>> polys, int[] depthOf, int j)
        {
            int parent = -1, parentDepth = -1;
            for (int i = 0; i < polys.Count; i++)
            {
                if (i == j) continue;
                if (depthOf[i] >= depthOf[j]) continue;
                if (!Contains(polys[i], polys[j][0])) continue;
                if (depthOf[i] > parentDepth) { parentDepth = depthOf[i]; parent = i; }
            }
            return parent;
        }

        // ── Glyph outline → flattened contours ────────────────────
        private static List<List<PointF>> OutlineContours(string text, string fontFamily,
                                                          out RectangleF bounds)
        {
            var result = new List<List<PointF>>();
            bounds = RectangleF.Empty;

            using (var gp = new GraphicsPath())
            {
                FontFamily fam;
                try { fam = new FontFamily(fontFamily); }
                catch { fam = FontFamily.GenericSansSerif; }

                using (fam)
                    gp.AddString(text, fam, (int)FontStyle.Bold, 100f,
                                 new PointF(0, 0), StringFormat.GenericDefault);

                if (gp.PointCount == 0) return result;
                gp.Flatten(null, 0.18f);   // finer → smoother curves on rounds/holes
                if (gp.PointCount == 0) return result;

                bounds        = gp.GetBounds();
                PointF[] pts  = gp.PathPoints;
                byte[]  types = gp.PathTypes;

                List<PointF> cur = null;
                for (int i = 0; i < pts.Length; i++)
                {
                    if ((types[i] & 0x07) == 0)        // PathPointType.Start
                    {
                        cur = new List<PointF>();
                        result.Add(cur);
                    }
                    if (cur != null) cur.Add(pts[i]);
                }
            }

            result.RemoveAll(c => c.Count < 3);
            return result;
        }
    }
}
