using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Controls
{
    // Text on the surface of a 3D shape: the same live text element as on the sheet (type in place, move, resize,
    // rotate, restyle from the "Text" section of the 2D panel; Enter or a click elsewhere writes it into the paint,
    // Esc removes it).
    //
    // A shape has no flat texture to type on: its paint is an atlas of small cells, one per triangle. So the text
    // is a screen-space element that is projected onto the surface the way the camera sees it: every texel of
    // the facing triangles is traced back to the surface point it stands for, then to the screen, and takes the
    // colour of the text there. The text therefore reads upright from the camera, in perspective, across the
    // edges of faces. The view is frozen while the text is live; moving the camera commits it.
    public partial class SceneView
    {
        private sealed class SurfaceMap
        {
            public Camera Cam;           // the camera at the click
            public int    W, H;          // the viewport at the click
            public int Cols;
            public List<SurfaceTriangle>[] Bins;

            // Exact depth at the projected texel, rather than a rounded depth-buffer pixel.
            // This also handles a concave object's own occlusion and other objects in front.
            public bool Visible(PointF screen, float depth)
            {
                if (screen.X < 0 || screen.Y < 0 || screen.X >= W || screen.Y >= H) return false;
                var bin = Bins[(int)screen.Y / 32 * Cols + (int)screen.X / 32];
                if (bin == null) return true;
                foreach (var t in bin)
                {
                    float b = ((screen.X - t.A.X) * (t.C.Y - t.A.Y) - (screen.Y - t.A.Y) * (t.C.X - t.A.X)) / t.Det;
                    float c = ((t.B.X - t.A.X) * (screen.Y - t.A.Y) - (t.B.Y - t.A.Y) * (screen.X - t.A.X)) / t.Det;
                    float a = 1f - b - c;
                    if (a < -1e-5f || b < -1e-5f || c < -1e-5f) continue;
                    float z = Cam.Mode == ViewMode.Perspective
                        ? -1f / (a / -t.ZA + b / -t.ZB + c / -t.ZC)
                        : a * t.ZA + b * t.ZB + c * t.ZC;
                    if (z > depth + Math.Max(1f, Math.Abs(depth)) * 1e-5f) return false;
                }
                return true;
            }
        }

        private struct SurfaceTriangle
        {
            public PointF A, B, C;
            public float ZA, ZB, ZC, Det;
        }

        private SurfaceMap _surf;

        private SurfaceMap CaptureSurfaceMap()
        {
            var s = new SurfaceMap
            {
                W = W, H = H,
                Cam = new Camera { AngleX = _camera.AngleX, AngleY = _camera.AngleY, Distance = _camera.Distance,
                                   Target = _camera.Target, Mode = _camera.Mode, OrthoScale = _camera.OrthoScale },
            };
            s.Cols = (s.W + 31) / 32;
            s.Bins = new List<SurfaceTriangle>[s.Cols * ((s.H + 31) / 32)];
            foreach (var top in _scene.Objects)
            {
                if (!top.IsVisible) continue;
                foreach (var obj in SceneGeometry.Leaves(top))
                {
                    if (!obj.IsVisible || obj.Mesh == null || obj.PrimitiveType == Aplat.Primitives.RulerFactory.Type) continue;
                    var mesh = obj.Mesh;
                    var xf = SceneGeometry.Xform.For(obj);
                    var cam = new Vec3[mesh.Vertices.Count];
                    var screen = new PointF[cam.Length];
                    for (int v = 0; v < cam.Length; v++)
                    {
                        cam[v] = s.Cam.ToCameraSpace(xf.Apply(mesh.Vertices[v]));
                        screen[v] = s.Cam.ProjectCamera(cam[v], s.W, s.H);
                    }
                    foreach (var face in mesh.Faces)
                    {
                        for (int i = 1; i + 1 < face.Length; i++)
                        {
                            int ia = face[0], ib = face[i], ic = face[i + 1];
                            if (s.Cam.Mode == ViewMode.Perspective && (cam[ia].Z >= -0.01f || cam[ib].Z >= -0.01f || cam[ic].Z >= -0.01f)) continue;
                            var t = new SurfaceTriangle { A = screen[ia], B = screen[ib], C = screen[ic], ZA = cam[ia].Z, ZB = cam[ib].Z, ZC = cam[ic].Z };
                            t.Det = (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
                            if (Math.Abs(t.Det) < 1e-6f) continue;
                            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))));
                            int x1 = Math.Min(s.W - 1, (int)Math.Ceiling(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X))));
                            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))));
                            int y1 = Math.Min(s.H - 1, (int)Math.Ceiling(Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))));
                            if (x0 > x1 || y0 > y1) continue;
                            for (int y = y0 / 32; y <= y1 / 32; y++)
                                for (int x = x0 / 32; x <= x1 / 32; x++)
                                {
                                    int at = y * s.Cols + x;
                                    if (s.Bins[at] == null) s.Bins[at] = new List<SurfaceTriangle>();
                                    s.Bins[at].Add(t);
                                }
                        }
                    }
                }
            }
            return s;
        }

        private void BeginSurfaceText(SceneObject obj, Point screen)
        {
            CommitLive();
            var layer = PaintLayerFor(obj);
            _surf = CaptureSurfaceMap();
            _liveCanvas = obj;
            TransformStarted?.Invoke(this, EventArgs.Empty);   // one undo step per text

            var item = new LiveItem
            {
                Kind       = LiveKind.Text,
                Text       = "",
                FontFamily = TextFontFamily,
                FontStyle  = TextFontStyle,
                FontPx     = PtToPx(TextSizePt, layer),
                Align      = TextAlignment,
                Color      = LiveColor(),
            };
            // The click is where the first line sits, as on the sheet.
            SizeF sz = item.TextLayoutSize;
            float ax = item.Align == StringAlignment.Center ? 0.5f : item.Align == StringAlignment.Far ? 1f : 0f;
            item.P1 = new PointF(screen.X - ax * sz.Width, screen.Y - item.LineHeight / 2f);
            item.P2 = new PointF(item.P1.X + sz.Width, item.P1.Y + sz.Height);
            StartLive(obj, item, new Bitmap(layer), null);
            EnterTextEdit(0, 0);
        }

        private bool SurfaceCameraMoved()
        {
            if (_surf == null) return false;
            var c = _surf.Cam;
            return c.AngleX != _camera.AngleX || c.AngleY != _camera.AngleY || c.Distance != _camera.Distance
                || c.Mode != _camera.Mode || c.OrthoScale != _camera.OrthoScale
                || (c.Target - _camera.Target).Length > 1e-6f || _surf.W != W || _surf.H != H;
        }

        private void EndSurface() { _surf = null; }

        // Layer = base + the element projected onto the shape.
        private void RenderSurfaceLive()
        {
            var layer = PaintLayerFor(_liveCanvas);
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_liveBase, 0, 0);
            }
            Rectangle r = _live.PaintBounds(1 << 16, 1 << 16);
            r = Rectangle.Intersect(r, new Rectangle(-64, -64, _surf.W + 128, _surf.H + 128));
            if (!r.IsEmpty)
            {
                using (var src = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(src))
                    {
                        g.TranslateTransform(-r.X, -r.Y);
                        _live.Render(g);
                    }
                    ProjectOnto(layer, _liveCanvas, src, r.Location, _surf);
                }
            }
            _liveCanvas.Material.MarkPaintDirty();
            _liveDrawn = new Rectangle(Point.Empty, layer.Size);
            Redraw();
        }

        // Paints `src` (a picture laid on the screen at `origin`) onto the object's paint layer as the camera of `s` sees it.
        private static void ProjectOnto(Bitmap layer, SceneObject obj, Bitmap src, Point origin, SurfaceMap s)
        {
            var mesh = obj.Mesh;
            if (mesh == null) return;
            var xf = SceneGeometry.Xform.For(obj);
            var world = new Vec3[mesh.Vertices.Count];
            var camera = new Vec3[world.Length];
            for (int i = 0; i < world.Length; i++)
            {
                world[i] = xf.Apply(mesh.Vertices[i]);
                camera[i] = s.Cam.ToCameraSpace(world[i]);
            }

            bool persp = s.Cam.Mode == ViewMode.Perspective;
            Vec3 camPos = persp ? s.Cam.GetPosition() : Vec3.Zero;
            Vec3 ortho  = persp ? Vec3.Zero : s.Cam.GetOrthoViewDir();
            var srcRect = new Rectangle(origin, src.Size);
            int lw = layer.Width, lh = layer.Height, faceCount = mesh.Faces.Count;

            var sd = src.LockBits(new Rectangle(Point.Empty, src.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var ld = layer.LockBits(new Rectangle(0, 0, lw, lh), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var sp = new int[sd.Stride / 4 * sd.Height];
                System.Runtime.InteropServices.Marshal.Copy(sd.Scan0, sp, 0, sp.Length);
                var lp = new int[ld.Stride / 4 * lh];
                System.Runtime.InteropServices.Marshal.Copy(ld.Scan0, lp, 0, lp.Length);
                int sStride = sd.Stride / 4, lStride = ld.Stride / 4;

                for (int fi = 0; fi < faceCount; fi++)
                {
                    int[] face = mesh.Faces[fi];
                    for (int i = 1; i + 1 < face.Length; i++)
                    {
                        Vec3 a = world[face[0]], b = world[face[i]], c = world[face[i + 1]];
                        Vec3 ca = camera[face[0]], cb = camera[face[i]], cc = camera[face[i + 1]];
                        if (persp && (ca.Z >= -0.01f || cb.Z >= -0.01f || cc.Z >= -0.01f)) continue;
                        Vec3 n = Vec3.Cross(b - a, c - a);
                        if (Vec3.Dot(n, persp ? camPos - a : ortho) <= 0f) continue;      // facing away
                        PointF pa = s.Cam.ProjectCamera(ca, s.W, s.H), pb = s.Cam.ProjectCamera(cb, s.W, s.H), pc = s.Cam.ProjectCamera(cc, s.W, s.H);
                        float x0 = Math.Min(pa.X, Math.Min(pb.X, pc.X)), x1 = Math.Max(pa.X, Math.Max(pb.X, pc.X));
                        float y0 = Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)), y1 = Math.Max(pa.Y, Math.Max(pb.Y, pc.Y));
                        if (x1 < srcRect.Left || x0 > srcRect.Right || y1 < srcRect.Top || y0 > srcRect.Bottom) continue;

                        PointF u0 = SceneGeometry.PaintUV(obj, fi, faceCount, 0, face.Length);
                        PointF u1 = SceneGeometry.PaintUV(obj, fi, faceCount, i, face.Length);
                        PointF u2 = SceneGeometry.PaintUV(obj, fi, faceCount, i + 1, face.Length);
                        double det = (u1.X - u0.X) * (u2.Y - u0.Y) - (u2.X - u0.X) * (u1.Y - u0.Y);
                        if (Math.Abs(det) < 1e-12) continue;
                        int tx0 = Math.Max(0, (int)Math.Floor(Math.Min(u0.X, Math.Min(u1.X, u2.X)) * lw) - 1);
                        int tx1 = Math.Min(lw - 1, (int)Math.Ceiling(Math.Max(u0.X, Math.Max(u1.X, u2.X)) * lw) + 1);
                        int ty0 = Math.Max(0, (int)Math.Floor(Math.Min(u0.Y, Math.Min(u1.Y, u2.Y)) * lh) - 1);
                        int ty1 = Math.Min(lh - 1, (int)Math.Ceiling(Math.Max(u0.Y, Math.Max(u1.Y, u2.Y)) * lh) + 1);

                        for (int ty = ty0; ty <= ty1; ty++)
                            for (int tx = tx0; tx <= tx1; tx++)
                            {
                                if (!PaintBary(new PointF(u0.X * lw, u0.Y * lh),
                                               new PointF(u1.X * lw, u1.Y * lh),
                                               new PointF(u2.X * lw, u2.Y * lh),
                                               tx + 0.5f, ty + 0.5f, i, face.Length,
                                               out float l0, out float l1, out float l2)) continue;
                                Vec3 cp = ca * l0 + cb * l1 + cc * l2;
                                PointF sc = s.Cam.ProjectCamera(cp, s.W, s.H);
                                if (!s.Visible(sc, cp.Z)) continue;
                                int src0 = Sample(sp, sStride, src.Width, src.Height, sc.X - origin.X - 0.5f, sc.Y - origin.Y - 0.5f);
                                int sa = src0 >> 24 & 255;
                                if (sa == 0) continue;
                                int idx = ty * lStride + tx;
                                lp[idx] = Over(lp[idx], src0);
                            }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(lp, 0, ld.Scan0, lp.Length);
            }
            finally { src.UnlockBits(sd); layer.UnlockBits(ld); }
        }

        // Extend the surface colour into a 1.5-texel gutter, so linear texture
        // filtering cannot pull transparent pixels into a seam. Sample the actual
        // triangle edge, never an extrapolated point on another part of the mesh.
        // Internal fan diagonals have no gutter: the next triangle owns that area.
        private static bool PaintBary(PointF a, PointF b, PointF c, float x, float y,
                                      int fanIndex, int vertexCount, out float l0, out float l1, out float l2)
        {
            if (BaryF(a, b, c, x, y, out l0, out l1, out l2) && l0 >= 0 && l1 >= 0 && l2 >= 0) return true;
            if ((l1 < 0 && fanIndex + 1 < vertexCount - 1) || (l2 < 0 && fanIndex > 1)) return false;
            var p = new Vec3(x, y, 0);
            var q = ClosestPointOnTri(p, new Vec3(a.X, a.Y, 0), new Vec3(b.X, b.Y, 0), new Vec3(c.X, c.Y, 0));
            if ((p - q).Length > 1.5f || !BaryF(a, b, c, q.X, q.Y, out l0, out l1, out l2)) return false;
            l0 = Math.Max(0, l0); l1 = Math.Max(0, l1); l2 = Math.Max(0, l2);
            float sum = l0 + l1 + l2;
            if (sum < 1e-6f) return false;
            l0 /= sum; l1 /= sum; l2 /= sum;
            return true;
        }

        // Bilinear sample of straight-alpha ARGB pixels (blended in premultiplied form); (0,0) is the centre of the first pixel.
        private static int Sample(int[] px, int stride, int w, int h, float x, float y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            double a = 0, r = 0, g = 0, b = 0;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int xx = ix + i, yy = iy + j;
                    double wgt = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h || wgt <= 0) continue;
                    int v = px[yy * stride + xx];
                    double pa = (v >> 24 & 255) / 255.0;
                    a += wgt * pa;
                    r += wgt * pa * (v >> 16 & 255); g += wgt * pa * (v >> 8 & 255); b += wgt * pa * (v & 255);
                }
            if (a <= 1e-6) return 0;
            int ia = (int)Math.Round(a * 255);
            return (ia << 24) | ((int)Math.Round(r / a) << 16) | ((int)Math.Round(g / a) << 8) | (int)Math.Round(b / a);
        }

        // Source-over of straight-alpha ARGB pixels.
        private static int Over(int dst, int src)
        {
            int sa = src >> 24 & 255, da = dst >> 24 & 255;
            if (sa == 255 || da == 0) return src;
            double s = sa / 255.0, d = da / 255.0, oa = s + d * (1 - s);
            if (oa <= 0) return 0;
            Func<int, int, int> mix = (sc, dc) => (int)Math.Round((sc * s + dc * d * (1 - s)) / oa);
            return ((int)Math.Round(oa * 255) << 24) | (mix(src >> 16 & 255, dst >> 16 & 255) << 16)
                 | (mix(src >> 8 & 255, dst >> 8 & 255) << 8) | mix(src & 255, dst & 255);
        }
    }
}
