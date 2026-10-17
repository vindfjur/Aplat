using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Aplat.Models;

namespace Aplat.IO
{
    // The .aplat scene file.
    //
    // Version 2 is a ZIP container (like .docx / .kra — rename it to .zip to look inside):
    //
    //   format            "aplat-scene 1", first so the file can be recognised and versioned
    //   scene.xml         the scene, readable, with its pictures replaced by references
    //   history/0000.xml  the undo snapshots, oldest first, the same way
    //   blobs/<sha1>.png  every picture (paint layer, texture) once, as the real PNG
    //   blobs/<sha1>.bin  the canvas's object journal
    //
    // A picture that appears in several snapshots is stored once (see BlobPacker), and
    // nothing is base64 any more — a typical file is several times smaller than the old
    // all-in-one XML, and saving is quicker.
    //
    // Saving never touches the existing file until the new one is complete and has been
    // read back: it is written beside it, checked, then swapped in. A crash or a full disk
    // mid-save leaves the previous version intact.
    public static class SceneFileManager
    {
        public const int FormatVersion = 1;
        private const string FormatName = "aplat-scene";

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        // ── Save ──────────────────────────────────────────────────────
        public static void Save(Scene scene, string path)
        {
            Save(scene, path, null);
        }

        // `undo` (optional) is the live undo history: its newest steps that fit UndoHistory's file
        // limit are written straight from its store. Without it, scene.History (steps as XML with the
        // pictures inline) is written.
        public static void Save(Scene scene, string path, UndoHistory undo)
        {
            scene.PrepareForSerialize();          // encode paint layers → base64

            // The history is stored as entries of its own, not escaped inside the XML.
            var history = scene.History;
            string sceneXml;
            scene.History = null;
            try { sceneXml = Serialize(scene); }
            finally { scene.History = history; }

            var blobs = new Dictionary<string, byte[]>();
            sceneXml = BlobPacker.Externalize(sceneXml, blobs);
            var snapshots = new List<string>();
            if (undo != null) snapshots = undo.ExportTo(blobs, UndoHistory.DefaultFileLimit);
            else if (history != null)
                foreach (string snap in history) snapshots.Add(BlobPacker.Externalize(snap, blobs));

            string temp = path + ".tmp";
            try
            {
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    WriteText(zip, "format", FormatName + " " + FormatVersion, CompressionLevel.NoCompression);
                    WriteText(zip, "scene.xml", sceneXml, CompressionLevel.Optimal);
                    for (int i = 0; i < snapshots.Count; i++)
                        WriteText(zip, "history/" + i.ToString("D4") + ".xml", snapshots[i], CompressionLevel.Optimal);
                    foreach (var blob in blobs)
                        WriteBytes(zip, "blobs/" + blob.Key, blob.Value,
                            blob.Key.EndsWith(".png") ? CompressionLevel.NoCompression : CompressionLevel.Optimal);   // a PNG is already deflated
                }
                Verify(temp);
                Commit(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* the original error is the one that matters */ }
                throw;
            }

            scene.FilePath   = path;
            scene.IsModified = false;
        }

        private static void WriteText(ZipArchive zip, string name, string text, CompressionLevel level)
        {
            WriteBytes(zip, name, Utf8.GetBytes(text), level);
        }

        private static void WriteBytes(ZipArchive zip, string name, byte[] bytes, CompressionLevel level)
        {
            var entry = zip.CreateEntry(name, level);
            using (var s = entry.Open()) s.Write(bytes, 0, bytes.Length);
        }

        // The file just written must read back before it replaces the good one.
        private static void Verify(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                if (ReadFormat(zip) != FormatVersion) throw new InvalidDataException(Loc.T("Файл записан неверно."));
                foreach (var e in zip.Entries)
                    using (var s = e.Open())
                    {
                        var sink = new byte[64 * 1024];
                        while (s.Read(sink, 0, sink.Length) > 0) { }   // decompresses fully and checks the CRC
                    }
            }
        }

        // Swap the finished file in. Replace is atomic where the platform has it.
        private static void Commit(string temp, string path)
        {
            if (!File.Exists(path)) { File.Move(temp, path); return; }
            try { File.Replace(temp, path, null); }
            catch (PlatformNotSupportedException) { File.Delete(path); File.Move(temp, path); }
        }

        // ── Load ──────────────────────────────────────────────────────
        public static Scene Load(string path)
        {
            return Load(path, null);
        }

        // With `undo`, the file's history goes straight into it (its pictures stay in the store,
        // not inlined); without, scene.History gets the steps as XML with the pictures inline.
        public static Scene Load(string path, UndoHistory undo)
        {
            if (!IsContainer(path)) throw new InvalidDataException(Loc.T("Это не файл сцены Aplat."));
            Scene scene = LoadContainer(path, undo);
            if (undo != null)
            {
                if (scene.History != null) foreach (string xml in scene.History) undo.Append(xml);
                scene.History = new List<string>();
                undo.Settle();
            }
            scene.FilePath   = path;
            scene.IsModified = false;
            return scene;
        }

        private static bool IsContainer(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var head = new byte[4];
                return fs.Read(head, 0, 4) == 4 && head[0] == (byte)'P' && head[1] == (byte)'K';
            }
        }

        // A scene file is untrusted input: no DTDs / entity expansion, no external resources.
        internal static Scene DeserializeScene(TextReader text)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            Scene scene;
            using (var xr = XmlReader.Create(text, settings))
                scene = (Scene)new XmlSerializer(typeof(Scene)).Deserialize(xr);
            if (scene.Objects == null) scene.Objects = new List<SceneObject>();
            scene.Objects.RemoveAll(o => o == null);
            foreach (var o in scene.Objects) Normalize(o, 0);
            return scene;
        }

        // The XML may say xsi:nil or omit parts, or nest groups absurdly deep; the rest of the
        // program assumes a mesh, a material and a child list always exist.
        private static Vec3 Finite(Vec3 v, Vec3 fallback)
        {
            float s = v.X + v.Y + v.Z;
            return float.IsNaN(s) || float.IsInfinity(s) ? fallback : v;
        }

        private static void Normalize(SceneObject o, int depth)
        {
            if (o == null) return;
            if (depth > 64) { o.Children = new List<SceneObject>(); return; }
            if (o.Mesh == null) o.Mesh = new Mesh();
            o.Mesh.Sanitize();
            o.Position = Finite(o.Position, Vec3.Zero);
            o.Rotation = Finite(o.Rotation, Vec3.Zero);
            o.Scale    = Finite(o.Scale, Vec3.One);
            if (o.Material == null) o.Material = new Material();
            if (o.Children == null) o.Children = new List<SceneObject>();
            o.Children.RemoveAll(c => c == null);
            foreach (var c in o.Children) Normalize(c, depth + 1);
        }

        private static Scene LoadContainer(string path, UndoHistory undo)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                int version = ReadFormat(zip);
                if (version > FormatVersion)
                    throw new InvalidDataException(Loc.T("Файл создан более новой версией Aplat (формат ") + version + ").");

                var cache = new Dictionary<string, string>();
                Func<string, byte[]> readBlob = name =>
                {
                    var e = zip.GetEntry("blobs/" + name);
                    return e == null ? null : ReadAll(e);
                };

                var sceneEntry = zip.GetEntry("scene.xml");
                if (sceneEntry == null) throw new InvalidDataException(Loc.T("В файле нет сцены."));
                string xml = BlobPacker.Inline(Utf8.GetString(ReadAll(sceneEntry)), readBlob, cache);
                var scene = DeserializeScene(new StringReader(xml));

                var names = new List<string>();
                foreach (var e in zip.Entries)
                    if (e.FullName.StartsWith("history/") && e.FullName.EndsWith(".xml")) names.Add(e.FullName);
                names.Sort(StringComparer.Ordinal);
                var history = new List<string>();
                foreach (string name in names)
                {
                    string raw = Utf8.GetString(ReadAll(zip.GetEntry(name)));
                    if (undo != null) undo.AppendReferenced(raw, readBlob);
                    else history.Add(BlobPacker.Inline(raw, readBlob, cache));
                }
                scene.History = history;
                return scene;
            }
        }

        private static int ReadFormat(ZipArchive zip)
        {
            var e = zip.GetEntry("format");
            if (e == null) throw new InvalidDataException(Loc.T("Это не файл сцены Aplat."));
            string[] parts = Utf8.GetString(ReadAll(e)).Trim().Split(' ');
            int v;
            if (parts.Length != 2 || parts[0] != FormatName || !int.TryParse(parts[1], out v))
                throw new InvalidDataException(Loc.T("Это не файл сцены Aplat."));
            return v;
        }

        // Limits against a hostile file (a few KB that inflate to gigabytes): the declared size is
        // checked first, then the bytes actually produced — the declared one can lie.
        private const long MaxEntryBytes = 512L << 20;

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            if (e.Length > MaxEntryBytes) throw new InvalidDataException(Loc.T("Файл повреждён или слишком велик: ") + e.FullName);
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > MaxEntryBytes) throw new InvalidDataException(Loc.T("Файл повреждён или слишком велик: ") + e.FullName);
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static string Serialize(Scene scene)
        {
            var sb = new StringBuilder();
            using (var w = new StringWriter(sb))
                new XmlSerializer(typeof(Scene)).Serialize(w, scene);
            return sb.ToString();
        }
    }
}
