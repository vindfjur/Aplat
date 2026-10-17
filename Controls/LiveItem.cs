using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using Aplat.Models;

namespace Aplat.Controls
{
    public enum LiveKind { Line, Rect, Ellipse, Triangle, Text, Image, Group }

    // How a closed shape is painted: its outline, its interior, or a tinted
    // interior under the outline.
    public enum ShapeFill { Outline, Fill, Both }

    // An element on the canvas that is still editable. A shape,
    // a line of text or a cut-out region stays "live" after it is drawn: it has
    // handles to move / resize / rotate it and its colour, stroke and fill can be
    // changed, until it is committed (merged into the canvas pixels).
    //
    // Geometry is in paint-layer pixels. P1/P2 are opposite corners of the
    // unrotated box (a line's endpoints); Angle turns the box about its centre.
    // Text is vector (glyph paths), so it stays crisp at any size. Its box is the
    // layout box, as a Figma text layer: the widest line × the number of lines ×
    // the font's line spacing, lines aligned inside it ("auto width" — the box
    // grows as you type, from the left / centre / right edge per alignment).
    public sealed class LiveItem : IDisposable
    {
        public LiveKind  Kind;
        public PointF    P1, P2;
        public float     Angle;                    // degrees, clockwise on screen
        public Color     Color       = Color.Black; // alpha = opacity
        public float     StrokeWidth = 4f;
        public ShapeFill Fill        = ShapeFill.Outline;

        public string    Text;
        public string    FontFamily  = "Segoe UI";
        public FontStyle FontStyle   = FontStyle.Regular;
        public float     FontPx      = 64f;         // em size in layer px
        public StringAlignment Align = StringAlignment.Near;   // line alignment in the box

        public Bitmap    Image;                     // owned (cut-out region)

        // A lasso selection (Kind == Group): the ops that were picked up, held as they were
        // drawn, shown through the frame. The frame starts as the box around their ink (Box0, sheet
        // px, unrotated) and the user moves / scales / turns it; the picture follows the change from
        // Box0 to the current box. M0 carries the ops' own transform at the lift (identity, or a
        // group's), GroupK is the sheet's y per layer y.
        public List<InkOp> Ops;                     // owned
        public float[]   M0 = { 1, 0, 0, 1, 0, 0 };
        public RectangleF Box0;
        public float     GroupK = 1f;
        public float     GroupInk;                  // widest pen among the ops, layer px

        public bool IsGroup { get { return Kind == LiveKind.Group; } }
        public bool IsLine  { get { return Kind == LiveKind.Line; } }
        public bool IsText  { get { return Kind == LiveKind.Text; } }
        public bool IsShape { get { return Kind <= LiveKind.Triangle; } }
        public bool IsClosedShape { get { return Kind == LiveKind.Rect || Kind == LiveKind.Ellipse || Kind == LiveKind.Triangle; } }

        public RectangleF Box
        {
            get
            {
                return RectangleF.FromLTRB(Math.Min(P1.X, P2.X), Math.Min(P1.Y, P2.Y),
                                           Math.Max(P1.X, P2.X), Math.Max(P1.Y, P2.Y));
            }
        }

        public PointF Center
        {
            get { return new PointF((P1.X + P2.X) / 2f, (P1.Y + P2.Y) / 2f); }
        }

        public static LiveKind KindFor(BrushKind k)
        {
            switch (k)
            {
                case BrushKind.ShapeLine:    return LiveKind.Line;
                case BrushKind.ShapeEllipse: return LiveKind.Ellipse;
                case BrushKind.ShapeTriangle:return LiveKind.Triangle;
                default:                     return LiveKind.Rect;
            }
        }

        public static BrushKind BrushFor(LiveKind k)
        {
            switch (k)
            {
                case LiveKind.Line:     return BrushKind.ShapeLine;
                case LiveKind.Ellipse:  return BrushKind.ShapeEllipse;
                case LiveKind.Triangle: return BrushKind.ShapeTriangle;
                case LiveKind.Text:     return BrushKind.Text2D;
                case LiveKind.Image:    return BrushKind.Select2D;
                case LiveKind.Group:    return BrushKind.Lasso;
                default:                return BrushKind.ShapeRect;
            }
        }

        // ── Text layout ───────────────────────────────────────────────
        // Layout px: origin at the box's top-left, unrotated, before any scaling of
        // the box. Each '\n' starts a line; every line knows the caret x of each of
        // its character boundaries, so the editor can place / hit-test the caret.
        public sealed class TextLine
        {
            public int     Start;    // index of the line's first char in Text
            public string  S;
            public float   X, Top;   // line origin (left, top) in layout px
            public float[] Pos;      // caret x of boundaries 0..S.Length, relative to X
            public float   Width { get { return Pos[Pos.Length - 1]; } }
        }

        private TextLine[]   _lines;
        private float        _lineH, _ascent, _layoutW;
        private GraphicsPath _textPath, _underlinePath;
        private string       _layoutKey;

        private static Graphics     _measureG;
        private static StringFormat _measureFmt;

        private static Graphics MeasureG
        {
            get
            {
                if (_measureG == null)
                {
                    _measureG = Graphics.FromImage(new Bitmap(1, 1));
                    _measureG.PageUnit = GraphicsUnit.Pixel;
                    _measureG.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    _measureFmt = (StringFormat)StringFormat.GenericTypographic.Clone();
                    _measureFmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                }
                return _measureG;
            }
        }

        private FontFamily ResolveFamily()
        {
            try { return new FontFamily(FontFamily); }
            catch (ArgumentException) { return new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif); }
        }

        // Bold / italic as available in the family; underline is drawn by us.
        private FontStyle GlyphStyle(FontFamily family)
        {
            FontStyle st = FontStyle & (FontStyle.Bold | FontStyle.Italic);
            if (family.IsStyleAvailable(st)) return st;
            if (family.IsStyleAvailable(FontStyle.Regular)) return FontStyle.Regular;
            return st;
        }

        private void Layout()
        {
            string key = Text + "\u0001" + FontFamily + "\u0001" + (int)FontStyle + "\u0001" + FontPx + "\u0001" + (int)Align;
            if (_lines != null && _layoutKey == key) return;
            _textPath?.Dispose(); _textPath = null;
            _underlinePath?.Dispose(); _underlinePath = null;

            string[] parts = (Text ?? "").Split('\n');
            var lines = new TextLine[parts.Length];
            using (FontFamily family = ResolveFamily())
            {
                FontStyle glyph = GlyphStyle(family);
                float em = Math.Max(1, family.GetEmHeight(glyph));
                _lineH  = family.GetLineSpacing(glyph) / em * FontPx;
                _ascent = family.GetCellAscent(glyph) / em * FontPx;

                float maxW = 0f;
                int start = 0;
                using (var font = new Font(family, Math.Max(0.5f, FontPx), glyph, GraphicsUnit.Pixel))
                {
                    var g = MeasureG;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string line = parts[i];
                        var pos = new float[line.Length + 1];
                        for (int c = 1; c <= line.Length; c++)
                            pos[c] = g.MeasureString(line.Substring(0, c), font, PointF.Empty, _measureFmt).Width;
                        lines[i] = new TextLine { Start = start, S = line, Pos = pos, Top = i * _lineH };
                        maxW = Math.Max(maxW, pos[line.Length]);
                        start += line.Length + 1;
                    }
                }
                _layoutW = Math.Max(maxW, 1f);

                var path = new GraphicsPath();
                bool underline = (FontStyle & FontStyle.Underline) != 0;
                var under = underline ? new GraphicsPath() : null;
                foreach (var ln in lines)
                {
                    ln.X = Align == StringAlignment.Center ? (_layoutW - ln.Width) / 2f
                         : Align == StringAlignment.Far    ? _layoutW - ln.Width : 0f;
                    if (ln.S.Length == 0) continue;
                    path.AddString(ln.S, family, (int)glyph, FontPx, new PointF(ln.X, ln.Top),
                                   StringFormat.GenericTypographic);
                    if (under != null && ln.Width > 0)
                        under.AddRectangle(new RectangleF(ln.X, ln.Top + _ascent + FontPx * 0.08f,
                                                          ln.Width, Math.Max(1f, FontPx * 0.055f)));
                }
                _textPath = path;
                _underlinePath = under;
            }
            _lines = lines;
            _layoutKey = key;
        }

        public TextLine[] Lines      { get { Layout(); return _lines; } }
        public float      LineHeight { get { Layout(); return _lineH; } }

        public SizeF TextLayoutSize
        {
            get { Layout(); return new SizeF(_layoutW, Math.Max(_lineH, _lines.Length * _lineH)); }
        }

        // Line holding the caret index (a caret at a line end belongs to that line).
        public int LineOf(int index)
        {
            var ls = Lines;
            for (int i = ls.Length - 1; i >= 0; i--)
                if (index >= ls[i].Start) return i;
            return 0;
        }

        // Top of the caret at `index`, in layout px.
        public PointF CaretPoint(int index)
        {
            var ls = Lines;
            int li = LineOf(index);
            var ln = ls[li];
            int c = Math.Max(0, Math.Min(ln.S.Length, index - ln.Start));
            return new PointF(ln.X + ln.Pos[c], ln.Top);
        }

        // Nearest caret index to a layout point.
        public int HitTest(PointF lp)
        {
            var ls = Lines;
            int li = (int)Math.Floor(lp.Y / Math.Max(1e-3f, _lineH));
            li = Math.Max(0, Math.Min(ls.Length - 1, li));
            return ls[li].Start + NearestBoundary(ls[li], lp.X - ls[li].X);
        }

        public int IndexAtX(int line, float layoutX)
        {
            var ls = Lines;
            line = Math.Max(0, Math.Min(ls.Length - 1, line));
            return ls[line].Start + NearestBoundary(ls[line], layoutX - ls[line].X);
        }

        private static int NearestBoundary(TextLine ln, float x)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int c = 0; c < ln.Pos.Length; c++)
            {
                float d = Math.Abs(ln.Pos[c] - x);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // Layout px ⇄ layer px (through the box's size, rotation and position).
        public PointF LayoutToLayer(PointF lp)
        {
            SizeF sz = TextLayoutSize;
            RectangleF b = Box;
            float sx = sz.Width  > 0 ? b.Width  / sz.Width  : 1f;
            float sy = sz.Height > 0 ? b.Height / sz.Height : 1f;
            return ToLayer((lp.X - sz.Width / 2f) * sx, (lp.Y - sz.Height / 2f) * sy);
        }

        public PointF LayerToLayout(PointF p)
        {
            SizeF sz = TextLayoutSize;
            RectangleF b = Box;
            float sx = sz.Width  > 0 ? b.Width  / sz.Width  : 1f;
            float sy = sz.Height > 0 ? b.Height / sz.Height : 1f;
            PointF q = ToLocal(p);
            return new PointF(q.X / Math.Max(1e-4f, sx) + sz.Width / 2f, q.Y / Math.Max(1e-4f, sy) + sz.Height / 2f);
        }

        // Keeps the box equal to the text layout. The edge the text grows from stays
        // put (left / centre / right per alignment, top always), in rotated space too,
        // so typing extends the text the way it is aligned.
        public void Normalize()
        {
            if (!IsText) return;
            SizeF sz = TextLayoutSize;
            RectangleF b = Box;
            float ax = Align == StringAlignment.Center ? 0.5f : Align == StringAlignment.Far ? 1f : 0f;
            PointF anchor = ToLayer((ax - 0.5f) * b.Width, -0.5f * b.Height);
            PointF v = Rotate((ax - 0.5f) * sz.Width, -0.5f * sz.Height);
            SetBox(new PointF(anchor.X - v.X, anchor.Y - v.Y), sz.Width, sz.Height);
        }

        private PointF Rotate(float x, float y)
        {
            double a = Angle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            return new PointF(x * cos - y * sin, x * sin + y * cos);
        }

        // ── Rendering (into the paint layer) ──────────────────────────
        public void Render(Graphics g)
        {
            var state = g.Save();
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingMode   = CompositingMode.SourceOver;

            if (IsLine)
            {
                using (var pen = MakePen()) g.DrawLine(pen, P1, P2);
                g.Restore(state);
                return;
            }

            if (IsGroup)
            {
                // The ops live in layer px; the matrix carries them through the lift and the
                // frame's change, and ends in layer px — so it replaces the caller's sheet scale.
                using (var m = GroupMatrix())
                {
                    g.Transform = m;
                    if (Ops != null)
                        foreach (var op in Ops)
                        {
                            var inner = g.Save();
                            op.Draw(g, op.Bounds);
                            g.Restore(inner);
                        }
                }
                g.Restore(state);
                return;
            }

            RectangleF box = Box;
            PointF c = Center;
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(Angle);
            var local = new RectangleF(-box.Width / 2f, -box.Height / 2f, box.Width, box.Height);

            switch (Kind)
            {
                case LiveKind.Rect:
                case LiveKind.Ellipse:
                case LiveKind.Triangle:
                    using (var path = ShapePath(local))
                    {
                        // "Both" = a light tint of the colour under a solid outline.
                        if (Fill != ShapeFill.Outline)
                            using (var b = new SolidBrush(Fill == ShapeFill.Both
                                       ? Color.FromArgb(Color.A * 30 / 100, Color) : Color))
                                g.FillPath(b, path);
                        if (Fill != ShapeFill.Fill)
                            using (var pen = MakePen()) g.DrawPath(pen, path);
                    }
                    break;

                case LiveKind.Text:
                {
                    Layout();
                    SizeF sz = TextLayoutSize;
                    g.TranslateTransform(local.X, local.Y);
                    if (sz.Width > 0 && sz.Height > 0)
                        g.ScaleTransform(local.Width / sz.Width, local.Height / sz.Height);
                    using (var b = new SolidBrush(Color))
                    {
                        g.FillPath(b, _textPath);
                        if (_underlinePath != null) g.FillPath(b, _underlinePath);
                    }
                    break;
                }

                case LiveKind.Image:
                    if (Image != null)
                    {
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        using (var ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe at the edges
                            g.DrawImage(Image, Rectangle.Round(local), 0, 0, Image.Width, Image.Height,
                                        GraphicsUnit.Pixel, ia);
                        }
                    }
                    break;
            }
            g.Restore(state);
        }

        // Ops' space → layer px for the frame as it is now: the lift transform, to sheet px, from
        // the frame at the lift to the current one (about its centre, turned by Angle), back.
        public Matrix GroupMatrix()
        {
            var m = new Matrix(M0[0], M0[1], M0[2], M0[3], M0[4], M0[5]);
            float k = Math.Max(1e-4f, GroupK);
            m.Scale(1f, k, MatrixOrder.Append);
            PointF c0 = new PointF(Box0.Left + Box0.Width / 2f, Box0.Top + Box0.Height / 2f);
            RectangleF b = Box;
            float sx = Box0.Width  > 1e-4f ? b.Width  / Box0.Width  : 1f;
            float sy = Box0.Height > 1e-4f ? b.Height / Box0.Height : 1f;
            m.Translate(-c0.X, -c0.Y, MatrixOrder.Append);
            m.Scale(sx, sy, MatrixOrder.Append);
            m.Rotate(Angle, MatrixOrder.Append);
            PointF c = Center;
            m.Translate(c.X, c.Y, MatrixOrder.Append);
            m.Scale(1f, 1f / k, MatrixOrder.Append);
            return m;
        }

        // Scale of the frame against the lift (the ink's margin follows it).
        public float GroupScale
        {
            get
            {
                RectangleF b = Box;
                float sx = Box0.Width  > 1e-4f ? b.Width  / Box0.Width  : 1f;
                float sy = Box0.Height > 1e-4f ? b.Height / Box0.Height : 1f;
                return Math.Max(sx, sy);
            }
        }

        private Pen MakePen()
        {
            var pen = new Pen(Color, Math.Max(1f, StrokeWidth));
            if (IsLine) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; }
            pen.LineJoin = Kind == LiveKind.Rect ? LineJoin.Miter : LineJoin.Round;
            return pen;
        }

        private GraphicsPath ShapePath(RectangleF r)
        {
            var p = new GraphicsPath();
            switch (Kind)
            {
                case LiveKind.Ellipse: p.AddEllipse(r); break;
                case LiveKind.Triangle:
                    p.AddPolygon(new[]
                    {
                        new PointF(r.X + r.Width / 2f, r.Top),
                        new PointF(r.Right, r.Bottom),
                        new PointF(r.Left,  r.Bottom),
                    });
                    break;
                default: p.AddRectangle(r); break;
            }
            return p;
        }

        // Layer-pixel area the item paints (rotated box + stroke), for partial redraws.
        public Rectangle PaintBounds(int layerW, int layerH)
        {
            // Italic and descender ink can reach past a text box's advance edges.
            float pad = IsText ? Math.Max(4f, FontPx * 0.3f)
                      : IsGroup ? GroupInk * GroupScale + 8f
                      : (IsShape ? StrokeWidth : 0f) + 4f;
            RectangleF r;
            if (IsLine) r = RectangleF.FromLTRB(Math.Min(P1.X, P2.X), Math.Min(P1.Y, P2.Y),
                                                Math.Max(P1.X, P2.X), Math.Max(P1.Y, P2.Y));
            else
            {
                PointF[] q = Corners();
                float x0 = q[0].X, x1 = q[0].X, y0 = q[0].Y, y1 = q[0].Y;
                foreach (var p in q) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
                r = RectangleF.FromLTRB(x0, y0, x1, y1);
            }
            r.Inflate(pad, pad);
            var rect = Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top),
                                          (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
            return Rectangle.Intersect(rect, new Rectangle(0, 0, layerW, layerH));
        }

        // ── Frame geometry (layer px) ─────────────────────────────────
        // Rotated box corners: TL, TR, BR, BL.
        public PointF[] Corners()
        {
            RectangleF b = Box;
            float hw = b.Width / 2f, hh = b.Height / 2f;
            return new[] { ToLayer(-hw, -hh), ToLayer(hw, -hh), ToLayer(hw, hh), ToLayer(-hw, hh) };
        }

        // Local box coordinates (origin at the centre, unrotated) → layer px.
        public PointF ToLayer(float lx, float ly)
        {
            double a = Angle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            PointF c = Center;
            return new PointF(c.X + lx * cos - ly * sin, c.Y + lx * sin + ly * cos);
        }

        public PointF ToLocal(PointF p)
        {
            double a = -Angle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            PointF c = Center;
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF(dx * cos - dy * sin, dx * sin + dy * cos);
        }

        // Handle anchors in local units of the box half-size: index 0..7 = TL, T,
        // TR, R, BR, B, BL, L (compass order, clockwise from the top-left).
        public static readonly PointF[] HandleDirs =
        {
            new PointF(-1, -1), new PointF(0, -1), new PointF(1, -1), new PointF(1, 0),
            new PointF( 1,  1), new PointF(0,  1), new PointF(-1, 1), new PointF(-1, 0),
        };

        public PointF HandleLayer(int i)
        {
            RectangleF b = Box;
            return ToLayer(HandleDirs[i].X * b.Width / 2f, HandleDirs[i].Y * b.Height / 2f);
        }

        // Point inside the (rotated) box, with a tolerance in layer px.
        public bool Contains(PointF p, float tol)
        {
            if (IsLine) return DistanceToSegment(p, P1, P2) <= Math.Max(tol, StrokeWidth / 2f + tol / 2f);
            PointF q = ToLocal(p);
            RectangleF b = Box;
            return Math.Abs(q.X) <= b.Width / 2f + tol && Math.Abs(q.Y) <= b.Height / 2f + tol;
        }

        // Is there ink of this item within `tol` of p — and where (q)? For the object
        // eraser: unlike Contains (the whole box, for grabbing the item) this follows
        // what is actually drawn — the line, the outline / fill of a shape, the lines
        // of text, the opaque part of a cut-out — so touching the empty middle of an
        // outlined rectangle does not take it.
        public bool NearestInk(PointF p, float tol, out PointF q)
        {
            q = p;
            if (IsLine)
            {
                if (DistanceToSegment(p, P1, P2) > StrokeWidth / 2f + tol) return false;
                q = ClosestPoint(p, P1, P2);
                return true;
            }
            RectangleF b = Box;
            float hw = b.Width / 2f, hh = b.Height / 2f;
            PointF lp = ToLocal(p);

            switch (Kind)
            {
                case LiveKind.Group: return false;   // taken as a whole, by its frame

                case LiveKind.Text:
                {
                    PointF t = LayerToLayout(p);
                    foreach (var ln in Lines)
                    {
                        float x0 = ln.X - tol, x1 = ln.X + ln.Width + tol, y0 = ln.Top - tol, y1 = ln.Top + _lineH + tol;
                        if (ln.S.Length == 0 || t.X < x0 || t.X > x1 || t.Y < y0 || t.Y > y1) continue;
                        q = LayoutToLayer(new PointF(Math.Max(ln.X, Math.Min(ln.X + ln.Width, t.X)),
                                                     Math.Max(ln.Top, Math.Min(ln.Top + _lineH, t.Y))));
                        return true;
                    }
                    return false;
                }

                case LiveKind.Image:
                {
                    if (Image == null || b.Width < 1 || b.Height < 1) return false;
                    float step = Math.Max(1f, tol / 2f);
                    for (float oy = -tol; oy <= tol + 0.01f; oy += step)
                        for (float ox = -tol; ox <= tol + 0.01f; ox += step)
                        {
                            float sx = lp.X + ox, sy = lp.Y + oy;
                            if (Math.Abs(sx) > hw || Math.Abs(sy) > hh) continue;
                            int ix = Math.Min(Image.Width - 1,  (int)((sx + hw) / b.Width  * Image.Width));
                            int iy = Math.Min(Image.Height - 1, (int)((sy + hh) / b.Height * Image.Height));
                            if (Image.GetPixel(ix, iy).A <= 16) continue;
                            q = ToLayer(sx, sy);
                            return true;
                        }
                    return false;
                }

                default:   // rectangle, ellipse, triangle
                {
                    PointF[] poly;
                    using (var path = ShapePath(new RectangleF(-hw, -hh, b.Width, b.Height)))
                    {
                        path.Flatten(null, 0.25f);
                        poly = path.PathPoints;
                    }
                    if (poly.Length < 3) return false;
                    float best = float.MaxValue; PointF bq = lp;
                    for (int i = 0; i < poly.Length; i++)
                    {
                        PointF a = poly[i], c = poly[(i + 1) % poly.Length];
                        PointF cp = ClosestPoint(lp, a, c);
                        float dx = cp.X - lp.X, dy = cp.Y - lp.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d < best) { best = d; bq = cp; }
                    }
                    bool inside = InPolygon(poly, lp);
                    bool onLine = Fill != ShapeFill.Fill && best <= StrokeWidth / 2f + tol;   // the outline is drawn
                    bool onFill = Fill != ShapeFill.Outline && (inside || best <= tol);       // the interior is drawn
                    if (!onLine && !onFill) return false;
                    q = ToLayer(inside && Fill != ShapeFill.Outline ? lp.X : bq.X, inside && Fill != ShapeFill.Outline ? lp.Y : bq.Y);
                    return true;
                }
            }
        }

        private static PointF ClosestPoint(PointF p, PointF a, PointF b)
        {
            return Aplat.Models.InkOp.ClosestOnSegment(p, a, b);
        }

        private static bool InPolygon(PointF[] poly, PointF p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            return inside;
        }

        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            float x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        // Re-centres the box: sets P1/P2 so the unrotated box of the given size is
        // centred on `center` (layer px).
        public void SetBox(PointF center, float width, float height)
        {
            P1 = new PointF(center.X - width / 2f, center.Y - height / 2f);
            P2 = new PointF(center.X + width / 2f, center.Y + height / 2f);
        }

        public LiveItem Clone()
        {
            var c = (LiveItem)MemberwiseClone();
            c._textPath = null;          // the clone lays its text out afresh
            c._underlinePath = null;
            c._lines = null;
            c._layoutKey = null;
            c.Image     = Image != null ? new Bitmap(Image) : null;
            if (Ops != null)
            {
                c.Ops = new List<InkOp>(Ops.Count);
                foreach (var op in Ops) c.Ops.Add(op.Clone());
            }
            c.M0 = (float[])M0.Clone();
            return c;
        }

        public void Dispose()
        {
            _textPath?.Dispose(); _textPath = null;
            _underlinePath?.Dispose(); _underlinePath = null;
            Image?.Dispose();     Image = null;
            if (Ops != null) { foreach (var op in Ops) op.Dispose(); Ops = null; }
        }
    }
}
