using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Aplat.Models
{
    // The drawing sheet as a list of objects.
    //
    // A canvas layer is a bitmap, but it is also the result of a short, ordered
    // list of drawing operations (InkOp) replayed over a base. The base is empty for
    // a new drawing, or the old flat picture of a file that predates the journal.
    // Keeping the list lets the object eraser take one thing — a stroke, a shape, a
    // line of text, a fill — off the sheet whole: the layer is rebuilt under that
    // op's footprint from the ops that remain, so whatever it covered comes back.
    //
    // Invariant, kept by every writer of a canvas layer (SceneView): the layer's
    // pixels == base + replay(ops). The journal is saved next to the PNG with a
    // stamp of the layer's pixels; if the two ever disagree on load (a file edited
    // elsewhere) the journal is dropped and the picture becomes the new base.
    public sealed class InkJournal : IDisposable
    {
        // Past this the oldest ops are flattened into the base: memory and replay
        // cost stay bounded in a long session, only the very old things stop being
        // erasable as objects.
        public const int  MaxOps    = 1500;
        public const int  KeepOps   = 400;
        public const long MaxWeight = 6000000;

        // INK1 kept the base picture inside; INK2 leaves it out (it is stored once, as
        // a picture of its own, however many saved states share it) and records the layer's size.
        // INK3 keeps the ops out of the header too: each op is its own record (InkOp.Record64).
        private const string Magic = "INK3", MagicV2 = "INK2", MagicV1 = "INK1";

        public readonly List<InkOp> Ops = new List<InkOp>();
        public int Revision { get; private set; }

        private byte[] _basePng;     // pre-journal picture (null = transparent)
        private Bitmap _base;        // its decoded form, kept once something needs it
        private int    _nextId = 1;

        public bool IsEmpty  { get { return Ops.Count == 0 && _basePng == null; } }
        public bool HasBase  { get { return _basePng != null; } }

        // The pre-journal picture as PNG bytes (null: transparent). Immutable — shared by clones.
        public byte[] BasePng { get { return _basePng; } }

        // The layer's size, as recorded when the journal was saved (0×0: unknown, older data).
        public Size LayerSize { get; private set; }

        public static InkJournal WithBase(byte[] png)
        {
            return new InkJournal { _basePng = png };
        }

        // ── Recording ─────────────────────────────────────────────────
        public void Add(InkOp op)
        {
            op.Id = _nextId++;
            Ops.Add(op);
            Revision++;
        }

        // Takes an op out without touching any pixels (the live element that was
        // just committed is being reopened and will draw itself again).
        public void Discard(InkOp op)
        {
            if (Ops.Remove(op)) { op.Dispose(); Revision++; }
        }

        public bool NeedsBake
        {
            get
            {
                if (Ops.Count > MaxOps) return true;
                long w = 0;
                foreach (var o in Ops) w += o.Weight;
                return w > MaxWeight;
            }
        }

        // Flattens the oldest ops into the base (see MaxOps).
        public void Bake(Size layerSize)
        {
            int n = Ops.Count - KeepOps;
            if (n <= 0) return;
            var flat = new Bitmap(layerSize.Width, layerSize.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(flat))
            {
                var all = new Rectangle(Point.Empty, layerSize);
                DrawBase(g, all);
                g.SetClip(all);
                for (int i = 0; i < n; i++) { FreshState(g); Ops[i].Draw(g, all); }
            }
            _basePng = EncodePng(flat);
            _base?.Dispose();
            _base = flat;
            for (int i = 0; i < n; i++) Ops[i].Dispose();
            Ops.RemoveRange(0, n);
            Revision++;
        }

        // ── Erasing an object ─────────────────────────────────────────
        // Removes the op and rebuilds the layer where it was. Returns the area to
        // refresh on screen.
        public Rectangle Remove(InkOp op, Bitmap layer)
        {
            Rectangle area = op.Bounds;
            if (!Ops.Remove(op)) return Rectangle.Empty;
            op.Dispose();
            Revision++;
            Replay(layer, area);
            return area;
        }

        // The lasso picks ops up to edit them as one live element: they leave the journal (not
        // disposed — the caller keeps them, to put them back on Esc) and the layer is replayed
        // without them. Returns where they were, in order, and the area to refresh.
        public Rectangle Lift(IList<InkOp> picked, Bitmap layer, out List<KeyValuePair<int, InkOp>> removed)
        {
            removed = new List<KeyValuePair<int, InkOp>>();
            Rectangle area = Rectangle.Empty;
            for (int i = 0; i < Ops.Count; i++)
                if (picked.Contains(Ops[i]))
                {
                    removed.Add(new KeyValuePair<int, InkOp>(i, Ops[i]));
                    area = area.IsEmpty ? Ops[i].Bounds : Rectangle.Union(area, Ops[i].Bounds);
                }
            for (int i = removed.Count - 1; i >= 0; i--) Ops.RemoveAt(removed[i].Key);
            if (removed.Count > 0) Revision++;
            Replay(layer, area);
            return area;
        }

        // Puts lifted ops back where they were (ascending positions rebuild the original order).
        public void Unlift(List<KeyValuePair<int, InkOp>> removed)
        {
            foreach (var kv in removed) Ops.Insert(Math.Min(kv.Key, Ops.Count), kv.Value);
            if (removed.Count > 0) Revision++;
        }

        // Layer region := base + the ops that reach it, in order.
        public void Replay(Bitmap layer, Rectangle region)
        {
            region.Intersect(new Rectangle(Point.Empty, layer.Size));
            if (region.IsEmpty) return;
            using (var g = Graphics.FromImage(layer))
            {
                g.SetClip(region);
                DrawBase(g, region);
                foreach (var op in Ops)
                    if (op.Bounds.IntersectsWith(region)) { FreshState(g); op.Draw(g, region); }
            }
        }

        // Every op starts from the state of a brand-new Graphics (the clip aside),
        // which is what it was first drawn with — so the replay cannot differ from the
        // live drawing because the previous op left a mode switched on.
        public static void FreshState(Graphics g)
        {
            g.ResetTransform();
            g.CompositingMode    = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.Default;
            g.InterpolationMode  = InterpolationMode.Bilinear;
            g.PixelOffsetMode    = PixelOffsetMode.Default;
            g.SmoothingMode      = SmoothingMode.None;
        }

        private void DrawBase(Graphics g, Rectangle region)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            if (_basePng == null)
                using (var br = new SolidBrush(Color.Transparent)) g.FillRectangle(br, region);
            else
            {
                if (_base == null) _base = DecodePng(_basePng);
                g.DrawImage(_base, region, region, GraphicsUnit.Pixel);
            }
            g.CompositingMode = CompositingMode.SourceOver;
        }

        // ── Hit-testing ───────────────────────────────────────────────
        // The topmost object whose visible ink is within `tol` px of p.
        public InkOp HitTest(Bitmap layer, PointF p, float tol)
        {
            int pad = (int)Math.Ceiling(tol) + 3;
            for (int i = Ops.Count - 1; i >= 0; i--)
            {
                InkOp op = Ops[i];
                if (!op.IsObject) continue;
                Rectangle b = op.Bounds;
                b.Inflate(pad, pad);
                if (p.X < b.Left || p.X > b.Right || p.Y < b.Top || p.Y > b.Bottom) continue;
                PointF q;
                if (!op.NearestInk(p, tol, out q)) continue;
                if (op.ChecksVisibility && !VisibleAt(layer, q)) continue;   // wiped by the pixel eraser: nothing to see there
                return op;
            }
            return null;
        }

        private static bool VisibleAt(Bitmap layer, PointF q)
        {
            int x = Math.Max(0, Math.Min(layer.Width - 1, (int)q.X));
            int y = Math.Max(0, Math.Min(layer.Height - 1, (int)q.Y));
            return layer.GetPixel(x, y).A > 12;
        }

        // ── Copies ────────────────────────────────────────────────────
        public InkJournal Clone()
        {
            var c = new InkJournal { _basePng = _basePng, _nextId = _nextId, Revision = Revision, LayerSize = LayerSize };
            foreach (var o in Ops) c.Ops.Add(o.Clone());
            return c;
        }

        public void Dispose()
        {
            foreach (var o in Ops) o.Dispose();
            Ops.Clear();
            _base?.Dispose();
            _base = null;
        }

        // ── Persistence ───────────────────────────────────────────────
        // A saved journal is three parts: this header (small, changes with every op), the ops
        // as separate records (OpRecords — each stored once, whatever number of saved states
        // contain it) and the base picture (BasePng, likewise).
        public byte[] SerializeHeader(uint stamp, Size layerSize)
        {
            using (var ms = new MemoryStream())
            {
                byte[] magic = System.Text.Encoding.ASCII.GetBytes(Magic);
                ms.Write(magic, 0, magic.Length);
                using (var z = new DeflateStream(ms, CompressionMode.Compress, true))
                using (var w = new BinaryWriter(z))
                {
                    w.Write(stamp);
                    w.Write(_nextId);
                    w.Write(_basePng != null);
                    w.Write(layerSize.Width); w.Write(layerSize.Height);
                    w.Write(Ops.Count);
                }
                return ms.ToArray();
            }
        }

        public List<string> OpRecords()
        {
            var list = new List<string>(Ops.Count);
            foreach (var op in Ops) list.Add(op.Record64);
            return list;
        }

        // Null when the data is damaged or not ours — the caller carries on without
        // a journal rather than failing the load.
        public static InkJournal Deserialize(byte[] data, IList<string> opRecords, byte[] basePng, out uint stamp)
        {
            stamp = 0;
            try
            {
                if (data == null || data.Length < 8) return null;
                bool v1 = Matches(data, MagicV1), v2 = Matches(data, MagicV2), v3 = Matches(data, Magic);
                if (!v1 && !v2 && !v3) return null;
                using (var ms = new MemoryStream(data, Magic.Length, data.Length - Magic.Length))
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                using (var r = new BinaryReader(z))
                {
                    stamp = r.ReadUInt32();
                    var j = new InkJournal { _nextId = r.ReadInt32() };
                    if (r.ReadBoolean())
                    {
                        if (v1)
                        {
                            int n = r.ReadInt32();
                            if (n < 0 || n > 400000000) return null;
                            j._basePng = r.ReadBytes(n);
                        }
                        else
                        {
                            if (basePng == null) return null;   // its picture is missing: not usable
                            j._basePng = basePng;
                        }
                    }
                    if (!v1) j.LayerSize = new Size(r.ReadInt32(), r.ReadInt32());
                    int count = r.ReadInt32();
                    if (count < 0 || count > 1000000) return null;
                    if (v3)
                    {
                        if (opRecords == null || opRecords.Count != count) return null;   // ops missing: not usable
                        for (int i = 0; i < count; i++) j.Ops.Add(InkOp.FromRecord64(opRecords[i]));
                        return j;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        byte tag = r.ReadByte();
                        int id = r.ReadInt32();
                        var b = new Rectangle(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                        InkOp op = InkOp.Read(r, tag);
                        op.Id = id; op.Bounds = b;
                        j.Ops.Add(op);
                    }
                    return j;
                }
            }
            catch { return null; }
        }

        private static bool Matches(byte[] data, string magic)
        {
            for (int i = 0; i < magic.Length; i++) if (data[i] != magic[i]) return false;
            return true;
        }

        // ── Helpers ───────────────────────────────────────────────────
        // A fingerprint of the layer's pixels (FNV-1a), meant to survive the PNG round
        // trip: alpha is exact there, and so is the colour of opaque pixels, but the
        // colour of translucent ones can move by a unit when the picture is decoded
        // (premultiplication) — so those count by their alpha alone.
        public static uint Stamp(Bitmap layer)
        {
            int w = layer.Width, h = layer.Height;
            var data = layer.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                uint hash = 2166136261u;
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        uint v = (uint)row[x], alpha = v >> 24;
                        hash = (hash ^ (alpha == 255 ? v : alpha << 24)) * 16777619u;
                    }
                }
                return hash;
            }
            finally { layer.UnlockBits(data); }
        }

        public static byte[] EncodePng(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        public static Bitmap DecodePng(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var src = new Bitmap(ms))
            {
                var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(copy))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                }
                return copy;
            }
        }
    }
}
