using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Aplat.UI
{
    public enum HAlign { Left, Center, Right }

    // Pixel-exact text for the owner-drawn UI.
    //
    // Text is rendered once into a transparent sprite (cached), measured from that
    // sprite's own pixels, and copied to the screen 1:1 at a whole-pixel position.
    // What is measured is therefore exactly what is shown — on Windows and on Mono,
    // on a window, a double buffer or a bitmap alike. (Measuring with font metrics
    // and drawing separately left labels 1–3px off centre: TextRenderer pads its
    // measurement, hinting moves glyph edges, and surfaces differ.)
    //
    // Placement is optical:
    //  • horizontally the visible ink is aligned — a centred "Ctrl" has equal space
    //    on both sides, a right-aligned "°" or "px" ends exactly at the inset;
    //  • vertically the band from the cap top to the baseline (from a rendered "H",
    //    once per font) is centred, so "Ctrl", "512" or "Grid" sit in the middle
    //    of their box whatever the font's ascent/descent padding. Descenders hang.
    // An edge is where coverage reaches 50% — where the eye puts it.
    public static class TextKit
    {
        private const int Pad = 6;                 // sprite margin around the text
        private const int MaxSprites = 800;        // bounded: values change while dragging

        private static readonly StringFormat Format = CreateFormat();
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Ink> Inks = new Dictionary<string, Ink>();
        private static readonly Dictionary<string, Band> Bands = new Dictionary<string, Band>();
        private static Bitmap _probe;
        private static Graphics _measure;

        private sealed class Sprite { public Bitmap Image; public Ink Ink; }
        private struct Ink  { public int Left, Right, Top, Bottom; }   // relative to the draw origin
        private struct Band { public int Top, Height; }

        private static StringFormat CreateFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap
                           | StringFormatFlags.NoClip;
            f.Trimming = StringTrimming.None;
            return f;
        }

        private static Graphics MeasureGraphics
        {
            get
            {
                if (_measure == null)
                {
                    _probe = new Bitmap(1, 1);
                    _measure = Graphics.FromImage(_probe);
                    _measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                }
                return _measure;
            }
        }

        private static string FontKey(Font font)
        {
            return font.FontFamily.Name + "|" + font.SizeInPoints + "|" + (int)font.Style;
        }

        // Advance width (typographic); used only to size sprites.
        public static int Width(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (int)Math.Ceiling(MeasureGraphics.MeasureString(text, font, PointF.Empty, Format).Width);
        }

        // Visible width of the text: first to last inked column.
        public static int InkWidth(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Ink ink = InkOf(text, font);
            return ink.Right - ink.Left + 1;
        }

        // Height of the cap band (cap top → baseline) in pixels.
        public static int CapHeight(Font font)
        {
            return BandOf(font).Height;
        }

        public static void Draw(Graphics g, string text, Font font, Color color, Rectangle box,
                                HAlign align = HAlign.Left, bool ellipsis = false)
        {
            if (string.IsNullOrEmpty(text) || box.Width <= 0) return;
            if (ellipsis) text = Fit(text, font, box.Width);

            Sprite sprite = SpriteOf(text, font, color);
            Ink ink = sprite.Ink;
            int inkW = ink.Right - ink.Left + 1;
            int x = align == HAlign.Center ? box.X + (box.Width - inkW) / 2 - ink.Left
                  : align == HAlign.Right  ? box.Right - 1 - ink.Right
                  : box.X - ink.Left;
            Band band = BandOf(font);
            int y = box.Y + (box.Height - band.Height) / 2 - band.Top;

            // A 1:1 copy at whole pixels: no resampling, no half-pixel shift.
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode   = PixelOffsetMode.Half;
            g.CompositingMode   = CompositingMode.SourceOver;
            var img = sprite.Image;
            g.DrawImage(img, new Rectangle(x - Pad, y - Pad, img.Width, img.Height),
                        0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
            g.Restore(state);
        }

        // Shortens with "…" until the text fits the width.
        public static string Fit(string text, Font font, int width)
        {
            if (InkWidth(text, font) <= width) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string s = text.Substring(0, n).TrimEnd() + "…";
                if (InkWidth(s, font) <= width) return s;
            }
            return "…";
        }

        // ── Sprites ───────────────────────────────────────────────────
        private static Sprite SpriteOf(string text, Font font, Color color)
        {
            string key = FontKey(font) + "|" + color.ToArgb() + "|" + text;
            Sprite sprite;
            if (Sprites.TryGetValue(key, out sprite)) return sprite;
            if (Sprites.Count >= MaxSprites)
            {
                foreach (var old in Sprites.Values) old.Image.Dispose();
                Sprites.Clear();
            }
            sprite = Render(text, font, color);
            Sprites[key] = sprite;
            if (Inks.Count >= MaxSprites * 4) Inks.Clear();
            Inks[FontKey(font) + "|" + text] = sprite.Ink;
            return sprite;
        }

        private static Ink InkOf(string text, Font font)
        {
            Ink ink;
            if (Inks.TryGetValue(FontKey(font) + "|" + text, out ink)) return ink;
            return SpriteOf(text, font, Color.Black).Ink;   // coverage is colour-independent
        }

        private static Band BandOf(Font font)
        {
            string key = FontKey(font);
            Band band;
            if (Bands.TryGetValue(key, out band)) return band;
            Sprite h = Render("H", font, Color.Black);
            h.Image.Dispose();
            band = h.Ink.Bottom >= h.Ink.Top
                ? new Band { Top = h.Ink.Top, Height = h.Ink.Bottom - h.Ink.Top + 1 }
                : new Band { Top = 0, Height = (int)Math.Round(font.GetHeight(MeasureGraphics)) };
            Bands[key] = band;
            return band;
        }

        // Renders white text on transparency, then keeps only its coverage and paints
        // it in the requested colour: pure greyscale anti-aliasing on every platform
        // (some rasterisers put coloured sub-pixel fringes on a transparent surface).
        private static Sprite Render(string text, Font font, Color color)
        {
            int w = Math.Max(1, Width(text, font)) + Pad * 2;
            int h = (int)Math.Ceiling(font.GetHeight(MeasureGraphics)) + Pad * 2;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, font, Brushes.White, Pad, Pad, Format);
            }
            Tint(bmp, color, 255);
            Ink ink = Measure(bmp);                     // on full coverage, before any fade
            if (color.A < 255) Tint(bmp, color, color.A);
            if (ink.Right < ink.Left)   // whitespace only
                ink = new Ink { Left = 0, Right = Math.Max(0, w - Pad * 2 - 1), Top = 0, Bottom = -1 };
            return new Sprite { Image = bmp, Ink = ink };
        }

        private static Ink Measure(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
            try
            {
                var row = new byte[data.Stride];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, data.Stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (row[x * 4 + 3] < 128) continue;   // alpha = coverage
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        bottom = y;
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            if (right < 0) return new Ink { Left = 0, Right = -1, Top = 0, Bottom = -1 };
            return new Ink { Left = left - Pad, Right = right - Pad, Top = top - Pad, Bottom = bottom - Pad };
        }

        // First pass (opacity 255): coverage = alpha × mean(RGB) of the white render,
        // painted in the text colour. A second pass with opacity < 255 fades it.
        private static void Tint(Bitmap bmp, Color color, int opacity)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * bmp.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int i = 0; i + 3 < bytes.Length; i += 4)
                {
                    int cov = opacity == 255
                        ? bytes[i + 3] * (bytes[i] + bytes[i + 1] + bytes[i + 2]) / (3 * 255)
                        : bytes[i + 3] * opacity / 255;
                    bytes[i]     = color.B;
                    bytes[i + 1] = color.G;
                    bytes[i + 2] = color.R;
                    bytes[i + 3] = (byte)cov;
                }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { bmp.UnlockBits(data); }
        }
    }

    // A keyboard key cap — [Ctrl] — used by the status bar hints and the command
    // palette, so every cap in the app has the same fill, border and centring.
    public static class KeyCap
    {
        // Ink + 6px each side, at least `min` wide; the spare pixels always split
        // evenly (a one-letter cap gets an even remainder), so the letter is centred.
        public static int WidthFor(string key, Font font, int min)
        {
            int ink = TextKit.InkWidth(key, font);
            int w = Math.Max(min, ink + 12);
            if ((w - ink) % 2 != 0) w++;
            return w;
        }

        public static void Draw(Graphics g, string key, Font font, Rectangle cap)
        {
            using (var b = new SolidBrush(Theme.BtnFace)) g.FillRectangle(b, cap);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, cap.X, cap.Y, cap.Width - 1, cap.Height - 1);
            TextKit.Draw(g, key, font, Theme.TextSoft, cap, HAlign.Center);
        }
    }
}
