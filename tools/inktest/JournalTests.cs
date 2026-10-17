using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Aplat.Controls;
using Aplat.Models;

// Checks the canvas's object journal (Models/InkJournal.cs, Models/InkOps.cs) without
// the UI: taking an object off the sheet must rebuild exactly the rest, hit-testing
// must follow what is visible, and the saved form must round-trip.
static class JournalTests
{
    const int W = 600, H = 600;
    static int fails;
    const int Tol = 24;   // alpha units; libgdiplus anti-aliases clipped edges a hair differently

    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    static int[] Px(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var a = new int[b.Width * b.Height];
        Marshal.Copy(d.Scan0, a, 0, a.Length);
        b.UnlockBits(d);
        return a;
    }

    static int Diff(Bitmap a, Bitmap b)
    {
        var x = Px(a); var y = Px(b); int n = 0;
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] == y[i]) continue;
            int da = Math.Abs(((x[i] >> 24) & 255) - ((y[i] >> 24) & 255));
            if (da > Tol) n++;
        }
        return n;
    }

    static StrokeOp Stroke(StrokeStyle st, int r, int argb, int seed, params float[] xy)
    {
        var pts = new List<PointF>();
        for (int i = 0; i < xy.Length; i += 2) pts.Add(new PointF(xy[i], xy[i + 1]));
        var arr = pts.ToArray();
        return new StrokeOp { Style = st, Radius = r, Scale = 1f, Argb = argb, Opacity = 100, Seed = seed, Pts = arr,
                              Bounds = StrokeOp.BoundsFor(arr, r, new Size(W, H)) };
    }

    static List<InkOp> Scene()
    {
        var ops = new List<InkOp>();
        ops.Add(Stroke(StrokeStyle.Brush, 14, Color.Red.ToArgb(), 1, 50, 100, 150, 130, 250, 90, 350, 140, 450, 100));
        ops.Add(Stroke(StrokeStyle.Pencil, 12, Color.Black.ToArgb(), 2, 60, 300, 200, 320, 330, 280));
        ops.Add(Stroke(StrokeStyle.Marker, 16, Color.Yellow.ToArgb(), 3, 100, 120, 300, 125, 480, 110));
        ops.Add(Stroke(StrokeStyle.Pen, 6, Color.Blue.ToArgb(), 4, 300, 50, 310, 250, 320, 450));
        var rect = new LiveItem { Kind = LiveKind.Rect, P1 = new PointF(80, 380), P2 = new PointF(260, 520),
                                  Color = Color.FromArgb(200, Color.Green), StrokeWidth = 8, Fill = ShapeFill.Both };
        ops.Add(new ItemOp { Item = rect, K = 1f, Bounds = rect.PaintBounds(W, H) });
        var txt = new LiveItem { Kind = LiveKind.Text, Text = "Hello\nWorld", FontFamily = "DejaVu Sans", FontPx = 60, Color = Color.Purple };
        txt.Normalize();
        txt.SetBox(new PointF(420, 450), txt.Box.Width, txt.Box.Height);
        ops.Add(new ItemOp { Item = txt, K = 1f, Bounds = txt.PaintBounds(W, H) });
        ops.Add(Stroke(StrokeStyle.Erase, 20, 0, 5, 200, 80, 220, 160));       // the pixel eraser nicks things
        var line = new LiveItem { Kind = LiveKind.Line, P1 = new PointF(20, 560), P2 = new PointF(580, 40), Color = Color.Orange, StrokeWidth = 5 };
        ops.Add(new ItemOp { Item = line, K = 1f, Bounds = line.PaintBounds(W, H) });
        return ops;
    }

    static Bitmap Render(IEnumerable<InkOp> ops, Bitmap basePic)
    {
        var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            var all = new Rectangle(0, 0, W, H);
            g.SetClip(all);
            if (basePic != null) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(basePic, all, all, GraphicsUnit.Pixel); g.CompositingMode = CompositingMode.SourceOver; }
            foreach (var op in ops) { InkJournal.FreshState(g); op.Draw(g, all); }
        }
        return b;
    }

    static InkJournal Journal(List<InkOp> ops, byte[] basePng)
    {
        var j = basePng != null ? InkJournal.WithBase(basePng) : new InkJournal();
        foreach (var o in ops) j.Add(o.Clone());
        return j;
    }

    static int Main()
    {
        var legacy = new Bitmap(W, H, PixelFormat.Format32bppArgb);   // a flat picture that predates the journal
        using (var g = Graphics.FromImage(legacy)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(Brushes.Gray, 200, 200, 250, 250); }
        byte[] legacyPng = InkJournal.EncodePng(legacy);

        // 0. Every op's Bounds must contain all the pixels it can change, or removal leaves ink behind.
        var all0 = Scene();
        for (int k = 0; k < all0.Count; k++)
        {
            var px = Px(Render(new List<InkOp> { all0[k] }, null));
            int outside = 0;
            for (int i = 0; i < px.Length; i++)
                if (((px[i] >> 24) & 255) != 0 && !all0[k].Bounds.Contains(i % W, i / W)) outside++;
            Check(outside == 0, "op " + k + " (" + all0[k].GetType().Name + ") paints only inside its Bounds (" + outside + " outside)");
        }

        foreach (bool withBase in new[] { false, true })
        {
            string tag = withBase ? "[with base] " : "[no base]   ";
            var ops = Scene();

            // 1. Removing an object rebuilds its footprint from the rest and touches nothing else.
            for (int k = 0; k < ops.Count; k++)
            {
                if (!ops[k].IsObject) continue;
                var jj = Journal(ops, withBase ? legacyPng : null);
                var lay = Render(jj.Ops, withBase ? legacy : null);
                var before = new Bitmap(lay);
                var rm = jj.Ops[k]; var area = rm.Bounds;
                jj.Remove(rm, lay);
                var expect = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                jj.Replay(expect, new Rectangle(0, 0, W, H));
                var pl = Px(lay); var pe = Px(expect); var pb = Px(before);
                int inside = 0, outside = 0;
                for (int i = 0; i < pl.Length; i++)
                {
                    if (area.Contains(i % W, i / W))
                    {
                        if (pl[i] != pe[i] && Math.Abs(((pl[i] >> 24) & 255) - ((pe[i] >> 24) & 255)) > Tol) inside++;
                    }
                    else if (pl[i] != pb[i]) outside++;
                }
                Check(inside == 0 && outside == 0, tag + "remove op " + k + " (" + ops[k].GetType().Name + "): inside = the rest (" + inside + " off), outside untouched (" + outside + " changed)");
            }

            // 2. Hit-testing follows what is drawn.
            var hj = Journal(ops, withBase ? legacyPng : null);
            var hl = Render(hj.Ops, withBase ? legacy : null);
            Check(hj.HitTest(hl, new PointF(150, 130), 6) is StrokeOp, tag + "a touch on a stroke finds a stroke");
            Check(hj.HitTest(hl, new PointF(5, 5), 6) == null, tag + "empty corner hits nothing");
            var hr = hj.HitTest(hl, new PointF(170, 450), 4);
            Check(hr is ItemOp && ((ItemOp)hr).Item.Kind == LiveKind.Rect, tag + "inside the tinted rectangle hits the rectangle");
            var ht = hj.HitTest(hl, new PointF(420, 440), 4);
            Check(ht is ItemOp && ((ItemOp)ht).Item.Kind == LiveKind.Text, tag + "on the text hits the text");

            // 3. Serialisation.
            uint stamp = InkJournal.Stamp(hl), st2;
            byte[] header = hj.SerializeHeader(stamp, new Size(W, H));
            var records = hj.OpRecords();
            var back = InkJournal.Deserialize(header, records, hj.BasePng, out st2);
            int recBytes = 0; foreach (var rec in records) recBytes += rec.Length;
            Check(back != null && st2 == stamp && back.Ops.Count == hj.Ops.Count && back.LayerSize == new Size(W, H), tag + "journal round-trips (header " + header.Length + " bytes, " + records.Count + " op records)");
            Check(InkJournal.Deserialize(header, records.GetRange(0, records.Count - 1), hj.BasePng, out st2) == null, tag + "a journal with an op missing is refused");
            Check(InkJournal.Deserialize(header, null, hj.BasePng, out st2) == null, tag + "a journal without its ops is refused");
            if (back != null) Check(Diff(hl, Render(back.Ops, withBase ? legacy : null)) == 0, tag + "the loaded journal replays to the same picture");
            Check(InkJournal.Deserialize(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, null, null, out st2) == null, tag + "garbage is refused, not thrown");
        }

        // 4. A fill: its runs replay exactly, it can be hit, and removing it brings back what it covered.
        {
            var mask = new byte[W * H]; var box = Rectangle.FromLTRB(100, 100, 300, 260);
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++)
                    if ((x - 200) * (x - 200) + (y - 180) * (y - 180) < 6000) mask[y * W + x] = 1;
            var fill = FillOp.FromMask(mask, W, H, box, Color.Teal.ToArgb());
            var j = Journal(new List<InkOp> { Stroke(StrokeStyle.Pen, 10, Color.Black.ToArgb(), 1, 120, 120, 280, 240), fill }, null);
            var lay = Render(j.Ops, null);
            Check(lay.GetPixel(200, 180).ToArgb() == Color.Teal.ToArgb(), "a fill paints its colour");
            Check(j.HitTest(lay, new PointF(200, 180), 3) == j.Ops[1], "a fill is hit inside its area");
            j.Remove(j.Ops[1], lay);
            Check(Diff(lay, Render(j.Ops, null)) == 0, "removing a fill returns what it covered");
        }

        // 5. The journal stays bounded: flattening the oldest keeps the picture.
        {
            var big = new InkJournal();
            for (int i = 0; i < InkJournal.KeepOps + 50; i++)
                big.Add(Stroke(StrokeStyle.Pen, 3, Color.Black.ToArgb(), i, 10 + (i % 500), 10 + (i * 7 % 500), 40 + (i % 500), 60 + (i * 5 % 500)));
            var full = Render(big.Ops, null);
            big.Bake(new Size(W, H));
            Check(big.Ops.Count == InkJournal.KeepOps && big.HasBase, "baking leaves KeepOps ops over a base");
            var lay = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            big.Replay(lay, new Rectangle(0, 0, W, H));
            Check(Diff(full, lay) == 0, "the picture after baking == the picture before");
        }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
