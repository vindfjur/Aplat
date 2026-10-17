using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml.Serialization;
using Aplat.Controls;
using Aplat.Models;

// The journal through Material: saved with the scene, loaded back, copied, cleared —
// and dropped (never trusted) when it no longer describes the picture next to it.
static class MaterialInkTests
{
    const int S = 400;
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    static StrokeOp Stroke(int argb, params float[] xy)
    {
        var pts = new List<PointF>();
        for (int i = 0; i < xy.Length; i += 2) pts.Add(new PointF(xy[i], xy[i + 1]));
        var arr = pts.ToArray();
        return new StrokeOp { Style = StrokeStyle.Brush, Radius = 10, Scale = 1f, Argb = argb, Opacity = 100, Seed = 1, Pts = arr,
                              Bounds = StrokeOp.BoundsFor(arr, 10, new Size(S, S)) };
    }

    static void Draw(Material m, InkOp op)
    {
        using (var g = Graphics.FromImage(m.PaintLayer)) { g.SetClip(op.Bounds); InkJournal.FreshState(g); op.Draw(g, op.Bounds); }
        m.Ink.Add(op);
        m.MarkPaintDirty();
    }

    static Material RoundTrip(Material m)
    {
        m.SyncPaintForSave();
        var xs = new XmlSerializer(typeof(Material));
        var sw = new StringWriter();
        xs.Serialize(sw, m);
        return (Material)xs.Deserialize(new StringReader(sw.ToString()));
    }

    static int Main()
    {
        var m = new Material();
        m.EnsurePaintLayer(S);
        m.EnsureInk();
        Draw(m, Stroke(Color.Red.ToArgb(), 50, 100, 350, 120));
        Draw(m, Stroke(Color.Blue.ToArgb(), 100, 50, 120, 350));
        var rect = new LiveItem { Kind = LiveKind.Rect, P1 = new PointF(200, 200), P2 = new PointF(330, 300), Color = Color.Green, StrokeWidth = 6 };
        Draw(m, new ItemOp { Item = rect, K = 1f, Bounds = rect.PaintBounds(S, S) });

        var back = RoundTrip(m);
        Check(!string.IsNullOrEmpty(m.InkBase64), "the journal is written with the scene");
        var layer2 = back.PaintLayer;
        Check(layer2 != null && layer2.Width == S, "the picture loads");
        Check(back.Ink != null && back.Ink.Ops.Count == 3, "the journal is restored, 3 ops (got " + (back.Ink == null ? -1 : back.Ink.Ops.Count) + ")");

        var red = back.Ink.HitTest(layer2, new PointF(200, 110), 4);
        Check(red is StrokeOp && ((StrokeOp)red).Argb == Color.Red.ToArgb(), "the loaded journal can be hit-tested");
        int before = layer2.GetPixel(200, 110).A;
        back.Ink.Remove(red, layer2);
        Check(layer2.GetPixel(200, 110).A == 0 && before > 0, "the red stroke leaves the loaded picture");
        Check(layer2.GetPixel(110, 200).A > 0, "the blue one stays");

        var cl = m.Clone();
        Check(cl.Ink != null && cl.Ink.Ops.Count == 3, "Material.Clone copies the journal");
        cl.Ink.Remove(cl.Ink.HitTest(cl.PaintLayer, new PointF(200, 110), 4), cl.PaintLayer);
        Check(m.Ink.Ops.Count == 3 && m.PaintLayer.GetPixel(200, 110).A > 0, "...and erasing in the copy leaves the original alone");

        // The picture was changed behind the journal's back (another program, an older build).
        m.SyncPaintForSave();
        var xs = new XmlSerializer(typeof(Material));
        var sw = new StringWriter();
        xs.Serialize(sw, m);
        var stale = (Material)xs.Deserialize(new StringReader(sw.ToString()));
        using (var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.FillEllipse(Brushes.Black, 10, 10, 50, 50);
            var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            stale.PaintBase64 = Convert.ToBase64String(ms.ToArray());
        }
        var l3 = stale.PaintLayer;
        Check(stale.Ink == null, "a journal that no longer matches the picture is dropped");
        var j = stale.EnsureInk();
        Check(j.HasBase && j.Ops.Count == 0, "the picture becomes the base of a fresh journal");
        Check(j.HitTest(l3, new PointF(30, 30), 5) == null, "the base is not erasable as an object");

        // A file from before the journal.
        var old = new Material();
        old.EnsurePaintLayer(S);
        using (var g = Graphics.FromImage(old.PaintLayer)) g.FillRectangle(Brushes.Orange, 100, 100, 50, 50);
        old.MarkPaintDirty();
        var old2 = RoundTrip(old);
        var ol = old2.PaintLayer;
        Check(old2.Ink == null, "a file without a journal loads without one");
        Check(old2.EnsureInk().HasBase, "the old picture is kept as the base");
        Draw(old2, Stroke(Color.Red.ToArgb(), 20, 20, 90, 90));
        old2.Ink.Remove(old2.Ink.HitTest(old2.PaintLayer, new PointF(50, 50), 4), old2.PaintLayer);
        Check(old2.PaintLayer.GetPixel(120, 120).ToArgb() == Color.Orange.ToArgb(), "erasing a new stroke keeps the old picture");

        // An undo snapshot leaves the picture out; the journal rebuilds the layer.
        {
            var snapM = new Material();
            snapM.EnsurePaintLayer(S);
            snapM.EnsureInk();
            Draw(snapM, Stroke(Color.Red.ToArgb(), 50, 100, 350, 120));
            var snapFill = new LiveItem { Kind = LiveKind.Ellipse, P1 = new PointF(120, 200), P2 = new PointF(300, 340), Color = Color.Green, StrokeWidth = 8, Fill = ShapeFill.Both };
            Draw(snapM, new ItemOp { Item = snapFill, K = 1f, Bounds = snapFill.PaintBounds(S, S) });
            Draw(snapM, Stroke(Color.Blue.ToArgb(), 60, 300, 340, 60));

            snapM.SyncPaintForSave(snapshot: true);
            var xs2 = new XmlSerializer(typeof(Material));
            string xml; var sw2 = new StringWriter();
            Material.SnapshotMode = true;
            try { xs2.Serialize(sw2, snapM); } finally { Material.SnapshotMode = false; }
            xml = sw2.ToString();
            Check(!xml.Contains("<PaintBase64>"), "a snapshot of a canvas with a journal holds no picture of it");
            Check(xml.Contains("<InkBase64>"), "...only its journal");
            Check(xml.Length < 6000, "...so it is small (" + xml.Length + " chars)");

            var restored = (Material)xs2.Deserialize(new StringReader(xml));
            Check(restored.HasPaint, "a restored snapshot reports that it has paint");
            var layerR = restored.PaintLayer;
            Check(layerR != null && layerR.Width == S, "the layer is rebuilt from the journal, at the right size");
            Check(restored.Ink != null && restored.Ink.Ops.Count == 3, "...with the journal intact");
            Check(restored.JournalMismatch() == 0, "...and it is the picture the journal describes");
            Check(snapM.JournalMismatch() == 0, "the live layer still matches its journal");
            int diff = 0;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                var p1 = snapM.PaintLayer.GetPixel(x, y); var p2 = layerR.GetPixel(x, y);
                if (Math.Abs(p1.A - p2.A) > 24) diff++;
            }
            Check(diff == 0, "the rebuilt layer equals the one it was taken from (" + diff + " px off)");

            // The file's own scene still carries the picture.
            snapM.SyncPaintForSave();
            var xs3 = new XmlSerializer(typeof(Material)); var sw3 = new StringWriter(); xs3.Serialize(sw3, snapM);
            Check(sw3.ToString().Contains("<PaintBase64>"), "a normal save still holds the picture");

            // A canvas with a legacy base: the base is stored once, apart from the journal.
            var legacyM = new Material(); legacyM.EnsurePaintLayer(S);
            using (var g = Graphics.FromImage(legacyM.PaintLayer)) g.FillRectangle(Brushes.Orange, 100, 100, 50, 50);
            legacyM.MarkPaintDirty(); legacyM.SyncPaintForSave();
            var lm = RoundTrip(legacyM); var ll = lm.PaintLayer; lm.EnsureInk();
            Draw(lm, Stroke(Color.Red.ToArgb(), 20, 20, 90, 90));
            lm.SyncPaintForSave(snapshot: true);
            Check(!string.IsNullOrEmpty(lm.InkBaseBase64), "the old picture is kept as a picture of its own (InkBaseBase64)");
            var sw4 = new StringWriter(); Material.SnapshotMode = true;
            try { xs2.Serialize(sw4, lm); } finally { Material.SnapshotMode = false; }
            var lr = (Material)xs2.Deserialize(new StringReader(sw4.ToString()));
            Check(lr.PaintLayer.GetPixel(120, 120).ToArgb() == Color.Orange.ToArgb(), "a snapshot rebuilds a canvas with an old base picture too");
        }

        // Undo / redo take the neighbouring state's layer and redraw only what differs.
        {
            var cur = new Material();
            cur.EnsurePaintLayer(S); cur.EnsureInk();
            var states = new System.Collections.Generic.List<string>();
            var xs5 = new XmlSerializer(typeof(Material));
            for (int i = 0; i < 12; i++)
            {
                Draw(cur, Stroke(i % 2 == 0 ? Color.Red.ToArgb() : Color.Blue.ToArgb(), 20 + i * 12, 30 + i * 20, 300, 60 + i * 25 + (i % 3) * 20));
                cur.SyncPaintForSave(snapshot: true);
                var w5 = new StringWriter(); Material.SnapshotMode = true;
                try { xs5.Serialize(w5, cur); } finally { Material.SnapshotMode = false; }
                states.Add(w5.ToString());
            }
            // from the newest state back to 5 strokes (undo), then forward to 9 (redo)
            Func<string, Material, Material> restore = (xml5, from) =>
            {
                var r = (Material)xs5.Deserialize(new StringReader(xml5));
                r.AdoptLayerFrom(from);
                return r;
            };
            var back5 = restore(states[4], cur);
            Check(back5.Ink.Ops.Count == 5 && back5.JournalMismatch() == 0, "undoing 7 strokes: the layer is the 5-stroke picture (adopted from the newer one)");
            var fresh5 = (Material)xs5.Deserialize(new StringReader(states[4]));
            int off = 0;
            for (int y = 0; y < S; y += 2) for (int x = 0; x < S; x += 2)
                if (Math.Abs(back5.PaintLayer.GetPixel(x, y).A - fresh5.PaintLayer.GetPixel(x, y).A) > 24) off++;
            Check(off == 0, "...identical to rebuilding that state from scratch (" + off + " px off)");
            var fwd9 = restore(states[8], back5);
            Check(fwd9.Ink.Ops.Count == 9 && fwd9.JournalMismatch() == 0, "redoing 4 strokes from there works the same way");
            var other = restore(states[2], new Material());   // nothing to adopt from: falls back to the full rebuild
            Check(other.Ink != null && other.Ink.Ops.Count == 3 && other.JournalMismatch() == 0, "with no neighbour to adopt from, the layer is rebuilt in full");
        }

        m.ClearPaint();
        Check(m.Ink == null && string.IsNullOrEmpty(m.InkBase64), "clearing the canvas clears the journal");

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
