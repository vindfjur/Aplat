using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace Aplat.Models
{
    // One square of a paint layer, as an undo snapshot stores it: the PNG of the 128 px block
    // (<TileBase64 x="3" y="2">…</TileBase64>). A block that did not change between two steps is
    // the same bytes in both, so the file / history keeps it once (see IO/BlobPacker).
    public class PaintTile
    {
        [XmlAttribute("x")] public int X { get; set; }
        [XmlAttribute("y")] public int Y { get; set; }
        [XmlText] public string Data { get; set; }
    }

    // Pixel work on tiles: fingerprint, PNG encode, paste.
    internal static class TileKit
    {
        public const int Size = 128;

        public static int Grid(int pixels) { return (pixels + Size - 1) / Size; }

        public static int Key(int x, int y) { return y * 4096 + x; }

        public static Rectangle Area(int tx, int ty, int w, int h)
        {
            int x = tx * Size, y = ty * Size;
            return new Rectangle(x, y, Math.Min(Size, w - x), Math.Min(Size, h - y));
        }

        // 64-bit fingerprint of a block (two independent 32-bit mixes), and whether it has any
        // visible pixel at all. Invisible pixels count as zero, so RGB left under alpha 0 is no
        // difference. `data` is a locked 32bpp ARGB view of the whole layer.
        public static ulong Hash(BitmapData data, Rectangle r, out bool empty)
        {
            uint a = 2166136261u, b = 0x9E3779B9u;
            bool any = false;
            var row = new int[r.Width];
            for (int y = r.Top; y < r.Bottom; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride + r.Left * 4), row, 0, r.Width);
                for (int x = 0; x < row.Length; x++)
                {
                    uint v = (uint)row[x];
                    if ((v >> 24) == 0) v = 0; else any = true;
                    a = (a ^ v) * 16777619u;
                    b = (b + v) * 0x85EBCA6Bu;
                    b ^= b >> 15;
                }
            }
            empty = !any;
            return ((ulong)a << 32) | b;
        }

        public static ulong HashOf(Bitmap layer, Rectangle r)
        {
            bool empty;
            var data = layer.LockBits(new Rectangle(0, 0, layer.Width, layer.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { return Hash(data, r, out empty); }
            finally { layer.UnlockBits(data); }
        }

        // Locks are always of the whole bitmap: a lock of a part of it is not reliable everywhere
        // (libgdiplus writes it back over the layer's corner even when only read).
        private static Rectangle Whole(Bitmap b) { return new Rectangle(0, 0, b.Width, b.Height); }

        public static string Encode(Bitmap layer, Rectangle r)
        {
            using (var tile = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                var src = layer.LockBits(Whole(layer), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var dst = tile.LockBits(Whole(tile), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[r.Width];
                    for (int y = 0; y < r.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(src.Scan0, (r.Top + y) * src.Stride + r.Left * 4), row, 0, r.Width);
                        Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, y * dst.Stride), r.Width);
                    }
                }
                finally { layer.UnlockBits(src); tile.UnlockBits(dst); }
                using (var ms = new MemoryStream())
                {
                    tile.Save(ms, ImageFormat.Png);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        // Replaces the block's pixels (transparent ones too) with the PNG's.
        public static void Paste(Bitmap layer, int tx, int ty, string base64)
        {
            var r = Area(tx, ty, layer.Width, layer.Height);
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
            using (var png = new Bitmap(ms))
            {
                int w = Math.Min(r.Width, png.Width), h = Math.Min(r.Height, png.Height);
                var src = png.LockBits(Whole(png), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var dst = layer.LockBits(Whole(layer), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[r.Width];
                    for (int y = 0; y < r.Height; y++)
                    {
                        Array.Clear(row, 0, row.Length);
                        if (y < h) Marshal.Copy(IntPtr.Add(src.Scan0, y * src.Stride), row, 0, w);
                        Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, (r.Top + y) * dst.Stride + r.Left * 4), r.Width);
                    }
                }
                finally { png.UnlockBits(src); layer.UnlockBits(dst); }
            }
        }

        public static void Clear(Bitmap layer, Rectangle r)
        {
            r.Intersect(Whole(layer));
            if (r.Width <= 0 || r.Height <= 0) return;
            var dst = layer.LockBits(Whole(layer), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[r.Width];
                for (int y = 0; y < r.Height; y++)
                    Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, (r.Top + y) * dst.Stride + r.Left * 4), r.Width);
            }
            finally { layer.UnlockBits(dst); }
        }
    }
}
