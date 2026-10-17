using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Aplat.Controls;

namespace Aplat.Models
{
    // One thing drawn on the canvas sheet. The sheet's pixels are the result of
    // replaying these in order over the pre-journal base (InkJournal), so any one
    // of them can be taken away — the object eraser — and the layer rebuilt exactly,
    // including whatever it was covering.
    //
    // Everything is in paint-layer pixels. An op knows how to draw itself, how to
    // answer "is your ink near this point" (for hit-testing) and how to write itself
    // to the .aplat (InkJournal.Serialize).
    public abstract class InkOp : IDisposable
    {
        public int Id;
        public Rectangle Bounds;   // covers every pixel the op can change

        // False for ops that only remove paint (pixel eraser, cut-out hole): they
        // always replay, and the object eraser never picks them.
        public abstract bool IsObject { get; }
        public abstract void Draw(Graphics g, Rectangle clip);
        public abstract InkOp Clone();

        // Ink within `tol` of p (layer px)? `q` is a point on that ink, where the
        // layer's visible alpha can be checked.
        public virtual bool NearestInk(PointF p, float tol, out PointF q) { q = p; return false; }

        // Whether a hit must land on visible pixels. Text is hit by its lines, with
        // the gaps between letters counting, so there the pixel under the tip is no proof.
        public virtual bool ChecksVisibility { get { return true; } }

        // Rough memory / replay cost, for keeping the journal bounded.
        public virtual long Weight { get { return 1; } }

        // Points that stand for the op's ink (layer px) — what the lasso asks "is this inside
        // my loop" of. Few, spread along the whole thing.
        public virtual IEnumerable<PointF> SamplePoints()
        {
            yield return new PointF(Bounds.Left + Bounds.Width / 2f, Bounds.Top + Bounds.Height / 2f);
        }

        // The tight box of the ink itself (layer px), without the safety margins of Bounds.
        public virtual RectangleF InkBounds { get { return Bounds; } }

        // The widest pen / outline in the op, layer px: the margin a frame drawn around it needs.
        public virtual float InkWidth { get { return 0f; } }

        // Recolours the op (the selection's colour dot). False when it has no single colour.
        public virtual bool Recolor(int argb) { return false; }

        // An op never changes once it is in the journal; one that is edited before it gets there
        // (a copy being recoloured) must drop the text it had made of itself.
        protected void Changed() { _record64 = null; }

        internal abstract byte Tag { get; }
        internal abstract void WritePayload(BinaryWriter w);
        public virtual void Dispose() { }

        // The op as one self-contained record (kind, id, footprint, payload) and its base64
        // text. An op never changes once it is in the journal, so the text is made once and
        // every saved state that contains the op reuses it — a saved state costs only the ops
        // it has that the last one did not.
        private string _record64;
        public string Record64
        {
            get
            {
                if (_record64 == null)
                    using (var ms = new MemoryStream())
                    {
                        using (var w = new BinaryWriter(ms)) WriteRecord(w, this);
                        _record64 = Convert.ToBase64String(ms.ToArray());
                    }
                return _record64;
            }
        }

        internal static InkOp FromRecord64(string text)
        {
            using (var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(text))))
            {
                InkOp op = ReadRecord(r);
                op._record64 = text;   // already in its saved form
                return op;
            }
        }

        // One op as kind, id, footprint and payload — also how a group holds its children.
        internal static void WriteRecord(BinaryWriter w, InkOp op)
        {
            w.Write(op.Tag);
            w.Write(op.Id);
            w.Write(op.Bounds.X); w.Write(op.Bounds.Y); w.Write(op.Bounds.Width); w.Write(op.Bounds.Height);
            op.WritePayload(w);
        }

        internal static InkOp ReadRecord(BinaryReader r)
        {
            byte tag = r.ReadByte();
            int id = r.ReadInt32();
            var b = new Rectangle(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            InkOp op = Read(r, tag);
            op.Id = id; op.Bounds = b;
            return op;
        }

        internal static InkOp Read(BinaryReader r, byte tag)
        {
            switch (tag)
            {
                case StrokeOp.TagId: return StrokeOp.ReadPayload(r);
                case ItemOp.TagId:   return ItemOp.ReadPayload(r);
                case FillOp.TagId:   return FillOp.ReadPayload(r);
                case ClearOp.TagId:  return ClearOp.ReadPayload(r);
                case GroupOp.TagId:  return GroupOp.ReadPayload(r);
                default: throw new InvalidDataException("unknown ink op " + tag);
            }
        }

        protected static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // Closest point to p on the segment a-b.
        public static PointF ClosestOnSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return new PointF(a.X + t * dx, a.Y + t * dy);
        }
    }

    public enum StrokeStyle { Brush, Pencil, Pen, Marker, Erase }

    // A freehand stroke of the brush / pencil / pen / marker — or of the pixel
    // eraser, which is the same path drawn with transparent paint.
    public sealed class StrokeOp : InkOp
    {
        internal const byte TagId = 1;

        public StrokeStyle Style;
        public int      Radius;    // layer px (brush size × sheet scale)
        public float    Scale;     // layer px per 1024-sheet px: pencil grain density
        public int      Argb;      // colour (alpha unused)
        public int      Opacity;   // 0..100
        public int      Seed;      // pencil grain
        public PointF[] Pts;

        private PointF[] _poly;    // flattened centre line (hit-testing only)

        public override bool IsObject { get { return Style != StrokeStyle.Erase; } }
        internal override byte Tag { get { return TagId; } }
        public override long Weight { get { return 4 + (Pts != null ? Pts.Length / 8 : 0); } }

        // Everything the stroke can touch: the points, the pen's half-width and a
        // margin for the spline bulging past its control points.
        public static Rectangle BoundsFor(IList<PointF> pts, int radius, Size layer)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, seg = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                PointF p = pts[i];
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                if (i > 0) seg = Math.Max(seg, Dist(p, pts[i - 1]));
            }
            if (pts.Count == 0) return Rectangle.Empty;
            float pad = Math.Max(1, radius) * 1.25f + 6f + seg * 0.3f;
            var r = Rectangle.FromLTRB((int)Math.Floor(x0 - pad), (int)Math.Floor(y0 - pad),
                                       (int)Math.Ceiling(x1 + pad), (int)Math.Ceiling(y1 + pad));
            r.Intersect(new Rectangle(Point.Empty, layer));
            return r;
        }

        public override void Draw(Graphics g, Rectangle clip)
        {
            DrawStroke(g, Style, Pts, Radius, Scale, Color.FromArgb(Argb), Opacity, Seed, clip);
        }

        // The one place a stroke is rasterised — the live stroke and the replay both
        // come through here, so they cannot drift apart. `area` is the region being
        // redrawn (the grain skips segments outside it).
        public static void DrawStroke(Graphics g, StrokeStyle style, IList<PointF> pts, int radius, float scale,
                                      Color c, int opacity, int seed, Rectangle area)
        {
            if (pts.Count == 0) return;
            if (radius < 1) radius = 1;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = StrokePath(pts))
            {
                if (style == StrokeStyle.Erase)
                {
                    g.CompositingMode = CompositingMode.SourceCopy;   // overwrite alpha, don't blend
                    StrokeOrDot(g, path, pts, radius * 2f, Color.FromArgb(0, 0, 0, 0), false);
                    g.CompositingMode = CompositingMode.SourceOver;
                    return;
                }
                g.CompositingMode = CompositingMode.SourceOver;
                int op = opacity;
                switch (style)
                {
                    case StrokeStyle.Pencil:
                        StrokeOrDot(g, path, pts, Math.Max(1.2f, radius * 1.0f), Color.FromArgb(150 * op / 100, c), false);
                        PencilGrain(g, path, pts, radius, scale, c, op, seed, area);
                        break;
                    case StrokeStyle.Pen:
                        // Precise drafting pen: thin, crisp, always full density.
                        StrokeOrDot(g, path, pts, Math.Max(1f, radius), Color.FromArgb(255, c), false);
                        break;
                    case StrokeStyle.Marker:
                        // Highlighter: one wide flat band — uniform translucency end to end.
                        StrokeOrDot(g, path, pts, radius * 2f, Color.FromArgb(Math.Max(20, 96 * op / 100), c), true);
                        break;
                    default:   // Brush: soft halo + dense core, each drawn once
                        StrokeOrDot(g, path, pts, radius * 2f,    Color.FromArgb(90 * op / 100, c), false);
                        StrokeOrDot(g, path, pts, radius * 1.35f, Color.FromArgb(235 * op / 100, c), false);
                        break;
                }
            }
        }

        // The stroke as one path: a cardinal spline through the samples (tension 0.5,
        // i.e. Catmull-Rom), so the polyline's corners become smooth curves.
        private static GraphicsPath StrokePath(IList<PointF> pts)
        {
            var path = new GraphicsPath();
            if (pts.Count == 1) return path;                     // a dot, see StrokeOrDot
            var arr = new PointF[pts.Count];
            pts.CopyTo(arr, 0);
            if (arr.Length == 2) path.AddLine(arr[0], arr[1]);
            else                 path.AddCurve(arr, 0.5f);
            return path;
        }

        private static void StrokeOrDot(Graphics g, GraphicsPath path, IList<PointF> pts, float width, Color c, bool flat)
        {
            if (pts.Count == 1)
            {
                PointF p = pts[0];
                using (var br = new SolidBrush(c))
                {
                    if (flat) g.FillRectangle(br, p.X - width / 2f, p.Y - width / 2f, width, width);
                    else      g.FillEllipse(br, p.X - width / 2f, p.Y - width / 2f, width, width);
                }
                return;
            }
            using (var pen = new Pen(c, width))
            {
                pen.StartCap = flat ? LineCap.Flat : LineCap.Round;
                pen.EndCap   = flat ? LineCap.Flat : LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        // Graphite grain scattered along the *curve* (the flattened spline, not the
        // straight chords between mouse samples — those cut corners on bends).
        // Seeded per stroke and per flattened segment: the spline only changes near
        // its end, so re-rendering gives the same grain everywhere else (no flicker).
        private static void PencilGrain(Graphics g, GraphicsPath path, IList<PointF> samples, int radius,
                                        float scale, Color color, int opacity, int seed, Rectangle area)
        {
            PointF[] pts;
            if (samples.Count < 2)
            {
                pts = new PointF[samples.Count];
                samples.CopyTo(pts, 0);
            }
            else
                using (var flat = (GraphicsPath)path.Clone())
                {
                    flat.Flatten(null, 0.25f);
                    pts = flat.PathPoints;
                }
            if (pts.Length == 0) return;

            for (int s = 0; s < pts.Length; s++)
            {
                PointF a = pts[s], b = s + 1 < pts.Length ? pts[s + 1] : pts[s];
                // Only segments reaching the redrawn area (keeps long strokes cheap).
                var segBox = RectangleF.FromLTRB(Math.Min(a.X, b.X) - radius, Math.Min(a.Y, b.Y) - radius,
                                                 Math.Max(a.X, b.X) + radius, Math.Max(a.Y, b.Y) + radius);
                if (segBox.IntersectsWith(area))
                {
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    // ~one grain cluster per screen pixel of stroke length.
                    int steps = Math.Max(1, (int)(Math.Sqrt(dx * dx + dy * dy) / scale));
                    var rng = new Random(seed ^ (s * 7919));
                    for (int i = 0; i < steps; i++)
                    {
                        float t = (float)i / steps;
                        float cx = a.X + dx * t, cy = a.Y + dy * t;
                        int dots = 2 + rng.Next(3);
                        for (int k = 0; k < dots; k++)
                        {
                            double ang = rng.NextDouble() * Math.PI * 2;
                            double r   = rng.NextDouble() * radius;
                            float gx = cx + (float)(Math.Cos(ang) * r);
                            float gy = cy + (float)(Math.Sin(ang) * r);
                            int al = (40 + rng.Next(120)) * opacity / 100;
                            float sz = (0.8f + (float)rng.NextDouble() * 1.1f) * scale;
                            using (var dotBr = new SolidBrush(Color.FromArgb(al, color)))
                                g.FillEllipse(dotBr, gx - sz / 2f, gy - sz / 2f, sz, sz);
                        }
                    }
                }
                if (s + 1 >= pts.Length) break;
            }
        }

        // Half the visible width of the stroke, for hit-testing.
        private float HalfWidth
        {
            get
            {
                switch (Style)
                {
                    case StrokeStyle.Pen:    return Math.Max(1f, Radius / 2f);
                    case StrokeStyle.Pencil: return Radius * 0.8f;   // the grain spreads out to the radius
                    default:                 return Radius;
                }
            }
        }

        public override bool NearestInk(PointF p, float tol, out PointF q)
        {
            q = p;
            if (Pts == null || Pts.Length == 0) return false;
            if (_poly == null)
            {
                if (Pts.Length == 1) _poly = new[] { Pts[0] };
                else
                    using (var path = StrokePath(Pts))
                    {
                        path.Flatten(null, 0.5f);
                        _poly = path.PathPoints;
                    }
            }
            float reach = HalfWidth + tol;
            if (_poly.Length == 1)
            {
                if (Dist(p, _poly[0]) > reach) return false;
                q = _poly[0];
                return true;
            }
            float best = float.MaxValue;
            for (int i = 0; i + 1 < _poly.Length; i++)
            {
                PointF c = ClosestOnSegment(p, _poly[i], _poly[i + 1]);
                float d = Dist(p, c);
                if (d < best) { best = d; q = c; }
            }
            return best <= reach;
        }

        public override IEnumerable<PointF> SamplePoints()
        {
            if (Pts == null || Pts.Length == 0) yield break;
            int step = Math.Max(1, Pts.Length / 48);
            for (int i = 0; i < Pts.Length; i += step) yield return Pts[i];
            yield return Pts[Pts.Length - 1];
        }

        public override RectangleF InkBounds
        {
            get
            {
                if (Pts == null || Pts.Length == 0) return Bounds;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
                float h = HalfWidth;
                return RectangleF.FromLTRB(x0 - h, y0 - h, x1 + h, y1 + h);
            }
        }

        public override float InkWidth { get { return HalfWidth * 2f; } }

        public override bool Recolor(int argb)
        {
            Argb = argb & 0xFFFFFF | unchecked((int)0xFF000000);
            Changed();
            return true;
        }

        public override InkOp Clone()
        {
            return new StrokeOp
            {
                Id = Id, Bounds = Bounds, Style = Style, Radius = Radius, Scale = Scale, Argb = Argb,
                Opacity = Opacity, Seed = Seed, Pts = (PointF[])Pts.Clone(),
            };
        }

        internal override void WritePayload(BinaryWriter w)
        {
            w.Write((byte)Style); w.Write(Radius); w.Write(Scale); w.Write(Argb); w.Write(Opacity); w.Write(Seed);
            w.Write(Pts.Length);
            foreach (var p in Pts) { w.Write(p.X); w.Write(p.Y); }
        }

        internal static StrokeOp ReadPayload(BinaryReader r)
        {
            var s = new StrokeOp
            {
                Style = (StrokeStyle)r.ReadByte(), Radius = r.ReadInt32(), Scale = r.ReadSingle(),
                Argb = r.ReadInt32(), Opacity = r.ReadInt32(), Seed = r.ReadInt32(),
            };
            int n = r.ReadInt32();
            if (n < 0 || n > 4000000) throw new InvalidDataException("stroke");
            s.Pts = new PointF[n];
            for (int i = 0; i < n; i++) s.Pts[i] = new PointF(r.ReadSingle(), r.ReadSingle());
            return s;
        }
    }

    // A committed live element: a shape, a line, text or a pasted cut-out. It is
    // kept as the LiveItem itself, so it replays as sharp vectors (text and shapes
    // stay crisp) and could be edited again.
    public sealed class ItemOp : InkOp
    {
        internal const byte TagId = 2;

        public LiveItem Item;   // owned
        public float    K = 1f; // sheet y = layer y × K (see SceneView.Live)

        public override bool IsObject { get { return true; } }
        public override bool ChecksVisibility { get { return Item == null || Item.Kind != LiveKind.Text; } }
        internal override byte Tag { get { return TagId; } }
        public override long Weight
        {
            get { return 4 + (Item != null && Item.Image != null ? (long)Item.Image.Width * Item.Image.Height / 256 : 0); }
        }

        public override void Draw(Graphics g, Rectangle clip)
        {
            var state = g.Save();           // a group draws its children through its own transform
            g.ScaleTransform(1f, 1f / K);   // sheet px → layer px
            Item.Render(g);
            g.Restore(state);
        }

        public override IEnumerable<PointF> SamplePoints()
        {
            PointF c = Item.Center;
            yield return new PointF(c.X, c.Y / K);
            foreach (var p in Item.Corners()) yield return new PointF(p.X, p.Y / K);
            if (Item.IsLine) { yield return new PointF(Item.P1.X, Item.P1.Y / K); yield return new PointF(Item.P2.X, Item.P2.Y / K); }
        }

        public override RectangleF InkBounds
        {
            get
            {
                PointF[] q = Item.IsLine ? new[] { Item.P1, Item.P2 } : Item.Corners();
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in q) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
                float h = Item.IsShape ? Item.StrokeWidth / 2f : 0f;
                return RectangleF.FromLTRB(x0 - h, (y0 - h) / K, x1 + h, (y1 + h) / K);
            }
        }

        public override float InkWidth { get { return Item.IsShape ? Item.StrokeWidth : 0f; } }

        public override bool Recolor(int argb)
        {
            if (Item.Kind == LiveKind.Image) return false;
            Item.Color = Color.FromArgb(Item.Color.A, Color.FromArgb(argb));
            Changed();
            return true;
        }

        public override bool NearestInk(PointF p, float tol, out PointF q)
        {
            PointF qs;
            bool hit = Item.NearestInk(new PointF(p.X, p.Y * K), tol, out qs);
            q = new PointF(qs.X, qs.Y / K);
            return hit;
        }

        public override InkOp Clone()
        {
            return new ItemOp { Id = Id, Bounds = Bounds, Item = Item.Clone(), K = K };
        }

        public override void Dispose()
        {
            if (Item != null) { Item.Dispose(); Item = null; }
        }

        internal override void WritePayload(BinaryWriter w)
        {
            w.Write(K);
            w.Write((byte)Item.Kind);
            w.Write(Item.P1.X); w.Write(Item.P1.Y); w.Write(Item.P2.X); w.Write(Item.P2.Y);
            w.Write(Item.Angle);
            w.Write(Item.Color.ToArgb());
            w.Write(Item.StrokeWidth);
            w.Write((byte)Item.Fill);
            w.Write(Item.Text ?? "");
            w.Write(Item.FontFamily ?? "");
            w.Write((int)Item.FontStyle);
            w.Write(Item.FontPx);
            w.Write((byte)Item.Align);
            if (Item.Image != null)
            {
                byte[] png = InkJournal.EncodePng(Item.Image);
                w.Write(png.Length);
                w.Write(png);
            }
            else w.Write(0);
        }

        internal static ItemOp ReadPayload(BinaryReader r)
        {
            var op = new ItemOp { K = r.ReadSingle() };
            var it = new LiveItem { Kind = (LiveKind)r.ReadByte() };
            it.P1 = new PointF(r.ReadSingle(), r.ReadSingle());
            it.P2 = new PointF(r.ReadSingle(), r.ReadSingle());
            it.Angle       = r.ReadSingle();
            it.Color       = Color.FromArgb(r.ReadInt32());
            it.StrokeWidth = r.ReadSingle();
            it.Fill        = (ShapeFill)r.ReadByte();
            it.Text        = r.ReadString();
            it.FontFamily  = r.ReadString();
            it.FontStyle   = (FontStyle)r.ReadInt32();
            it.FontPx      = r.ReadSingle();
            it.Align       = (StringAlignment)r.ReadByte();
            int n = r.ReadInt32();
            if (n < 0 || n > 200000000) throw new InvalidDataException("image");
            if (n > 0) it.Image = InkJournal.DecodePng(r.ReadBytes(n));
            op.Item = it;
            return op;
        }
    }

    // A paint-bucket fill: the pixels it recoloured, as horizontal runs. Undoing it
    // (object-erasing it) brings back whatever was there before.
    public sealed class FillOp : InkOp
    {
        internal const byte TagId = 3;

        public int   Argb;
        public int[] Y, X0, X1;     // runs sorted by y then x; X1 exclusive

        public override bool IsObject { get { return true; } }
        internal override byte Tag { get { return TagId; } }
        public override long Weight { get { return 4 + Y.Length / 64; } }

        // `mask` is w×h, non-zero where the fill changed a pixel.
        public static FillOp FromMask(byte[] mask, int w, int h, Rectangle box, int argb)
        {
            var ys = new List<int>(); var xs0 = new List<int>(); var xs1 = new List<int>();
            for (int y = box.Top; y < box.Bottom; y++)
            {
                int row = y * w, x = box.Left;
                while (x < box.Right)
                {
                    if (mask[row + x] == 0) { x++; continue; }
                    int s = x;
                    while (x < box.Right && mask[row + x] != 0) x++;
                    ys.Add(y); xs0.Add(s); xs1.Add(x);
                }
            }
            return new FillOp { Argb = argb, Y = ys.ToArray(), X0 = xs0.ToArray(), X1 = xs1.ToArray(), Bounds = box };
        }

        // First run whose row is >= y.
        private int FirstAtOrAfter(int y)
        {
            int lo = 0, hi = Y.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (Y[mid] < y) lo = mid + 1; else hi = mid; }
            return lo;
        }

        public override void Draw(Graphics g, Rectangle clip)
        {
            g.SmoothingMode   = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;   // a fill replaces, it doesn't blend
            using (var br = new SolidBrush(Color.FromArgb(Argb)))
                for (int i = FirstAtOrAfter(clip.Top); i < Y.Length && Y[i] < clip.Bottom; i++)
                {
                    int x0 = Math.Max(X0[i], clip.Left), x1 = Math.Min(X1[i], clip.Right);
                    if (x1 > x0) g.FillRectangle(br, x0, Y[i], x1 - x0, 1);
                }
            g.CompositingMode = CompositingMode.SourceOver;
        }

        public override bool NearestInk(PointF p, float tol, out PointF q)
        {
            q = p;
            int r = (int)Math.Ceiling(tol);
            int py = (int)Math.Floor(p.Y), px = (int)Math.Floor(p.X);
            float best = float.MaxValue;
            for (int i = FirstAtOrAfter(py - r); i < Y.Length && Y[i] <= py + r; i++)
            {
                float cx = Math.Max(X0[i], Math.Min(X1[i] - 1, p.X)), cy = Y[i] + 0.5f;
                float d = Dist(p, new PointF(cx, cy));
                if (d < best) { best = d; q = new PointF(cx, cy); }
            }
            return best <= tol + 1f;
        }

        public override InkOp Clone()
        {
            return new FillOp { Id = Id, Bounds = Bounds, Argb = Argb,
                                Y = (int[])Y.Clone(), X0 = (int[])X0.Clone(), X1 = (int[])X1.Clone() };
        }

        internal override void WritePayload(BinaryWriter w)
        {
            w.Write(Argb);
            w.Write(Y.Length);
            for (int i = 0; i < Y.Length; i++) { w.Write(Y[i]); w.Write(X0[i]); w.Write(X1[i]); }
        }

        internal static FillOp ReadPayload(BinaryReader r)
        {
            var f = new FillOp { Argb = r.ReadInt32() };
            int n = r.ReadInt32();
            if (n < 0 || n > 20000000) throw new InvalidDataException("fill");
            f.Y = new int[n]; f.X0 = new int[n]; f.X1 = new int[n];
            for (int i = 0; i < n; i++) { f.Y[i] = r.ReadInt32(); f.X0[i] = r.ReadInt32(); f.X1[i] = r.ReadInt32(); }
            return f;
        }
    }

    // The hole a cut-out leaves behind: the rectangle goes transparent.
    public sealed class ClearOp : InkOp
    {
        internal const byte TagId = 4;

        public override bool IsObject { get { return false; } }
        internal override byte Tag { get { return TagId; } }

        public override void Draw(Graphics g, Rectangle clip)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var br = new SolidBrush(Color.Transparent)) g.FillRectangle(br, Bounds);
            g.CompositingMode = CompositingMode.SourceOver;
        }

        public override InkOp Clone() { return new ClearOp { Id = Id, Bounds = Bounds }; }
        internal override void WritePayload(BinaryWriter w) { }
        internal static ClearOp ReadPayload(BinaryReader r) { return new ClearOp(); }
    }

    // A lasso selection that was moved, scaled or turned: its ops, kept exactly as they were
    // drawn, and the transform (child space → layer px) they are drawn through. Nothing is
    // re-fitted, so the strokes stay the strokes — still erasable as one object, still sharp
    // (text and shapes are vectors), and a group can be lassoed and moved again.
    public sealed class GroupOp : InkOp
    {
        internal const byte TagId = 5;

        public float[] M;                       // a, b, c, d, e, f of System.Drawing's Matrix.Elements
        public List<InkOp> Children = new List<InkOp>();   // owned

        public override bool IsObject { get { return true; } }
        internal override byte Tag { get { return TagId; } }
        public override long Weight
        {
            get { long w = 4; foreach (var c in Children) w += c.Weight; return w; }
        }

        public Matrix Transform { get { return new Matrix(M[0], M[1], M[2], M[3], M[4], M[5]); } }

        // `ink` boxes of the children carried through the transform, padded by the pen.
        public static GroupOp Create(List<InkOp> children, float[] m, Size layer)
        {
            var g = new GroupOp { M = m, Children = children };
            g.Bounds = g.ComputeBounds(layer);
            return g;
        }

        private Rectangle ComputeBounds(Size layer)
        {
            var q = new List<PointF>();
            float pad = 0;
            foreach (var c in Children)
            {
                Rectangle b = c.Bounds;
                q.Add(new PointF(b.Left, b.Top)); q.Add(new PointF(b.Right, b.Top));
                q.Add(new PointF(b.Right, b.Bottom)); q.Add(new PointF(b.Left, b.Bottom));
            }
            PointF[] pts = q.ToArray();
            if (pts.Length == 0) return Rectangle.Empty;
            using (var m = Transform) m.TransformPoints(pts);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            pad = 3f;
            var r = Rectangle.FromLTRB((int)Math.Floor(x0 - pad), (int)Math.Floor(y0 - pad),
                                       (int)Math.Ceiling(x1 + pad), (int)Math.Ceiling(y1 + pad));
            r.Intersect(new Rectangle(Point.Empty, layer));
            return r;
        }

        public override void Draw(Graphics g, Rectangle clip)
        {
            var state = g.Save();
            using (var m = Transform) g.MultiplyTransform(m);
            foreach (var c in Children)
            {
                var inner = g.Save();
                c.Draw(g, c.Bounds);
                g.Restore(inner);
            }
            g.Restore(state);
        }

        private bool Inverse(out Matrix inv, out float scale)
        {
            inv = Transform;
            double det = (double)M[0] * M[3] - (double)M[1] * M[2];
            scale = (float)Math.Sqrt(Math.Abs(det));
            if (!inv.IsInvertible || scale < 1e-6f) { inv.Dispose(); inv = null; return false; }
            inv.Invert();
            return true;
        }

        public override bool NearestInk(PointF p, float tol, out PointF q)
        {
            q = p;
            Matrix inv; float s;
            if (!Inverse(out inv, out s)) return false;
            var pc = new[] { p };
            inv.TransformPoints(pc);
            inv.Dispose();
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                PointF qc;
                if (!Children[i].NearestInk(pc[0], tol / s, out qc)) continue;
                var qq = new[] { qc };
                using (var m = Transform) m.TransformPoints(qq);
                q = qq[0];
                return true;
            }
            return false;
        }

        public override IEnumerable<PointF> SamplePoints()
        {
            var pts = new List<PointF>();
            foreach (var c in Children) pts.AddRange(c.SamplePoints());
            PointF[] arr = pts.ToArray();
            using (var m = Transform) m.TransformPoints(arr);
            return arr;
        }

        public override RectangleF InkBounds
        {
            get
            {
                var q = new List<PointF>();
                foreach (var c in Children)
                {
                    RectangleF b = c.InkBounds;
                    q.Add(new PointF(b.Left, b.Top)); q.Add(new PointF(b.Right, b.Top));
                    q.Add(new PointF(b.Right, b.Bottom)); q.Add(new PointF(b.Left, b.Bottom));
                }
                PointF[] pts = q.ToArray();
                if (pts.Length == 0) return Bounds;
                using (var m = Transform) m.TransformPoints(pts);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
                return RectangleF.FromLTRB(x0, y0, x1, y1);
            }
        }

        public override float InkWidth
        {
            get
            {
                float w = 0f;
                foreach (var c in Children) w = Math.Max(w, c.InkWidth);
                return w * (float)Math.Sqrt(Math.Abs((double)M[0] * M[3] - (double)M[1] * M[2]));
            }
        }

        public override bool Recolor(int argb)
        {
            bool any = false;
            foreach (var c in Children) any |= c.Recolor(argb);
            if (any) Changed();
            return any;
        }

        public override InkOp Clone()
        {
            var g = new GroupOp { Id = Id, Bounds = Bounds, M = (float[])M.Clone() };
            foreach (var c in Children) g.Children.Add(c.Clone());
            return g;
        }

        public override void Dispose()
        {
            foreach (var c in Children) c.Dispose();
            Children.Clear();
        }

        internal override void WritePayload(BinaryWriter w)
        {
            foreach (float f in M) w.Write(f);
            w.Write(Children.Count);
            foreach (var c in Children) WriteRecord(w, c);
        }

        internal static GroupOp ReadPayload(BinaryReader r)
        {
            var g = new GroupOp { M = new float[6] };
            for (int i = 0; i < 6; i++) g.M[i] = r.ReadSingle();
            int n = r.ReadInt32();
            if (n < 0 || n > 100000) throw new InvalidDataException("group");
            for (int i = 0; i < n; i++) g.Children.Add(ReadRecord(r));
            return g;
        }
    }
}
