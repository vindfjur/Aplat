using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Aplat.IO
{
    // Moves the heavy payloads out of scene XML, git-style.
    //
    // A scene (and each undo snapshot) carries its pictures as base64 text inside the
    // XML: a paint layer, an embedded texture, the canvas's object journal. Base64 is a
    // third bigger than the bytes, does not compress, and every undo snapshot repeated
    // all of it. Here each such payload is stored once as raw bytes under the hash of
    // its content (a "blob"), and the XML keeps only a reference, "blob:<hash>.png".
    // The same picture in ten snapshots is one blob; a picture that did not change
    // between two saves costs nothing but its reference.
    internal static class BlobPacker
    {
        // <PaintBase64>…</PaintBase64>, <TextureBase64>…, <InkBase64>…, <TileBase64 x="3" y="2">… —
        // long base64 only; attributes (a tile's position) stay where they are.
        private const string Attrs = @"(?<attrs>(?:\s+[\w:.-]+=""[^""]*"")*)";

        private static readonly Regex Payload = new Regex(
            @"<(?<tag>\w*Base64)" + Attrs + @">(?<data>[A-Za-z0-9+/=\s]{64,})</\k<tag>>", RegexOptions.CultureInvariant);

        private static readonly Regex Reference = new Regex(
            @"<(?<tag>\w*Base64)" + Attrs + @">blob:(?<name>[0-9a-f]{40}\.(?:png|bin))</\k<tag>>", RegexOptions.CultureInvariant);

        // Replaces every payload in `xml` by a reference; the bytes go to `blobs` (name → bytes).
        public static string Externalize(string xml, Dictionary<string, byte[]> blobs)
        {
            return Externalize(xml, blobs, null);
        }

        // `names` (optional) gets the name of every blob the XML now refers to, once each.
        public static string Externalize(string xml, Dictionary<string, byte[]> blobs, List<string> names)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            var seen = names != null ? new HashSet<string>() : null;
            return Payload.Replace(xml, m =>
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(m.Groups["data"].Value); }
                catch (FormatException) { return m.Value; }   // not really base64: leave it as it is
                string name = NameOf(bytes);
                if (!blobs.ContainsKey(name)) blobs[name] = bytes;
                if (seen != null && seen.Add(name)) names.Add(name);
                string tag = m.Groups["tag"].Value;
                return "<" + tag + m.Groups["attrs"].Value + ">blob:" + name + "</" + tag + ">";
            });
        }

        // The names of the blobs `xml` refers to, each once.
        public static List<string> References(string xml)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(xml)) return names;
            var seen = new HashSet<string>();
            foreach (Match m in Reference.Matches(xml))
                if (seen.Add(m.Groups["name"].Value)) names.Add(m.Groups["name"].Value);
            return names;
        }

        // The reverse: every reference becomes the base64 text the rest of the program expects.
        // `read` returns a blob's bytes (null if it is missing); equal blobs share one string.
        public static string Inline(string xml, Func<string, byte[]> read, Dictionary<string, string> cache)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            return Reference.Replace(xml, m =>
            {
                string name = m.Groups["name"].Value, text;
                if (!cache.TryGetValue(name, out text))
                {
                    byte[] bytes = read(name);
                    if (bytes == null) throw new System.IO.InvalidDataException(Loc.T("В файле не хватает данных: ") + name);
                    text = Convert.ToBase64String(bytes);
                    cache[name] = text;
                }
                string tag = m.Groups["tag"].Value;
                return "<" + tag + m.Groups["attrs"].Value + ">" + text + "</" + tag + ">";
            });
        }

        // Content hash + a hint of the kind (a PNG opens in any viewer once unzipped).
        private static string NameOf(byte[] bytes)
        {
            bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder(44);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.Append(png ? ".png" : ".bin").ToString();
            }
        }
    }
}
