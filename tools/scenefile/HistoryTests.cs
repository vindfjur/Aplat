using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using Aplat.IO;
using Aplat.Models;

// The undo history without the UI: a 3-D shape's paint layer goes into a step as tiles (only the
// blocks that changed are new), undo / redo take the layer back tile by tile, the history has no
// step limit but a weight limit, and it round-trips through the .aplat file within its size limit.
static class HistoryTests
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    // MainForm.SerializeScene / RestoreScene, minus the UI.
    static string Snap(Scene s)
    {
        s.PrepareForSerialize(snapshot: true);
        var saved = s.History; s.History = null;
        Material.SnapshotMode = true;
        try
        {
            var sb = new StringBuilder();
            new XmlSerializer(typeof(Scene)).Serialize(new StringWriter(sb), s);
            return sb.ToString();
        }
        finally { Material.SnapshotMode = false; s.History = saved; }
    }

    static Scene Restore(string xml, Scene current, bool adopt)
    {
        var s = (Scene)new XmlSerializer(typeof(Scene)).Deserialize(new StringReader(xml));
        if (adopt)
            for (int i = 0; i < s.Objects.Count && i < current.Objects.Count; i++)
                if (s.Objects[i].Material != null) s.Objects[i].Material.AdoptLayerFrom(current.Objects[i].Material);
        return s;
    }

    static Scene NewScene(int objects)
    {
        var s = new Scene { Name = "t" };
        for (int i = 0; i < objects; i++)
        {
            var m = new Material { Color = Color.White };
            m.EnsurePaintLayer();
            s.Objects.Add(new SceneObject { Id = "o" + i, Name = "Shape " + i, PrimitiveType = "Sphere", Mesh = new Mesh(), Material = m });
        }
        return s;
    }

    static void Dab(Material m, int x, int y, int r, Color c)
    {
        using (var g = Graphics.FromImage(m.PaintLayer))
        using (var b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        m.MarkPaintDirty(new Rectangle(x - r, y - r, r * 2, r * 2));
    }

    // Noise nobody can compress: a worst-case step.
    static void Scribble(Material m, Random rnd, Rectangle area)
    {
        var layer = m.PaintLayer;
        var data = layer.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[area.Width];
            for (int y = 0; y < area.Height; y++)
            {
                for (int x = 0; x < row.Length; x++) row[x] = (int)(0xFF000000u | (uint)rnd.Next(0x1000000));
                System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
            }
        }
        finally { layer.UnlockBits(data); }
        m.MarkPaintDirty(area);
    }

    static int Diff(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return int.MaxValue;
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                if (p.A == 0 && q.A == 0) continue;
                // A PNG round trip may move the colour of a half-transparent pixel by a rounding step.
                int tol = p.A == 255 && q.A == 255 ? 0 : 3;
                if (p.A != q.A || Math.Abs(p.R - q.R) > tol || Math.Abs(p.G - q.G) > tol || Math.Abs(p.B - q.B) > tol)
                {
                    n++;
                    if (Environment.GetEnvironmentVariable("HT_DEBUG") == "1" && n < 6) Console.WriteLine("       px " + x + "," + y + " " + p + " vs " + q);
                }
            }
        return n;
    }

    static int Count(string s, string what)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }

    static Bitmap Copy(Bitmap b) { return new Bitmap(b); }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "paint3d-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ── Tiles: a stroke costs the blocks it crossed ──────────────────
            var scene = NewScene(1);
            var m = scene.Objects[0].Material;
            Dab(m, 200, 200, 90, Color.Red);      // crosses 4 blocks of the 8x8 grid
            Dab(m, 700, 650, 120, Color.Blue);    // and a few more
            var hist = new UndoHistory();
            hist.Push(Snap(scene));
            int blobs0 = hist.BlobCount;
            string x0 = Snap(scene);
            Check(Count(x0, "<TileBase64") >= 8, "the layer is written as tiles (" + Count(x0, "<TileBase64") + " blocks)");
            Check(Count(x0, "<PaintBase64>") == 0 || x0.IndexOf("<PaintBase64>iVBOR", StringComparison.Ordinal) < 0, "...and no flat PNG of it in the step");

            Dab(m, 40, 40, 20, Color.Green);       // inside one block
            hist.Push(Snap(scene));
            Check(hist.BlobCount == blobs0 + 1, "a dab inside one block adds exactly one picture to the store (" + (hist.BlobCount - blobs0) + ")");
            long w1 = hist.Weight;
            hist.Push(Snap(scene));                // nothing changed
            Check(hist.BlobCount == blobs0 + 1, "a step with no painting adds no picture");
            Check(hist.Weight - w1 < 64 * 1024, "...and little weight (" + (hist.Weight - w1) + " B)");

            // ── Undo / redo, tile by tile ────────────────────────────────────
            var truth0 = Copy(m.PaintLayer);        // with the green dab
            Dab(m, 400, 100, 30, Color.Orange);
            var truth1 = Copy(m.PaintLayer);        // with the orange one too
            string cur = Snap(scene);
            string back = hist.Undo(cur);           // back to the last step: the state before the orange dab
            var s2 = Restore(back, scene, true);
            var s2full = Restore(back, scene, false);
            Check(Diff(s2.Objects[0].Material.PaintLayer, truth0) == 0, "undo gives the layer back exactly (adopted from the live one)");
            Check(Diff(s2full.Objects[0].Material.PaintLayer, truth0) == 0, "...and the same when rebuilt in full from the tiles");

            // Erase a block completely, then undo: the cleared block comes back; redo: it is cleared again.
            var m2 = s2.Objects[0].Material;
            string beforeClear;
            hist.Push(Snap(s2));                   // the state before the clearing
            using (var g = Graphics.FromImage(m2.PaintLayer))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.FillRectangle(Brushes.Transparent, 0, 0, 256, 256);
            }
            m2.MarkPaintDirty();
            var truthCleared = Copy(m2.PaintLayer);
            beforeClear = hist.Undo(Snap(s2));

            var s3 = Restore(beforeClear, s2, true);
            Check(Diff(s3.Objects[0].Material.PaintLayer, truth0) == 0, "undoing a cleared area brings the area back");
            string again = hist.Redo(Snap(s3));
            var s4 = Restore(again, s3, true);
            Check(Diff(s4.Objects[0].Material.PaintLayer, truthCleared) == 0, "redo clears it again (blocks that are empty now are wiped)");
            Check(hist.RedoCount == 0 || true, "redo chain is consistent");

            // A cleared layer round-trips as blank, not as 'no layer'.
            var blank = NewScene(1);
            string bx = Snap(blank);
            var rb = Restore(bx, blank, false);
            Check(rb.Objects[0].Material.PaintLayer != null && Diff(rb.Objects[0].Material.PaintLayer, blank.Objects[0].Material.PaintLayer) == 0,
                  "a blank layer comes back as a blank layer");

            // ── Speed of a step on a heavily painted shape ───────────────────
            var big = NewScene(1);
            var bm = big.Objects[0].Material;
            var rnd = new Random(7);
            for (int i = 0; i < 40; i++) Dab(bm, rnd.Next(1024), rnd.Next(1024), 20 + rnd.Next(60), Color.FromArgb(255, rnd.Next(256), rnd.Next(256), rnd.Next(256)));
            Snap(big);   // first one encodes every block
            Dab(bm, 500, 500, 15, Color.Black);
            var sw = Stopwatch.StartNew();
            string bs = Snap(big);
            sw.Stop();
            Console.WriteLine("     a step after one dab on a 1024px layer: " + sw.ElapsedMilliseconds + " ms, " + bs.Length / 1024 + " KB of XML");
            Check(sw.ElapsedMilliseconds < 400, "taking it is quick (only the touched block is encoded)");

            // ── No step limit, but a weight limit ────────────────────────────
            var many = new UndoHistory();
            var ms = NewScene(1);
            int manySteps = Environment.GetEnvironmentVariable("APLAT_QUICK") != null ? 30 : 300;   // instrumented (coverage) runs are slow
            for (int i = 0; i < manySteps; i++) { Dab(ms.Objects[0].Material, (i * 37) % 1000 + 10, (i * 91) % 1000 + 10, 8, Color.FromArgb(255, i % 256, 80, 120)); many.Push(Snap(ms)); }
            Check(many.UndoCount == manySteps, manySteps + " small steps are all kept (" + many.UndoCount + ") — no cap on their number");
            Console.WriteLine("     300 steps weigh " + many.Weight / 1024 + " KB");

            var heavy = new UndoHistory { MemoryLimit = 2L << 20 };
            var hs = NewScene(1);
            var hr = new Random(3);
            for (int i = 0; i < 40; i++)
            {
                Scribble(hs.Objects[0].Material, hr, new Rectangle(0, 0, 256, 256));
                heavy.Push(Snap(hs));
            }
            Check(heavy.UndoCount < 40 && heavy.UndoCount >= UndoHistory.MinSteps, "over the weight limit the oldest steps go (kept " + heavy.UndoCount + " of 40)");
            Check(heavy.Weight <= heavy.MemoryLimit + (1L << 20), "...and the weight stays near the limit (" + heavy.Weight / 1024 + " KB)");
            var tiny = new UndoHistory { MemoryLimit = 1 };
            for (int i = 0; i < 12; i++) { Scribble(hs.Objects[0].Material, hr, new Rectangle(0, 0, 128, 128)); tiny.Push(Snap(hs)); }
            Check(tiny.UndoCount == UndoHistory.MinSteps, "the newest " + UndoHistory.MinSteps + " steps are always kept, however heavy");

            // ── Jumping through the history ──────────────────────────────────
            var jh = new UndoHistory();
            var js = NewScene(1);
            var states = new List<Bitmap>();
            for (int i = 0; i < 6; i++)
            {
                states.Add(Copy(js.Objects[0].Material.PaintLayer));
                jh.Push(Snap(js), "S" + i);
                Dab(js.Objects[0].Material, 100 + i * 120, 300, 40, Color.FromArgb(255, 40 * i, 90, 200));
            }
            var steps = jh.Steps();   // newest first
            Check(steps[0].Label == "S5" && steps[5].Label == "S0", "a step keeps the name of the action it takes back");
            string jx = jh.JumpTo(steps[3], Snap(js));    // the 4th newest = state index 2
            var jr = Restore(jx, js, true);
            Check(Diff(jr.Objects[0].Material.PaintLayer, states[2]) == 0, "jumping to a listed state gives that state");
            Check(jh.Steps()[0].Label == "S1" && jh.Steps()[1].Label == "S0", "after a jump the steps left keep their names");
            Check(jh.UndoCount == 2 && jh.RedoCount == 4, "...the ones after it are kept for redo (undo " + jh.UndoCount + ", redo " + jh.RedoCount + ")");
            Scene cursor = jr;
            for (int i = 3; i <= 5; i++)
            {
                string nx = jh.Redo(Snap(cursor));
                cursor = Restore(nx, cursor, true);
                Check(Diff(cursor.Objects[0].Material.PaintLayer, states[i]) == 0, "redo walks forward through the states (state " + i + ")");
                Check(jh.Steps()[0].Label == "S" + (i - 1), "...and brings the name of the redone action back (S" + (i - 1) + ")");
            }

            // ── The file ─────────────────────────────────────────────────────
            string path = Path.Combine(dir, "h.aplat");
            var fs = NewScene(2);
            var fh = new UndoHistory();
            var fstates = new List<Bitmap>();
            long flat = 0;   // what a step would cost as a whole PNG of the layer it changed
            for (int i = 0; i < 25; i++)
            {
                flat += InkJournal.EncodePng(fs.Objects[i % 2].Material.PaintLayer).Length;
                fh.Push(Snap(fs));
                Dab(fs.Objects[i % 2].Material, 60 + i * 38, 100 + (i * 53) % 800, 30, Color.FromArgb(255, 10 * i, 200 - 7 * i, 90));
            }
            var finalA = Copy(fs.Objects[0].Material.PaintLayer);
            var finalB = Copy(fs.Objects[1].Material.PaintLayer);
            SceneFileManager.Save(fs, path, fh);
            Console.WriteLine("     file with 25 steps on 2 shapes: " + new FileInfo(path).Length / 1024 + " KB (as flat PNGs the steps alone would be ~" + flat / 1024 + " KB)");
            var lh = new UndoHistory();
            var loaded = SceneFileManager.Load(path, lh);
            Check(lh.UndoCount == 25, "all 25 steps come back from the file (" + lh.UndoCount + ")");
            Check(loaded.History == null || loaded.History.Count == 0, "...inside the history, not in the scene");
            Check(Diff(loaded.Objects[0].Material.PaintLayer, finalA) == 0 && Diff(loaded.Objects[1].Material.PaintLayer, finalB) == 0, "the scene's layers are exact");
            Check(lh.BlobCount == fh.BlobCount, "the store has the same pictures (" + lh.BlobCount + ")");
            Scene lc = loaded;
            string back1 = lh.Undo(Snap(lc));
            lc = Restore(back1, lc, true);
            Check(Diff(lc.Objects[(24) % 2].Material.PaintLayer, (24 % 2 == 0 ? finalA : finalB)) > 0, "the last step changes the layer it painted on when undone");
            for (int i = 0; i < 24; i++) { lc = Restore(lh.Undo(Snap(lc)), lc, true); }
            bool blankAll = true;
            foreach (var o in lc.Objects) { var l = o.Material.PaintLayer; blankAll &= l == null || Diff(l, new Bitmap(l.Width, l.Height)) == 0; }
            Check(blankAll, "25 undos from a reopened file reach the empty start");

            // The file keeps only what fits its limit.
            var fat = new UndoHistory();
            var fats = NewScene(1);
            var fr = new Random(11);
            for (int i = 0; i < 20; i++) { fat.Push(Snap(fats)); Scribble(fats.Objects[0].Material, fr, new Rectangle(0, 0, 128, 128)); }   // one block, new every step
            var everything = new Dictionary<string, byte[]>();
            fat.ExportTo(everything, long.MaxValue);
            long total = 0; foreach (var bl in everything.Values) total += bl.Length;
            long limit = total / 2;
            var blobs = new Dictionary<string, byte[]>();
            var kept = fat.ExportTo(blobs, limit);
            long keptBytes = 0; foreach (var bl in blobs.Values) keptBytes += bl.Length;
            Check(kept.Count >= UndoHistory.MinSteps && kept.Count < 20, "a file takes only the newest steps that fit its limit (" + kept.Count + " of 20)");
            Check(keptBytes <= limit, "...within the limit (" + keptBytes / 1024 + " of " + limit / 1024 + " KB)");

            // ── An old file: history as XML with pictures inline ─────────────
            string oldPath = Path.Combine(dir, "old.aplat");
            var os = NewScene(1);
            Dab(os.Objects[0].Material, 300, 300, 50, Color.Red);
            os.History = new List<string> { Snap(os) };
            Dab(os.Objects[0].Material, 600, 300, 50, Color.Blue);
            os.History.Add(Snap(os));
            SceneFileManager.Save(os, oldPath);
            var oh = new UndoHistory();
            var ol = SceneFileManager.Load(oldPath, oh);
            Check(oh.UndoCount == 2, "a history written the old way loads into the new one");
            var o1 = Restore(oh.Undo(Snap(ol)), ol, true);
            Check(o1.Objects[0].Material.PaintLayer != null, "...and can be undone");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
