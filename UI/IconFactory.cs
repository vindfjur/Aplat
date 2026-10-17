using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Aplat.UI
{
    public enum UiIcon
    {
        None,
        Menu,
        New,
        Open,
        Save,
        Undo,
        Redo,
        Brush,
        Pencil,
        Pen,
        Eyedropper,
        Fill,
        Eraser,
        Hand,
        Text,
        Line,
        Rect,
        Ellipse,
        Triangle,
        Cube,
        Sphere,
        Cylinder,
        Cone,
        Tetrahedron,
        Pyramid,
        Torus,
        Color,
        Clear,
        Select,
        Move,
        Rotate,
        Scale,
        EditShape,
        Wireframe,
        Vertices,
        Grid,
        Canvas,
        Export,
        Help,
        Texture,
        Lock,
        Unlock,
        Eye,
        EyeOff,
        ViewFront,
        ViewSide,
        ViewTop,
        ViewPerspective,
        ZoomFit,
        Guides,
        Speed,
        Zoom,
        Marker,
        SelectRect,
        Light,
        Close,
        History,
        Keyboard,
        Plus,
        Panels,
        ThemeLight,
        ThemeDark,
        ThemeSystem,
        Palette,
        Cut,
        Copy,
        Paste,
        Duplicate,
        Deselect,
        Group,
        Ungroup,
        Rename,
        Ground,
        Capsule,
        EraserObject,
        Check,
        Bold,
        Italic,
        Underline,
        AlignLeft,
        AlignCenter,
        AlignRight,
        Command,
        Lasso,
        Ruler,
        Shadows,
        Activity,
        Refresh,
        Bell,
        BellOff,
        Play,
        Pause,
        Stop
    }

    // Renders the app's icon set. Every icon is SVG path data on Lucide's 24px grid
    // (UI/IconPaths.cs), stroked with one pen
    // width and round caps/joins — a single visual family at every size, centred by
    // construction. No system icon font: its glyph metrics differ between Windows
    // builds and were the cause of icons sitting off-centre in their buttons.
    public static class IconFactory
    {
        private const float Grid = 24f;          // design grid of the path data
        private const float StrokeOnGrid = 1.85f; // Lucide's 2px, a touch finer

        // Parsed geometry per icon, in grid units (parsed once, reused for every size).
        private static readonly Dictionary<UiIcon, IconShape> Shapes = new Dictionary<UiIcon, IconShape>();

        public static Image Create(UiIcon icon)
        {
            return Create(icon, Theme.Text, 18);
        }

        public static Image Create(UiIcon icon, Color color)
        {
            return Create(icon, color, 18);
        }

        public static Image Create(UiIcon icon, Color color, int size)
        {
            size = Math.Max(8, size);
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Draw(g, icon, color, new RectangleF(0, 0, size, size));
            }
            return bmp;
        }

        // Draws the icon scaled into `bounds` (square area, centred if not square).
        public static void Draw(Graphics g, UiIcon icon, Color color, RectangleF bounds)
        {
            IconShape shape = GetShape(icon);
            if (shape == null) return;

            float side = Math.Min(bounds.Width, bounds.Height);
            float scale = side / Grid;
            var state = g.Save();
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(bounds.X + (bounds.Width - side) / 2f, bounds.Y + (bounds.Height - side) / 2f);
            g.ScaleTransform(scale, scale);

            // Keep the on-screen stroke at least ~1.2px so small icons don't go grey.
            float width = Math.Max(StrokeOnGrid, 1.2f / scale);
            using (var pen = new Pen(color, width))
            using (var fill = new SolidBrush(Color.FromArgb((int)(color.A * 0.32f), color)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap   = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                foreach (var f in shape.Fills)   g.FillPath(fill, f);
                foreach (var s in shape.Strokes) g.DrawPath(pen, s);
            }
            g.Restore(state);
        }

        private static IconShape GetShape(UiIcon icon)
        {
            if (icon == UiIcon.None) return null;
            IconShape shape;
            lock (Shapes)
            {
                if (Shapes.TryGetValue(icon, out shape)) return shape;
                string[] data;
                if (!IconPaths.Map.TryGetValue(icon, out data)) return null;
                shape = new IconShape();
                foreach (string d in data)
                {
                    bool filled = d.StartsWith("F:", StringComparison.Ordinal);
                    var path = SvgPath.Parse(filled ? d.Substring(2) : d);
                    (filled ? shape.Fills : shape.Strokes).Add(path);
                }
                Shapes[icon] = shape;
            }
            return shape;
        }

        private sealed class IconShape
        {
            public readonly List<GraphicsPath> Strokes = new List<GraphicsPath>();
            public readonly List<GraphicsPath> Fills   = new List<GraphicsPath>();
        }
    }

    // Minimal SVG path-data parser (SVG 1.1 §8.3): M L H V C S Q T A Z, absolute and
    // relative, with the compact number / arc-flag syntax icon sets use ("a1 1 0 00-2 0").
    // Quadratics are raised to cubics and elliptical arcs converted to cubic Béziers,
    // since GraphicsPath can't express rotated arcs.
    internal static class SvgPath
    {
        public static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath(FillMode.Winding);
            var r = new Reader(d);
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            char cmd = ' ', prev = ' ';

            while (r.SkipSeparators())
            {
                if (r.PeekCommand()) cmd = r.ReadCommand();
                else if (cmd == 'M') cmd = 'L';          // implicit lineto after moveto
                else if (cmd == 'm') cmd = 'l';

                bool rel = char.IsLower(cmd);
                PointF o = rel ? cur : PointF.Empty;
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        cur = start = Add(o, r.Point());
                        path.StartFigure();
                        break;
                    case 'L':
                    {
                        var p = Add(o, r.Point());
                        path.AddLine(cur, p); cur = p;
                        break;
                    }
                    case 'H':
                    {
                        var p = new PointF(r.Number() + (rel ? cur.X : 0), cur.Y);
                        path.AddLine(cur, p); cur = p;
                        break;
                    }
                    case 'V':
                    {
                        var p = new PointF(cur.X, r.Number() + (rel ? cur.Y : 0));
                        path.AddLine(cur, p); cur = p;
                        break;
                    }
                    case 'C':
                    {
                        var c1 = Add(o, r.Point()); var c2 = Add(o, r.Point()); var p = Add(o, r.Point());
                        path.AddBezier(cur, c1, c2, p);
                        lastCtrl = c2; cur = p;
                        break;
                    }
                    case 'S':
                    {
                        var c1 = "CcSs".IndexOf(prev) >= 0 ? Reflect(lastCtrl, cur) : cur;
                        var c2 = Add(o, r.Point()); var p = Add(o, r.Point());
                        path.AddBezier(cur, c1, c2, p);
                        lastCtrl = c2; cur = p;
                        break;
                    }
                    case 'Q':
                    {
                        var q = Add(o, r.Point()); var p = Add(o, r.Point());
                        AddQuad(path, cur, q, p);
                        lastCtrl = q; cur = p;
                        break;
                    }
                    case 'T':
                    {
                        var q = "QqTt".IndexOf(prev) >= 0 ? Reflect(lastCtrl, cur) : cur;
                        var p = Add(o, r.Point());
                        AddQuad(path, cur, q, p);
                        lastCtrl = q; cur = p;
                        break;
                    }
                    case 'A':
                    {
                        float rx = r.Number(), ry = r.Number(), rot = r.Number();
                        bool large = r.Flag(), sweep = r.Flag();
                        var p = Add(o, r.Point());
                        AddArc(path, cur, p, rx, ry, rot, large, sweep);
                        cur = p;
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        cur = start;
                        break;
                    default:
                        throw new FormatException("Unsupported SVG path command '" + cmd + "'");
                }
                prev = cmd;
                if (char.ToUpperInvariant(cmd) == 'Z') cmd = ' ';
            }
            return path;
        }

        private static PointF Add(PointF a, PointF b) { return new PointF(a.X + b.X, a.Y + b.Y); }
        private static PointF Reflect(PointF ctrl, PointF about) { return new PointF(2 * about.X - ctrl.X, 2 * about.Y - ctrl.Y); }

        private static void AddQuad(GraphicsPath path, PointF p0, PointF q, PointF p1)
        {
            var c1 = new PointF(p0.X + 2f / 3f * (q.X - p0.X), p0.Y + 2f / 3f * (q.Y - p0.Y));
            var c2 = new PointF(p1.X + 2f / 3f * (q.X - p1.X), p1.Y + 2f / 3f * (q.Y - p1.Y));
            path.AddBezier(p0, c1, c2, p1);
        }

        // Endpoint → centre parameterisation (SVG 1.1 §F.6.5), then ≤90° cubic segments.
        private static void AddArc(GraphicsPath path, PointF p0, PointF p1,
                                   float rxIn, float ryIn, float rotDeg, bool large, bool sweep)
        {
            double rx = Math.Abs(rxIn), ry = Math.Abs(ryIn);
            if (rx < 1e-6 || ry < 1e-6 || (p0.X == p1.X && p0.Y == p1.Y)) { path.AddLine(p0, p1); return; }

            double phi = rotDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 =  cos * dx + sin * dy;
            double y1 = -sin * dx + cos * dy;

            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { double s = Math.Sqrt(lambda); rx *= s; ry *= s; }

            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp =  coef * rx * y1 / ry;
            double cyp = -coef * ry * x1 / rx;
            double cx = cos * cxp - sin * cyp + (p0.X + p1.X) / 2.0;
            double cy = sin * cxp + cos * cyp + (p0.Y + p1.Y) / 2.0;

            double theta1 = Angle(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
            double delta  = Angle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            else if (sweep && delta < 0) delta += 2 * Math.PI;

            int segs = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
            if (segs < 1) segs = 1;
            double step = delta / segs;
            double k = 4.0 / 3.0 * Math.Tan(step / 4);
            PointF from = p0;
            for (int i = 0; i < segs; i++)
            {
                double a1 = theta1 + i * step, a2 = a1 + step;
                double c1x = Math.Cos(a1) - k * Math.Sin(a1), c1y = Math.Sin(a1) + k * Math.Cos(a1);
                double c2x = Math.Cos(a2) + k * Math.Sin(a2), c2y = Math.Sin(a2) - k * Math.Cos(a2);
                PointF c1 = Map(c1x, c1y, rx, ry, cos, sin, cx, cy);
                PointF c2 = Map(c2x, c2y, rx, ry, cos, sin, cx, cy);
                PointF to = i == segs - 1 ? p1 : Map(Math.Cos(a2), Math.Sin(a2), rx, ry, cos, sin, cx, cy);
                path.AddBezier(from, c1, c2, to);
                from = to;
            }
        }

        private static PointF Map(double ux, double uy, double rx, double ry, double cos, double sin, double cx, double cy)
        {
            double x = ux * rx, y = uy * ry;
            return new PointF((float)(cos * x - sin * y + cx), (float)(sin * x + cos * y + cy));
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) { _s = s ?? string.Empty; }

            // Skips whitespace/commas; false at end of input.
            public bool SkipSeparators()
            {
                while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',')) _i++;
                return _i < _s.Length;
            }

            public bool PeekCommand()
            {
                char c = _s[_i];
                return char.IsLetter(c) && c != 'e' && c != 'E';
            }

            public char ReadCommand() { return _s[_i++]; }

            public PointF Point() { float x = Number(); float y = Number(); return new PointF(x, y); }

            // Arc flags are single characters and may be written without separators.
            public bool Flag()
            {
                SkipSeparators();
                if (_i >= _s.Length || (_s[_i] != '0' && _s[_i] != '1'))
                    throw new FormatException("Bad arc flag in SVG path at " + _i);
                return _s[_i++] == '1';
            }

            public float Number()
            {
                SkipSeparators();
                int start = _i;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                bool dot = false;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (char.IsDigit(c)) { _i++; continue; }
                    if (c == '.' && !dot) { dot = true; _i++; continue; }
                    break;
                }
                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    int save = _i++;
                    if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                    if (_i < _s.Length && char.IsDigit(_s[_i])) { while (_i < _s.Length && char.IsDigit(_s[_i])) _i++; }
                    else _i = save;
                }
                if (_i == start) throw new FormatException("Number expected in SVG path at " + start);
                return float.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
