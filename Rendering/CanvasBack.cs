using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Aplat.Rendering
{
    // The reverse of the drawing sheet: a stretched canvas seen from behind — raw
    // linen inside a pine stretcher frame (with a crossbar on a long sheet), the
    // primed fabric wrapped round the edges and stapled. It says at a glance "this is
    // the back, you cannot draw here" without a word; the front stays blank paper.
    //
    // Drawn in code (no image file to ship or license) at the sheet's own aspect
    // ratio, so the frame keeps its proportions whatever size the sheet is set to.
    // The same picture in both themes: it is an object, not interface.
    public static class CanvasBack
    {
        // Longest side of the generated picture, px.
        private const int LongSide = 1024;

        // Quantised aspect (width / height × 50): the key a renderer caches the
        // texture by, so a slider drag does not regenerate it every frame.
        public static int AspectKey(float aspect)
        {
            return Math.Max(10, Math.Min(400, (int)Math.Round(aspect * 50f)));
        }

        // How deep the frame stands behind the sheet, as a fraction of the sheet's short side.
        public const float Depth = 0.045f;

        // Where everything sits, in fractions of the sheet (u across, v down) — the
        // picture is drawn from these, and the renderer builds the frame's real
        // geometry from the same numbers, so painted and solid parts line up exactly.
        public struct Layout
        {
            public int Width, Height;                   // generated picture, px
            public float WrapU, WrapV;                  // fabric margin
            public float InnerU0, InnerV0, InnerU1, InnerV1;   // opening inside the four bars
            public bool HasCross, CrossVertical;
            public float CrossU0, CrossV0, CrossU1, CrossV1;   // the crossbar
        }

        private static void Metrics(int aspectKey, out float aspect, out int w, out int h,
                                    out RectangleF outer, out RectangleF inner, out RectangleF cross,
                                    out bool vertical, out bool horizontal, out float wrap, out float barW)
        {
            aspect = aspectKey / 50f;
            w = aspect >= 1f ? LongSide : Math.Max(64, (int)Math.Round(LongSide * aspect));
            h = aspect >= 1f ? Math.Max(64, (int)Math.Round(LongSide / aspect)) : LongSide;
            float m = Math.Min(w, h);
            wrap = m * 0.055f;                           // fabric folded round the frame
            barW = m * 0.052f;                           // stretcher bar
            float crossW = m * 0.064f;                   // crossbar
            outer = new RectangleF(wrap, wrap, w - 2 * wrap, h - 2 * wrap);
            inner = new RectangleF(outer.X + barW, outer.Y + barW, outer.Width - 2 * barW, outer.Height - 2 * barW);

            // A long sheet gets a crossbar across its short way, like the real thing.
            vertical = aspect >= 1.25f; horizontal = aspect <= 0.8f;
            cross = RectangleF.Empty;
            if (vertical)   cross = new RectangleF(inner.X + (inner.Width - crossW) / 2f, inner.Y, crossW, inner.Height);
            if (horizontal) cross = new RectangleF(inner.X, inner.Y + (inner.Height - crossW) / 2f, inner.Width, crossW);
        }

        public static Layout LayoutFor(int aspectKey)
        {
            float aspect, wrap, barW; int w, h; bool v, hz;
            RectangleF outer, inner, cross;
            Metrics(aspectKey, out aspect, out w, out h, out outer, out inner, out cross, out v, out hz, out wrap, out barW);
            return new Layout
            {
                Width = w, Height = h,
                WrapU = wrap / w, WrapV = wrap / h,
                InnerU0 = inner.Left / w, InnerV0 = inner.Top / h, InnerU1 = inner.Right / w, InnerV1 = inner.Bottom / h,
                HasCross = !cross.IsEmpty, CrossVertical = v,
                CrossU0 = cross.Left / w, CrossV0 = cross.Top / h, CrossU1 = cross.Right / w, CrossV1 = cross.Bottom / h,
            };
        }

        public static Bitmap Create(int aspectKey)
        {
            float aspect, wrap, barW; int w, h; bool vertical, horizontal;
            RectangleF outer, inner, cross;
            Metrics(aspectKey, out aspect, out w, out h, out outer, out inner, out cross, out vertical, out horizontal, out wrap, out barW);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            float m = Math.Min(w, h);

            Linen(bmp, outer);

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Openings: the raw canvas between the bars, shaded where it dips under them.
                foreach (var hole in Openings(inner, cross, vertical, horizontal))
                    InnerShadow(g, hole, m);

                var rng = new Random(7);
                Frame(g, outer, inner, m, rng);
                if (!cross.IsEmpty) Bar(g, cross, vertical, m, rng, true);

                Folds(g, w, h, outer, m);
                Staples(g, w, h, wrap, m);
            }
            return bmp;
        }

        // ── The fabric ────────────────────────────────────────────────
        // Plain weave: crossing threads of slightly uneven thickness. Primed fabric
        // (the wrapped margin) is lighter and cooler than the raw linen inside.
        private static void Linen(Bitmap bmp, RectangleF frameOuter)
        {
            int w = bmp.Width, h = bmp.Height;
            var rng = new Random(11);
            var sx = new float[w]; var sy = new float[h];     // weave phase per column / row
            var tx = new float[w]; var ty = new float[h];     // thread thickness noise
            const float period = 3.4f;
            for (int x = 0; x < w; x++) { sx[x] = (float)Math.Sin(x * 2 * Math.PI / period); tx[x] = (float)(rng.NextDouble() - 0.5); }
            for (int y = 0; y < h; y++) { sy[y] = (float)Math.Sin(y * 2 * Math.PI / period); ty[y] = (float)(rng.NextDouble() - 0.5); }
            // Slow cloudiness, so the cloth is not one flat tone.
            var cloud = new float[16 * 16];
            for (int i = 0; i < cloud.Length; i++) cloud[i] = (float)(rng.NextDouble() - 0.5);

            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    float fy = y * 15f / h; int cy = (int)fy; float ay = fy - cy;
                    for (int x = 0; x < w; x++)
                    {
                        float fx = x * 15f / w; int cx = (int)fx; float ax = fx - cx;
                        float c0 = cloud[cy * 16 + cx] * (1 - ax) + cloud[cy * 16 + cx + 1] * ax;
                        float c1 = cloud[(cy + 1) * 16 + cx] * (1 - ax) + cloud[(cy + 1) * 16 + cx + 1] * ax;
                        float cl = c0 * (1 - ay) + c1 * ay;

                        bool primed = x < frameOuter.Left || x >= frameOuter.Right || y < frameOuter.Top || y >= frameOuter.Bottom;
                        float r = primed ? 231 : 213, gch = primed ? 228 : 202, b = primed ? 220 : 183;
                        float k = 1f + 0.05f * sx[x] * sy[y] + 0.035f * (tx[x] + ty[y]) + 0.04f * cl;
                        row[x] = Pack(r * k, gch * k, b * k);
                    }
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), w);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        private static int Pack(float r, float g, float b)
        {
            return (255 << 24) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
        }

        private static int Clamp(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

        // ── Wood ──────────────────────────────────────────────────────
        private static readonly Color Pine = Color.FromArgb(226, 186, 128);

        private static RectangleF[] Openings(RectangleF inner, RectangleF cross, bool vertical, bool horizontal)
        {
            if (vertical)
                return new[]
                {
                    RectangleF.FromLTRB(inner.Left, inner.Top, cross.Left, inner.Bottom),
                    RectangleF.FromLTRB(cross.Right, inner.Top, inner.Right, inner.Bottom),
                };
            if (horizontal)
                return new[]
                {
                    RectangleF.FromLTRB(inner.Left, inner.Top, inner.Right, cross.Top),
                    RectangleF.FromLTRB(inner.Left, cross.Bottom, inner.Right, inner.Bottom),
                };
            return new[] { inner };
        }

        // The cloth dips under the bars: a soft shadow along each edge, stronger on
        // the top and left where the light comes from.
        private static void InnerShadow(Graphics g, RectangleF r, float m)
        {
            float big = m * 0.045f, small = m * 0.028f;
            Strip(g, new RectangleF(r.Left, r.Top, r.Width, big), 110, true, true);              // under the top bar
            Strip(g, new RectangleF(r.Left, r.Top, big, r.Height), 110, false, true);            // under the left bar
            Strip(g, new RectangleF(r.Left, r.Bottom - small, r.Width, small), 60, true, false); // over the bottom bar
            Strip(g, new RectangleF(r.Right - small, r.Top, small, r.Height), 60, false, false); // over the right bar
        }

        // A gradient strip, darkest at the side that touches the bar.
        private static void Strip(Graphics g, RectangleF r, int alpha, bool vertical, bool barFirst)
        {
            if (r.Width < 1 || r.Height < 1) return;
            Color dark = Color.FromArgb(alpha, 52, 36, 22), clear = Color.FromArgb(0, 52, 36, 22);
            using (var br = new LinearGradientBrush(r, barFirst ? dark : clear, barFirst ? clear : dark,
                       vertical ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal))
                g.FillRectangle(br, r);
        }

        // The four bars as mitred trapezoids, with grain, a lit outer edge and a
        // shaded inner one.
        private static void Frame(Graphics g, RectangleF o, RectangleF i, float m, Random rng)
        {
            var top    = new[] { new PointF(o.Left, o.Top),    new PointF(o.Right, o.Top),    new PointF(i.Right, i.Top),    new PointF(i.Left, i.Top) };
            var bottom = new[] { new PointF(o.Left, o.Bottom), new PointF(o.Right, o.Bottom), new PointF(i.Right, i.Bottom), new PointF(i.Left, i.Bottom) };
            var left   = new[] { new PointF(o.Left, o.Top),    new PointF(o.Left, o.Bottom),  new PointF(i.Left, i.Bottom),  new PointF(i.Left, i.Top) };
            var right  = new[] { new PointF(o.Right, o.Top),   new PointF(o.Right, o.Bottom), new PointF(i.Right, i.Bottom), new PointF(i.Right, i.Top) };

            // A drop shadow of the whole frame onto the primed margin.
            float s = m * 0.012f;
            var keep = g.Save();
            g.ExcludeClip(Rectangle.Round(o));   // only on the margin, never over the cloth inside
            using (var sh = new SolidBrush(Color.FromArgb(46, 40, 30, 20)))
                g.FillRectangle(sh, o.Left + s, o.Top + s * 1.4f, o.Width, o.Height);
            g.Restore(keep);

            Plank(g, top,    o, true,  m, rng, 0);
            Plank(g, bottom, o, true,  m, rng, -6);
            Plank(g, left,   o, false, m, rng, -4);
            Plank(g, right,  o, false, m, rng, -10);

            // Mitre joints.
            using (var pen = new Pen(Color.FromArgb(150, 110, 72, 36), Math.Max(1f, m / 700f)))
            {
                g.DrawLine(pen, o.Left, o.Top, i.Left, i.Top);
                g.DrawLine(pen, o.Right, o.Top, i.Right, i.Top);
                g.DrawLine(pen, o.Left, o.Bottom, i.Left, i.Bottom);
                g.DrawLine(pen, o.Right, o.Bottom, i.Right, i.Bottom);
            }
            // Outer lit edge, inner shaded edge.
            float lw = Math.Max(1f, m / 640f);
            using (var lit = new Pen(Color.FromArgb(120, 255, 244, 214), lw))
            using (var dim = new Pen(Color.FromArgb(120, 112, 74, 36), lw))
            {
                g.DrawRectangle(lit, o.X, o.Y, o.Width, o.Height);
                g.DrawRectangle(dim, i.X, i.Y, i.Width, i.Height);
            }
        }

        private static void Plank(Graphics g, PointF[] poly, RectangleF bounds, bool horizontal, float m, Random rng, int tone)
        {
            var c = Color.FromArgb(Pine.R + tone, Pine.G + tone, Pine.B + tone);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(poly);
                using (var br = new SolidBrush(c)) g.FillPath(br, path);
                var save = g.Save();
                g.SetClip(path);
                Grain(g, path.GetBounds(), horizontal, m, rng);
                g.Restore(save);
            }
        }

        // A crossbar: a plain plank between the bars.
        private static void Bar(Graphics g, RectangleF r, bool verticalBar, float m, Random rng, bool crossbar)
        {
            // A little shadow either side onto the cloth.
            float sh = m * 0.022f;
            Color dark = Color.FromArgb(80, 52, 36, 22), clear = Color.FromArgb(0, 52, 36, 22);
            if (verticalBar)
            {
                using (var b1 = new LinearGradientBrush(new RectangleF(r.Left - sh, r.Top, sh, r.Height), clear, dark, LinearGradientMode.Horizontal))
                    g.FillRectangle(b1, r.Left - sh, r.Top, sh, r.Height);
                using (var b2 = new LinearGradientBrush(new RectangleF(r.Right, r.Top, sh, r.Height), dark, clear, LinearGradientMode.Horizontal))
                    g.FillRectangle(b2, r.Right, r.Top, sh, r.Height);
            }
            else
            {
                using (var b1 = new LinearGradientBrush(new RectangleF(r.Left, r.Top - sh, r.Width, sh), clear, dark, LinearGradientMode.Vertical))
                    g.FillRectangle(b1, r.Left, r.Top - sh, r.Width, sh);
                using (var b2 = new LinearGradientBrush(new RectangleF(r.Left, r.Bottom, r.Width, sh), dark, clear, LinearGradientMode.Vertical))
                    g.FillRectangle(b2, r.Left, r.Bottom, r.Width, sh);
            }

            using (var br = new SolidBrush(Color.FromArgb(Pine.R - 4, Pine.G - 4, Pine.B - 4))) g.FillRectangle(br, r);
            var save = g.Save();
            g.SetClip(r);
            Grain(g, r, !verticalBar, m, rng);
            g.Restore(save);
            float lw = Math.Max(1f, m / 640f);
            using (var lit = new Pen(Color.FromArgb(110, 255, 244, 214), lw))
            using (var dim = new Pen(Color.FromArgb(120, 112, 74, 36), lw))
            {
                if (verticalBar)
                {
                    g.DrawLine(lit, r.Left, r.Top, r.Left, r.Bottom);
                    g.DrawLine(dim, r.Right, r.Top, r.Right, r.Bottom);
                }
                else
                {
                    g.DrawLine(lit, r.Left, r.Top, r.Right, r.Top);
                    g.DrawLine(dim, r.Left, r.Bottom, r.Right, r.Bottom);
                }
            }
        }

        // Long fibres along the plank, a few darker, a few lighter.
        private static void Grain(Graphics g, RectangleF r, bool alongX, float m, Random rng)
        {
            int n = (int)((alongX ? r.Height : r.Width) / Math.Max(1f, m * 0.0035f));
            for (int k = 0; k < n; k++)
            {
                float t = (float)rng.NextDouble();
                bool darkLine = rng.NextDouble() < 0.7;
                var c = darkLine ? Color.FromArgb(18 + rng.Next(34), 128, 84, 40)
                                 : Color.FromArgb(20 + rng.Next(30), 255, 240, 205);
                using (var pen = new Pen(c, Math.Max(1f, m * (0.0008f + (float)rng.NextDouble() * 0.0016f))))
                {
                    // Slightly wavering, not ruler-straight.
                    float wob = m * 0.004f * ((float)rng.NextDouble() - 0.5f);
                    if (alongX)
                    {
                        float y = r.Top + t * r.Height;
                        g.DrawBezier(pen, r.Left, y, r.Left + r.Width * 0.35f, y + wob, r.Left + r.Width * 0.65f, y - wob, r.Right, y);
                    }
                    else
                    {
                        float x = r.Left + t * r.Width;
                        g.DrawBezier(pen, x, r.Top, x + wob, r.Top + r.Height * 0.35f, x - wob, r.Top + r.Height * 0.65f, x, r.Bottom);
                    }
                }
            }
        }

        // ── The wrap ──────────────────────────────────────────────────
        // The fabric is folded at each corner: a crease from the sheet's corner to the frame's.
        private static void Folds(Graphics g, int w, int h, RectangleF o, float m)
        {
            float lw = Math.Max(1f, m / 560f);
            using (var dark = new Pen(Color.FromArgb(70, 90, 80, 66), lw))
            using (var light = new Pen(Color.FromArgb(120, 255, 252, 244), lw))
            {
                var corners = new[]
                {
                    new[] { 0f, 0f, o.Left, o.Top },
                    new[] { (float)w, 0f, o.Right, o.Top },
                    new[] { 0f, (float)h, o.Left, o.Bottom },
                    new[] { (float)w, (float)h, o.Right, o.Bottom },
                };
                foreach (var c in corners)
                {
                    g.DrawLine(dark, c[0], c[1], c[2], c[3]);
                    g.DrawLine(light, c[0] + 1.2f, c[1] + 1.2f, c[2] + 1.2f, c[3] + 1.2f);
                }
            }
        }

        // Steel staples along the wrapped margin, and a crossed pair at each corner.
        private static void Staples(Graphics g, int w, int h, float wrap, float m)
        {
            float len = m * 0.04f, th = Math.Max(2f, m * 0.0065f);
            float mid = wrap / 2f;
            int along = Math.Max(3, (int)Math.Round(Math.Max(w, h) / (m * 0.2f)));
            int across = Math.Max(2, (int)Math.Round(Math.Min(w, h) / (m * 0.2f)));

            for (int k = 1; k < along; k++)
            {
                float x = w * k / (float)along;
                Staple(g, x, mid, len, th, false);
                Staple(g, x, h - mid, len, th, false);
            }
            for (int k = 1; k < across; k++)
            {
                float y = h * k / (float)across;
                Staple(g, mid, y, len, th, true);
                Staple(g, w - mid, y, len, th, true);
            }
            // Corners: two short staples crossed.
            foreach (var c in new[] { new PointF(mid, mid), new PointF(w - mid, mid), new PointF(mid, h - mid), new PointF(w - mid, h - mid) })
            {
                StapleAt(g, c.X, c.Y, len * 0.9f, th, 45f);
                StapleAt(g, c.X, c.Y, len * 0.9f, th, -45f);
            }
        }

        private static void Staple(Graphics g, float cx, float cy, float len, float th, bool vertical)
        {
            StapleAt(g, cx, cy, len, th, vertical ? 90f : 0f);
        }

        private static void StapleAt(Graphics g, float cx, float cy, float len, float th, float angle)
        {
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (var shadow = new Pen(Color.FromArgb(90, 60, 56, 50), th))
                g.DrawLine(shadow, -len / 2f, th * 0.7f, len / 2f, th * 0.7f);
            using (var steel = new Pen(Color.FromArgb(255, 150, 156, 162), th) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(steel, -len / 2f, 0, len / 2f, 0);
            using (var shine = new Pen(Color.FromArgb(200, 226, 230, 234), Math.Max(1f, th * 0.35f)))
                g.DrawLine(shine, -len / 2f, -th * 0.2f, len / 2f, -th * 0.2f);
            g.Restore(state);
        }
    }
}
