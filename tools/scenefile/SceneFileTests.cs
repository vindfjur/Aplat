using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using Aplat.IO;
using Aplat.Models;

// The .aplat container (IO/SceneFileManager.cs, IO/BlobPacker.cs) without the UI:
// a scene with pictures and an undo history must round-trip exactly, the history's
// pictures must be stored once, the old XML format must still load, and a damaged or
// too-new file must be refused instead of half-loaded.
static class SceneFileTests
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    static string Png(int size, Color c, int seed)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, seed * 5, seed * 7, size / 2, size / 2);
            }
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return Convert.ToBase64String(ms.ToArray()); }
        }
    }

    static string Xml(Scene s)
    {
        var sb = new StringBuilder();
        using (var w = new StringWriter(sb)) new XmlSerializer(typeof(Scene)).Serialize(w, s);
        return sb.ToString();
    }

    static SceneObject Canvas(string paint, string tex)
    {
        return new SceneObject
        {
            Name = "Холст", PrimitiveType = "Canvas",
            Mesh = new Mesh(), Material = new Material { Color = Color.White, PaintBase64 = paint, TextureBase64 = tex },
        };
    }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aplat-scenefile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string texture = Png(300, Color.SaddleBrown, 1);
            string paintA = Png(600, Color.Red, 2), paintB = Png(600, Color.Blue, 3);

            // A scene whose undo history repeats the same texture and two paint layers.
            var scene = new Scene { Name = "t" };
            scene.Objects.Add(Canvas(paintB, texture));
            var history = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                var snap = new Scene { Name = "t" };
                snap.Objects.Add(Canvas(i % 2 == 0 ? paintA : paintB, texture));
                history.Add(Xml(snap));
            }
            scene.History = history;

            string legacy = Path.Combine(dir, "legacy.aplat");
            File.WriteAllText(legacy, Xml(scene), new UTF8Encoding(false));      // the old all-in-one XML
            string file = Path.Combine(dir, "scene.aplat");
            SceneFileManager.Save(scene, file);

            // 1. It is a container, smaller, with no stray temp file.
            using (var fs = File.OpenRead(file)) Check(fs.ReadByte() == 'P' && fs.ReadByte() == 'K', "the file is a zip container");
            long oldSize = new FileInfo(legacy).Length, newSize = new FileInfo(file).Length;
            Console.WriteLine("     old xml " + oldSize + " bytes -> container " + newSize + " bytes (" + (oldSize / Math.Max(1, newSize)) + "x smaller)");
            Check(newSize * 3 < oldSize, "the container is at least 3x smaller than the all-in-one XML");
            Check(!File.Exists(file + ".tmp"), "no temp file left behind");

            // 2. Pictures stored once, as raw PNG, however many snapshots use them.
            using (var zip = ZipFile.OpenRead(file))
            {
                var names = new List<string>();
                foreach (var e in zip.Entries) names.Add(e.FullName);
                Check(names[0] == "format", "the format marker is the first entry");
                int blobs = 0, snaps = 0;
                foreach (string n in names) { if (n.StartsWith("blobs/")) blobs++; if (n.StartsWith("history/")) snaps++; }
                Check(blobs == 3, "3 distinct pictures stored (texture + 2 paint layers), got " + blobs);
                Check(snaps == 6, "6 history snapshots stored, got " + snaps);
                var png = zip.GetEntry(names.Find(n => n.EndsWith(".png")));
                using (var s = png.Open()) { var h = new byte[4]; s.Read(h, 0, 4); Check(h[1] == 'P' && h[2] == 'N' && h[3] == 'G', "a blob is a real PNG, not base64"); }
            }

            // 3. Exact round trip.
            var back = SceneFileManager.Load(file);
            Check(back.Objects.Count == 1 && back.Objects[0].Material.PaintBase64 == paintB, "the paint layer comes back identical");
            Check(back.Objects[0].Material.TextureBase64 == texture, "the texture comes back identical");
            bool same = back.History != null && back.History.Count == history.Count;
            for (int i = 0; same && i < history.Count; i++) same = back.History[i] == history[i];
            Check(same, "every undo snapshot comes back byte for byte");
            Check(back.FilePath == file && !back.IsModified, "FilePath is set, the scene is not marked modified");

            // 4. Plain XML is not a scene file any more (the project has no older format to read).
            bool plainRefused = false;
            try { SceneFileManager.Load(legacy); } catch (InvalidDataException) { plainRefused = true; }
            Check(plainRefused, "a plain XML file is refused with a clear error");

            // 5. Saving over an existing file.
            scene.Name = "second save";
            SceneFileManager.Save(scene, file);
            Check(SceneFileManager.Load(file).Name == "second save" && !File.Exists(file + ".tmp"), "saving over an existing file works");

            // 5b. The canvas's journal: every op is stored once however many undo snapshots hold it.
            {
                var inkScene = new Scene { Name = "ink" };
                var mat = new Material { Color = Color.White };
                var canvas = new SceneObject { Name = "Холст", PrimitiveType = "Canvas", Mesh = new Mesh(), Material = mat };
                inkScene.Objects.Add(canvas);
                mat.EnsurePaintLayer(256);
                var ink = mat.EnsureInk();
                var snaps = new List<string>();
                const int Strokes = 12;
                for (int i = 0; i < Strokes; i++)
                {
                    var pts = new List<PointF>();
                    for (int k = 0; k < 40; k++) pts.Add(new PointF(10 + k * 5 + i, 20 + i * 15 + (k % 7)));
                    var arr = pts.ToArray();
                    ink.Add(new StrokeOp { Style = StrokeStyle.Brush, Radius = 6, Scale = 1f, Argb = Color.Red.ToArgb(), Opacity = 100, Seed = i, Pts = arr,
                                           Bounds = StrokeOp.BoundsFor(arr, 6, new Size(256, 256)) });
                    using (var g = Graphics.FromImage(mat.PaintLayer)) { var op = ink.Ops[ink.Ops.Count - 1]; InkJournal.FreshState(g); op.Draw(g, op.Bounds); }
                    mat.MarkPaintDirty();
                    mat.SyncPaintForSave(snapshot: true);
                    Material.SnapshotMode = true;
                    try { snaps.Add(Xml(inkScene)); } finally { Material.SnapshotMode = false; }
                }
                inkScene.History = snaps;
                string inkFile = Path.Combine(dir, "ink.aplat");
                SceneFileManager.Save(inkScene, inkFile);
                using (var zip = ZipFile.OpenRead(inkFile))
                {
                    int bins = 0;
                    foreach (var e in zip.Entries) if (e.FullName.EndsWith(".bin")) bins++;
                    Check(bins == Strokes, "each of the " + Strokes + " strokes is stored once across " + snaps.Count + " snapshots (" + bins + " op blobs)");
                }
                long total = 0; foreach (string sn in snaps) total += sn.Length;
                Console.WriteLine("     snapshots in memory: " + total + " chars; file: " + new FileInfo(inkFile).Length + " bytes");
                var inkBack = SceneFileManager.Load(inkFile);
                Check(inkBack.History.Count == Strokes, "the history comes back");
                var restored = (Scene)new XmlSerializer(typeof(Scene)).Deserialize(new StringReader(inkBack.History[5]));
                var rm = restored.Objects[0].Material;
                Check(rm.PaintLayer != null && rm.Ink != null && rm.Ink.Ops.Count == 6, "a restored snapshot rebuilds its canvas from the stored ops (6 strokes)");
                Check(rm.JournalMismatch() == 0, "...and the layer is what its journal describes");
                Check(inkBack.Objects[0].Material.Ink != null && inkBack.Objects[0].Material.Ink.Ops.Count == Strokes, "the saved scene itself has all its ops");
            }

            // 6. Damaged and too-new files are refused.
            string cut = Path.Combine(dir, "cut.aplat");
            var bytes = File.ReadAllBytes(file);
            File.WriteAllBytes(cut, AsSpanCopy(bytes, bytes.Length / 2));
            bool threw = false;
            try { SceneFileManager.Load(cut); } catch (Exception) { threw = true; }
            Check(threw, "a truncated file throws instead of loading half a scene");

            string newer = Path.Combine(dir, "newer.aplat");
            using (var zip = ZipFile.Open(newer, ZipArchiveMode.Create))
                using (var w = new StreamWriter(zip.CreateEntry("format").Open())) w.Write("aplat-scene 99");
            string msg = null;
            try { SceneFileManager.Load(newer); } catch (InvalidDataException e) { msg = e.Message; }
            Check(msg != null && msg.Contains("новой версией"), "a file from a newer version is refused with a clear message");

            string foreign = Path.Combine(dir, "foreign.aplat");
            using (var zip = ZipFile.Open(foreign, ZipArchiveMode.Create))
                using (var w = new StreamWriter(zip.CreateEntry("hello.txt").Open())) w.Write("hi");
            threw = false;
            try { SceneFileManager.Load(foreign); } catch (InvalidDataException) { threw = true; }
            Check(threw, "some other zip is not taken for a scene");

            // 7. Hostile files: no DTD / entity expansion, no oversized entries, no network texture paths.
            string dtd = Path.Combine(dir, "dtd.aplat");
            File.WriteAllText(dtd, "<?xml version=\"1.0\"?><!DOCTYPE Scene [<!ENTITY x \"boom\">]><Scene><Name>&x;</Name></Scene>");
            threw = false;
            try { SceneFileManager.Load(dtd); } catch (Exception) { threw = true; }
            Check(threw, "a scene with a DOCTYPE is refused");

            string bomb = Path.Combine(dir, "bomb.aplat");
            using (var zip = ZipFile.Open(bomb, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("format").Open())) w.Write("aplat-scene 2");
                using (var st = zip.CreateEntry("scene.xml", CompressionLevel.Optimal).Open())
                {
                    var chunk = new byte[1 << 20];
                    for (int i = 0; i < 600; i++) st.Write(chunk, 0, chunk.Length);   // 600 MB of zeros, a few hundred KB packed
                }
            }
            threw = false;
            try { SceneFileManager.Load(bomb); } catch (InvalidDataException) { threw = true; }
            Check(threw, "a decompression bomb is refused instead of filling memory");

            Check(!Material.IsLocalImagePath(@"\\evil.example\share\a.png"), "a UNC texture path is not followed");
            Check(!Material.IsLocalImagePath(@"\\?\C:\a.png") && !Material.IsLocalImagePath("//evil/a.png"), "device and // paths are not followed");
            Check(!Material.IsLocalImagePath(@"C:\a.png:stream") && !Material.IsLocalImagePath("http://evil/a.png"), "streams and URLs are not followed");
            Check(Material.IsLocalImagePath(@"C:\Users\me\a.png") && Material.IsLocalImagePath(@"Z:\pics\a.png"), "ordinary local and mapped-drive paths still work");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }

    // First `count` bytes (a stand-in for a torn write).
    static byte[] AsSpanCopy(byte[] a, int count)
    {
        var r = new byte[count];
        Array.Copy(a, r, count);
        return r;
    }
}
