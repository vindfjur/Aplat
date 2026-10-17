using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml.Serialization;

namespace Aplat.Models
{
    public enum TextureAlphaMode { Blend, Opaque, Cutout }

    public enum MaterialMode { Solid, Wireframe, Checkerboard }

    [Serializable]
    public class Material
    {
        public const int PaintSize = 1024;        // paint-atlas resolution of 3-D shapes
        public const int CanvasPaintSize = 2048;  // the drawing sheet: crisp strokes when zoomed in

        public int ColorArgb { get; set; }
        public MaterialMode Mode { get; set; }
        private string _texturePath;
        public string TexturePath
        {
            get { return _texturePath; }
            set { if (_texturePath != value) { _texturePath = value; TextureBase64 = null; } }
        }
        // Embedded PNG travels with .aplat / undo / copies, including on another PC.
        public string TextureBase64 { get; set; }
        public TextureAlphaMode AlphaMode { get; set; }
        public float AlphaCutoff { get; set; } = 0.5f;
        public bool PixelTexture { get; set; }
        public bool TransmitLight { get; set; } = true;
        public bool LightEnabled { get; set; }
        public float LightPower { get; set; } = 1.5f;
        public float LightRange { get; set; } = 4f;
        public bool LightFlicker { get; set; }
        public int? LocalLightArgb { get; set; }
        [XmlIgnore]
        public Color LightColor
        {
            get { return Color.FromArgb(LocalLightArgb ?? (EmissionArgb == Color.Black.ToArgb() ? Color.White.ToArgb() : EmissionArgb)); }
            set { LocalLightArgb = value.ToArgb(); }
        }
        [XmlIgnore] public string TextureKey { get { return !string.IsNullOrEmpty(TextureBase64) ? TextureBase64 : TexturePath; } }
        [XmlIgnore] public bool IsTransparent { get { return Opacity < 0.995f || (AlphaMode == TextureAlphaMode.Blend && HasTexture && TextureHasAlpha); } }
        private bool TextureHasAlpha
        {
            get { var image = Texture; return image != null && _textureHasAlpha; }
        }
        private bool _textureHasAlpha;
        private string _loadedData;
        private bool _textureAttempted;
        public bool NoTile { get; set; }   // fit the image once instead of tiling

        // ── PBR ──────────────────────────────────────────────────
        // Metalness / roughness in the usual metal-roughness sense; the renderer turns
        // them into physically plausible lighting parameters (diffuse, specular colour
        // and highlight sharpness). Old files without these elements get the defaults
        // from the constructor, so they look exactly as before.
        public float Metallic         { get; set; }   // 0 — dielectric, 1 — metal
        public float Roughness        { get; set; }   // 0 — mirror, 1 — matte
        public float Opacity          { get; set; }   // 0..1, object opacity
        public int   EmissionArgb     { get; set; }   // colour of the self-glow
        public float EmissionStrength { get; set; }   // 0..1, glow strength
        public string AoTexturePath   { get; set; }   // ambient-occlusion (AO) map, 2nd layer

        // Per-object paint layer (projection brush). Stored as PNG base64 in the
        // .aplat; decoded lazily into PaintLayer and re-encoded only when dirty.
        public string PaintBase64 { get; set; }

        // The drawing sheet as a list of objects (see InkJournal), saved next to the
        // flat PNG above. Only canvases have one; null / empty elsewhere.
        public string InkBase64 { get; set; }

        // The journal's pre-journal picture (an old file's flat drawing) as PNG, kept apart from
        // the journal so every saved state that shares it stores it once (see IO/BlobPacker).
        public string InkBaseBase64 { get; set; }

        // The journal's ops, one text per op, in order (InkBase64 is only its header). Each op is
        // stored once in a file however many undo snapshots contain it. Null when there is none.
        [XmlArray("InkOps"), XmlArrayItem("InkOpBase64")]
        public System.Collections.Generic.List<string> InkOps { get; set; }

        // While an undo snapshot is serialised the canvas layer is left out of it when its
        // journal can rebuild it: the snapshot then costs the few KB of strokes since the last
        // one instead of a whole PNG per step. The file's own scene keeps the picture.
        [XmlIgnore] public static bool SnapshotMode;
        public bool ShouldSerializePaintBase64() { return !(SnapshotMode && (JournalRebuildsLayer || TilesRebuildLayer)); }

        // The layer of a 3-D shape (no journal) in an undo snapshot: the 128 px blocks that hold
        // anything, each a PNG (see PaintTile). A block untouched since the previous step is the
        // same text in both, so a step costs the blocks the stroke crossed, not the whole sheet.
        // Only present in snapshots; PaintTilesLayer is the layer's side in px (0 = no tiles).
        [XmlArray("PaintTiles"), XmlArrayItem("TileBase64")]
        public List<PaintTile> PaintTiles { get; set; }
        public int PaintTilesLayer { get; set; }
        public bool ShouldSerializePaintTiles() { return SnapshotMode && TilesRebuildLayer && !JournalRebuildsLayer; }
        public bool ShouldSerializePaintTilesLayer() { return ShouldSerializePaintTiles(); }

        private bool TilesRebuildLayer
        {
            get { return PaintTilesLayer > 0 && PaintTiles != null; }
        }

        // What the tiles of the live layer looked like when they were last written down: the
        // fingerprint of each block's pixels and its PNG text. Tiles are re-encoded only where
        // the fingerprint moved.
        private sealed class TileState { public ulong Hash; public string Data; }
        private Dictionary<int, TileState> _tileCache;
        private int _tilesSyncedVersion = -1;

        private bool JournalRebuildsLayer
        {
            get { return _ink != null && !_ink.IsEmpty && !string.IsNullOrEmpty(InkBase64); }
        }

        private Bitmap _texture;
        private string _loadedPath;

        private Bitmap _aoTexture;
        private string _loadedAoPath;

        private Bitmap _paintLayer;
        private bool   _paintDecoded;
        private bool   _paintDirty;

        // Object journal of the canvas layer, and the bookkeeping that keeps its saved
        // form tied to the pixels it describes (a fingerprint of the layer).
        private InkJournal _ink;
        private uint _paintStamp;
        private bool _paintStampValid;
        private int  _inkSavedRev = -1;
        private uint _inkSavedStamp;

        public Material()
        {
            ColorArgb = Color.DodgerBlue.ToArgb();
            Mode = MaterialMode.Solid;
            TexturePath = "";
            PaintBase64 = "";

            Metallic         = 0f;
            Roughness        = 0.55f;
            Opacity          = 1f;
            EmissionArgb     = Color.Black.ToArgb();
            EmissionStrength = 0f;
            AoTexturePath    = "";
        }

        [XmlIgnore]
        public Color Color
        {
            get { return Color.FromArgb(ColorArgb); }
            set { ColorArgb = value.ToArgb(); }
        }

        [XmlIgnore]
        public bool HasTexture
        {
            get { return !string.IsNullOrEmpty(TexturePath) || !string.IsNullOrEmpty(TextureBase64); }
        }

        [XmlIgnore]
        public Bitmap Texture
        {
            get
            {
                if (!HasTexture) return null;
                if (_textureAttempted && _loadedPath == TexturePath && _loadedData == TextureBase64)
                    return _texture;
                _textureAttempted = true;
                _loadedPath = TexturePath;
                _loadedData = TextureBase64;
                try
                {
                    Bitmap next;
                    if (!string.IsNullOrEmpty(TextureBase64))
                    {
                        using (var ms = new MemoryStream(Convert.FromBase64String(TextureBase64)))
                        using (var src = new Bitmap(ms)) next = new Bitmap(src);
                    }
                    else next = LoadBitmap(TexturePath);
                    _texture?.Dispose();
                    _texture = next;
                    _loadedPath = TexturePath;
                    _loadedData = TextureBase64;
                    _textureHasAlpha = false;
                    for (int y = 0; y < next.Height && !_textureHasAlpha; y++)
                        for (int x = 0; x < next.Width; x++)
                            if (next.GetPixel(x, y).A < 255) { _textureHasAlpha = true; break; }
                }
                catch { _texture = null; }

                return _texture;
            }
        }

        private const long MaxImageFileBytes = 256L << 20;

        // A .aplat can carry any text in TexturePath / AoTexturePath. Opening a scene must never make
        // Windows reach out to another machine (a \\host\share path makes it send NTLM credentials),
        // nor open devices or alternate streams, so only plain local / mapped-drive paths pass.
        public static bool IsLocalImagePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 260) return false;
            if (path.StartsWith("\\\\") || path.StartsWith("//")) return false;   // UNC, \\?\, \\.\
            if (path.IndexOf(':', path.Length > 1 && path[1] == ':' ? 2 : 0) >= 0) return false;   // URL, stream
            return Path.IsPathRooted(path);
        }

        // Robustly load an image into a standalone bitmap. Reading the whole file
        // into memory first avoids GDI+'s exclusive file lock and forces cloud-synced
        // placeholders (iCloud/OneDrive) to download, which `new Bitmap(path)` does
        // not. The result is copied into a 32-bpp bitmap detached from the stream so
        // it can outlive it. Throws on a genuinely unreadable file.
        public static Bitmap LoadBitmap(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!IsLocalImagePath(path)) throw new IOException(Loc.T("Изображения по сетевым путям не загружаются: ") + path);
            if (new FileInfo(path).Length > MaxImageFileBytes) throw new IOException(Loc.T("Файл изображения слишком большой."));
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var src = new Bitmap(ms))
            {
                var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(copy))
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
                return copy;
            }
        }

        [XmlIgnore]
        public Color EmissionColor
        {
            get { return Color.FromArgb(EmissionArgb); }
            set
            {
                // Preserve the light colour of older scenes before editing surface emission.
                if (LightEnabled && !LocalLightArgb.HasValue) LocalLightArgb = LightColor.ToArgb();
                EmissionArgb = value.ToArgb();
            }
        }

        [XmlIgnore]
        public bool HasAoTexture
        {
            get { return !string.IsNullOrEmpty(AoTexturePath); }
        }

        // Ambient-occlusion map. Loaded lazily like the main texture, and likewise
        // survives an unavailable file (returns null).
        [XmlIgnore]
        public Bitmap AoTexture
        {
            get
            {
                if (string.IsNullOrEmpty(AoTexturePath))
                {
                    _aoTexture = null;
                    _loadedAoPath = null;
                    return null;
                }
                if (_aoTexture != null && _loadedAoPath == AoTexturePath)
                    return _aoTexture;
                try
                {
                    _aoTexture = LoadBitmap(AoTexturePath);
                    _loadedAoPath = _aoTexture != null ? AoTexturePath : null;
                }
                catch
                {
                    _aoTexture = null;
                    _loadedAoPath = null;
                }
                return _aoTexture;
            }
        }

        // Ready-made PBR presets — a quick way to get a recognisable material.
        // The names are stable ids (the Russian source text, also the switch below): never run
        // them through Loc.T here — in the English UI that would turn them into English and no
        // preset would match, i.e. choosing one would do nothing. The UI translates for display.
        public static readonly string[] PresetNames = "Пластик|Глянцевый лак|Металл|Матовый металл|Хром|Золото|Дерево|Резина|Керамика|Неон|Стекло|Вырезы PNG".Split('|');

        // The preset these settings amount to, or null for a custom material. A preset that
        // sets a colour (gold, glass) counts only while the colour is still that one.
        public string MatchPreset()
        {
            foreach (string name in PresetNames)
            {
                var t = new Material { Color = Color, EmissionArgb = EmissionArgb };
                t.ApplyPreset(name);
                if (Math.Abs(t.Metallic - Metallic) > 0.006f || Math.Abs(t.Roughness - Roughness) > 0.006f ||
                    Math.Abs(t.EmissionStrength - EmissionStrength) > 0.006f || Math.Abs(t.Opacity - Opacity) > 0.006f ||
                    t.AlphaMode != AlphaMode || t.ColorArgb != ColorArgb) continue;
                if (t.EmissionStrength > 0.001f && t.EmissionArgb != EmissionArgb) continue;
                return name;
            }
            return null;
        }

        public void ApplyPreset(string name)
        {
            Opacity = 1f;
            AlphaMode = TextureAlphaMode.Blend;
            switch (name)
            {
                case "Стекло":
                    Set(0f, 0.08f, 0f); Opacity = 0.22f; TransmitLight = true;
                    Color = System.Drawing.Color.FromArgb(180, 224, 240); break;
                case "Вырезы PNG":
                    Set(0f, 0.7f, 0f); AlphaMode = TextureAlphaMode.Cutout; AlphaCutoff = 0.5f; break;
                case "Пластик":        Set(0f,   0.45f, 0f);   break;
                case "Глянцевый лак":  Set(0f,   0.12f, 0f);   break;
                case "Металл":         Set(1f,   0.28f, 0f);   break;
                case "Матовый металл": Set(1f,   0.62f, 0f);   break;
                case "Хром":           Set(1f,   0.06f, 0f);   break;
                case "Золото":
                    Set(1f, 0.22f, 0f);
                    Color = System.Drawing.Color.FromArgb(255, 226, 178, 74);
                    break;
                case "Дерево":         Set(0f,   0.78f, 0f);   break;
                case "Резина":         Set(0f,   0.95f, 0f);   break;
                case "Керамика":       Set(0.05f, 0.28f, 0f);  break;
                case "Неон":
                    Set(0f, 0.5f, 0.85f);
                    EmissionColor = Color;
                    break;
            }
        }

        private void Set(float metallic, float roughness, float emission)
        {
            Metallic         = metallic;
            Roughness        = roughness;
            EmissionStrength = emission;
        }

        // ── Paint layer ───────────────────────────────────────────
        [XmlIgnore]
        public bool HasPaint
        {
            get
            {
                if (_paintLayer != null) return true;
                return !string.IsNullOrEmpty(PaintBase64) || !string.IsNullOrEmpty(InkBase64) || TilesRebuildLayer;
            }
        }

        // The decoded paint bitmap, or null if nothing has been painted yet.
        [XmlIgnore]
        public Bitmap PaintLayer
        {
            get
            {
                if (_paintDecoded) return _paintLayer;
                _paintDecoded = true;
                if (!string.IsNullOrEmpty(PaintBase64))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(PaintBase64);
                        using (var ms = new MemoryStream(bytes))
                        using (var src = new Bitmap(ms))
                            _paintLayer = new Bitmap(src);
                    }
                    catch { _paintLayer = null; }
                }
                if (_paintLayer != null) DecodeInk();
                else if (string.IsNullOrEmpty(PaintBase64) && !string.IsNullOrEmpty(InkBase64)) RebuildFromJournal();
                else if (string.IsNullOrEmpty(PaintBase64) && TilesRebuildLayer) RebuildFromTiles();
                return _paintLayer;
            }
        }

        // The saved journal is trusted only while it matches the picture byte for
        // byte; otherwise (a file edited elsewhere, an older build) it is dropped and
        // the picture becomes the base of a fresh one when something needs it.
        private InkJournal ReadJournal(out uint stamp)
        {
            byte[] basePng = string.IsNullOrEmpty(InkBaseBase64) ? null : Convert.FromBase64String(InkBaseBase64);
            return InkJournal.Deserialize(Convert.FromBase64String(InkBase64), InkOps, basePng, out stamp);
        }

        // An undo snapshot carries no picture of the canvas — only its journal. The layer is
        // the replay of it: base, then every op in order.
        private void RebuildFromJournal()
        {
            try
            {
                uint stamp;
                var j = ReadJournal(out stamp);
                if (j == null) return;
                int w = j.LayerSize.Width > 0 ? j.LayerSize.Width : CanvasPaintSize;
                int h = j.LayerSize.Height > 0 ? j.LayerSize.Height : CanvasPaintSize;
                var layer = NewLayer(w, h);
                j.Replay(layer, new Rectangle(0, 0, w, h));
                _paintLayer = layer;
                _ink = j;
                _paintDirty = true;   // there is no PNG yet; saving encodes it
                _inkSavedRev = j.Revision; _inkSavedStamp = 0;
            }
            catch { /* unreadable journal: the canvas stays blank rather than failing the undo */ }
        }

        // Undo / redo: the previous state's layer is almost always the one wanted, give or take the
        // last stroke or two. Take it as it is and redraw only where the two journals differ (their
        // common ops are identical, so everything outside the differing ops' footprints already
        // matches) — a step back costs the strokes undone, not a replay of the whole drawing.
        // Falls back to the full rebuild when there is nothing to start from.
        public void AdoptLayerFrom(Material previous)
        {
            if (_paintDecoded || !string.IsNullOrEmpty(PaintBase64)) return;
            if (string.IsNullOrEmpty(InkBase64)) { if (TilesRebuildLayer) AdoptTilesFrom(previous); return; }
            try
            {
                var oldInk = previous != null ? previous.Ink : null;
                var oldLayer = previous != null ? previous.PaintLayer : null;
                uint stamp;
                var j = ReadJournal(out stamp);
                if (j == null || oldInk == null || oldLayer == null) return;   // lazily rebuilt in full
                int w = j.LayerSize.Width > 0 ? j.LayerSize.Width : CanvasPaintSize;
                int h = j.LayerSize.Height > 0 ? j.LayerSize.Height : CanvasPaintSize;
                if (oldLayer.Width != w || oldLayer.Height != h || !SameBase(oldInk.BasePng, j.BasePng)) return;

                int p = 0;
                while (p < oldInk.Ops.Count && p < j.Ops.Count &&
                       string.Equals(oldInk.Ops[p].Record64, j.Ops[p].Record64, StringComparison.Ordinal)) p++;
                Rectangle region = Rectangle.Empty;
                for (int i = p; i < oldInk.Ops.Count; i++) region = Union(region, oldInk.Ops[i].Bounds);
                for (int i = p; i < j.Ops.Count; i++) region = Union(region, j.Ops[i].Bounds);

                var layer = new Bitmap(oldLayer);
                if (!region.IsEmpty) j.Replay(layer, region);
                _paintLayer = layer;
                _paintDecoded = true;
                _ink = j;
                _paintDirty = true;
                _paintStampValid = false;
                _inkSavedRev = j.Revision; _inkSavedStamp = 0;
            }
            catch { /* fall back to the lazy full rebuild */ }
        }

        // ── Tiles ─────────────────────────────────────────────────

        // A snapshot's layer from its tiles, in full (nothing to start from).
        private void RebuildFromTiles()
        {
            try
            {
                int n = PaintTilesLayer;
                var layer = NewLayer(n, n);
                var cache = new Dictionary<int, TileState>();
                foreach (var t in PaintTiles)
                {
                    TileKit.Paste(layer, t.X, t.Y, t.Data);
                    cache[TileKit.Key(t.X, t.Y)] = new TileState
                    {
                        Hash = TileKit.HashOf(layer, TileKit.Area(t.X, t.Y, n, n)), Data = t.Data
                    };
                }
                _paintLayer = layer;
                _tileCache = cache;
                _tilesSyncedVersion = PaintVersion;
                _paintDirty = true;   // there is no flat PNG yet; saving encodes it
            }
            catch { /* unreadable tiles: the layer stays blank rather than failing the undo */ }
        }

        // Undo / redo of a 3-D shape's paint: start from the layer we are leaving and rewrite only
        // the blocks whose text differs from the one coming back.
        private void AdoptTilesFrom(Material previous)
        {
            try
            {
                var oldLayer = previous != null ? previous._paintLayer : null;
                int n = PaintTilesLayer;
                if (oldLayer == null || oldLayer.Width != n || oldLayer.Height != n) return;   // lazily rebuilt in full
                previous.SyncTilesForSnapshot();
                var old = previous._tileCache;
                if (old == null) return;

                var layer = new Bitmap(oldLayer);
                var cache = new Dictionary<int, TileState>();
                foreach (var t in PaintTiles)
                {
                    int key = TileKit.Key(t.X, t.Y);
                    TileState st;
                    if (old.TryGetValue(key, out st) && string.Equals(st.Data, t.Data, StringComparison.Ordinal))
                    {
                        cache[key] = st;
                        continue;
                    }
                    TileKit.Paste(layer, t.X, t.Y, t.Data);
                    cache[key] = new TileState { Hash = TileKit.HashOf(layer, TileKit.Area(t.X, t.Y, n, n)), Data = t.Data };
                }
                foreach (var key in old.Keys)
                    if (!cache.ContainsKey(key))   // painted before, blank now
                        TileKit.Clear(layer, TileKit.Area(key % 4096, key / 4096, n, n));
                _paintLayer = layer;
                _paintDecoded = true;
                _tileCache = cache;
                _tilesSyncedVersion = PaintVersion;
                _paintDirty = true;
                _paintStampValid = false;
            }
            catch { /* fall back to the lazy full rebuild */ }
        }

        // Writes the live layer down as tiles (PaintTiles), encoding only the blocks that changed
        // since the last time. Cheap when nothing was painted: the layer's version is the same.
        private void SyncTilesForSnapshot()
        {
            var layer = _paintLayer;
            if (layer == null) return;
            if (layer.Width != layer.Height) { PaintTiles = null; PaintTilesLayer = 0; _tileCache = null; return; }
            int n = layer.Width;
            if (TilesRebuildLayer && PaintTilesLayer == n && _tilesSyncedVersion == PaintVersion && _tileCache != null) return;

            var old = _tileCache ?? new Dictionary<int, TileState>();
            var cache = new Dictionary<int, TileState>();
            var tiles = new List<PaintTile>();
            int g = TileKit.Grid(n);
            var stale = new List<int>();   // blocks whose PNG must be made
            var data = layer.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int ty = 0; ty < g; ty++)
                    for (int tx = 0; tx < g; tx++)
                    {
                        bool empty;
                        ulong h = TileKit.Hash(data, TileKit.Area(tx, ty, n, n), out empty);
                        if (empty) continue;
                        int key = TileKit.Key(tx, ty);
                        TileState st;
                        if (!old.TryGetValue(key, out st) || st.Hash != h)
                        {
                            st = new TileState { Hash = h };
                            stale.Add(key);
                        }
                        cache[key] = st;
                    }
            }
            finally { layer.UnlockBits(data); }

            foreach (int key in stale)
                cache[key].Data = TileKit.Encode(layer, TileKit.Area(key % 4096, key / 4096, n, n));
            for (int ty = 0; ty < g; ty++)
                for (int tx = 0; tx < g; tx++)
                {
                    TileState st;
                    if (cache.TryGetValue(TileKit.Key(tx, ty), out st))
                        tiles.Add(new PaintTile { X = tx, Y = ty, Data = st.Data });
                }
            _tileCache = cache;
            PaintTiles = tiles;
            PaintTilesLayer = n;
            _tilesSyncedVersion = PaintVersion;
        }

        private void ForgetTiles()
        {
            PaintTiles = null; PaintTilesLayer = 0; _tileCache = null; _tilesSyncedVersion = -1;
        }

        private static bool SameBase(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static Rectangle Union(Rectangle a, Rectangle b)
        {
            return a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);
        }

        // Self-check for tests and diagnostics: how many pixels of the layer differ from what its
        // journal replays to (0 when the journal still describes the layer, up to anti-aliasing).
        public int JournalMismatch()
        {
            var layer = PaintLayer;
            if (layer == null || _ink == null) return 0;
            using (var replay = new Bitmap(layer.Width, layer.Height, PixelFormat.Format32bppArgb))
            {
                _ink.Replay(replay, new Rectangle(0, 0, layer.Width, layer.Height));
                int tol;
                if (!int.TryParse(Environment.GetEnvironmentVariable("PAINT3D_INK_TOL"), out tol)) tol = 64;
                return PixelsDiffer(layer, replay, tol);
            }
        }

        private static int PixelsDiffer(Bitmap a, Bitmap b, int tolerance)
        {
            int w = a.Width, h = a.Height, n = 0;
            var da = a.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var ra = new int[w]; var rb = new int[w];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(da.Scan0, y * da.Stride), ra, 0, w);
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(db.Scan0, y * db.Stride), rb, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        if (ra[x] == rb[x]) continue;
                        int aa = (ra[x] >> 24) & 255, ba = (rb[x] >> 24) & 255;
                        int d = Math.Abs(aa - ba);
                        if (aa > 0 && ba > 0)
                        {
                            d = Math.Max(d, Math.Abs((((ra[x] >> 16) & 255) - ((rb[x] >> 16) & 255)) * Math.Min(aa, ba) / 255));
                            d = Math.Max(d, Math.Abs((((ra[x] >> 8) & 255) - ((rb[x] >> 8) & 255)) * Math.Min(aa, ba) / 255));
                            d = Math.Max(d, Math.Abs(((ra[x] & 255) - (rb[x] & 255)) * Math.Min(aa, ba) / 255));
                        }
                        if (d > tolerance) n++;
                    }
                }
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
            return n;
        }

        // The saved journal is trusted only while it matches the picture byte for
        // byte; otherwise (a file edited elsewhere, an older build) it is dropped and
        // the picture becomes the base of a fresh one when something needs it.
        private void DecodeInk()
        {
            if (string.IsNullOrEmpty(InkBase64)) return;
            try
            {
                uint stamp;
                var j = ReadJournal(out stamp);
                uint now = InkJournal.Stamp(_paintLayer);
                if (j != null && stamp == now)
                {
                    _ink = j;
                    _paintStamp = now; _paintStampValid = true;
                    _inkSavedRev = j.Revision; _inkSavedStamp = now;
                }
                else j?.Dispose();
            }
            catch { /* unreadable journal: carry on with the flat picture */ }
        }

        // The canvas's object journal, created on first use. A picture that predates
        // it (an old file) is kept as its base, so it still shows and only the new
        // things are erasable as objects. Call before the first change to the layer.
        [XmlIgnore]
        public InkJournal Ink { get { var l = PaintLayer; return _ink; } }

        public InkJournal EnsureInk()
        {
            var layer = PaintLayer;
            if (_ink != null) return _ink;
            if (layer == null || IsBlank(layer)) { _ink = new InkJournal(); return _ink; }
            byte[] png = null;
            lock (_paintSync)
                if (!_paintDirty && !string.IsNullOrEmpty(PaintBase64))
                    try { png = Convert.FromBase64String(PaintBase64); } catch { png = null; }
            _ink = InkJournal.WithBase(png ?? InkJournal.EncodePng(layer));
            return _ink;
        }

        private static bool IsBlank(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, w);
                    for (int x = 0; x < w; x++) if (((uint)row[x] >> 24) != 0) return false;
                }
                return true;
            }
            finally { bmp.UnlockBits(data); }
        }

        // A fully transparent layer. GDI+ hands one out already cleared; other implementations
        // (libgdiplus) may return whatever the memory held, which would show as stray paint.
        private static Bitmap NewLayer(int w, int h)
        {
            var layer = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            TileKit.Clear(layer, new Rectangle(0, 0, w, h));
            return layer;
        }

        // Create the paint layer on demand (transparent) so a brush can stamp onto it.
        public Bitmap EnsurePaintLayer()
        {
            return EnsurePaintLayer(PaintSize);
        }

        // Same, at a given resolution. A smaller existing layer (a project saved when
        // the canvas was 1024px) is upscaled once, so old drawings keep working.
        public Bitmap EnsurePaintLayer(int size)
        {
            var layer = PaintLayer;
            if (layer == null)
            {
                _paintLayer   = NewLayer(size, size);
                _paintDecoded = true;
                return _paintLayer;
            }
            if (layer.Width < size)
            {
                var up = NewLayer(size, size);
                using (var g = Graphics.FromImage(up))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingMode   = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(layer, new Rectangle(0, 0, size, size));
                }
                layer.Dispose();
                _paintLayer = up;
                // The journal's coordinates belonged to the smaller layer: the upscaled
                // picture becomes the base of a new one (see EnsureInk).
                _ink?.Dispose(); _ink = null; InkBase64 = null; InkBaseBase64 = null; InkOps = null;
                ForgetTiles();
                MarkPaintDirty();
                return up;
            }
            return layer;
        }

        // Pixel sizes (brush radius, pen width, font) are specified for a 1024px
        // layer; multiply by this to get the same on-screen size on this layer.
        public static float PaintScale(Bitmap layer)
        {
            return layer == null ? 1f : layer.Width / (float)PaintSize;
        }

        // Bumped on every brush stroke so the GL renderer knows to re-upload the
        // paint texture to the GPU.
        [XmlIgnore] public int PaintVersion { get; private set; }

        public void MarkPaintDirty()
        {
            lock (_paintSync) { _paintDirty = true; _paintStampValid = false; _gpuDirtyFull = true; PaintVersion++; }
        }

        // A change confined to `area` (layer px): the renderer re-uploads only that
        // part of the texture — a 2048² layer is 16 MB, too much to send per mouse move.
        public void MarkPaintDirty(Rectangle area)
        {
            lock (_paintSync)
            {
                _paintDirty = true;
                _paintStampValid = false;
                PaintVersion++;
                if (_gpuDirtyFull) return;
                _gpuDirty = _gpuDirty.IsEmpty ? area : Rectangle.Union(_gpuDirty, area);
            }
        }

        // For the renderer: the area changed since the last call, or false when the
        // whole texture must be re-uploaded. Resets the tracking either way.
        public bool TakeGpuDirtyArea(out Rectangle area)
        {
            lock (_paintSync)
            {
                bool partial = !_gpuDirtyFull && !_gpuDirty.IsEmpty;
                area = _gpuDirty;
                _gpuDirty = Rectangle.Empty;
                _gpuDirtyFull = false;
                return partial;
            }
        }

        private readonly object _paintSync = new object();
        private Rectangle _gpuDirty;
        private bool      _gpuDirtyFull = true;

        // Wipe all painting, revealing the base material colour again.
        public void ClearPaint()
        {
            if (_paintLayer != null) { _paintLayer.Dispose(); _paintLayer = null; }
            _paintDecoded = true;   // nothing left to decode
            PaintBase64   = "";
            _ink?.Dispose(); _ink = null; InkBase64 = null; InkBaseBase64 = null; InkOps = null;
            ForgetTiles();
            MarkPaintDirty();
        }

        // Encode the paint bitmap to base64 before serialization (only if changed).
        public void SyncPaintForSave(bool snapshot = false)
        {
            // A state restored from a snapshot may still hold tiles only; a file holds the picture.
            if (!snapshot && _paintLayer == null && !_paintDecoded && TilesRebuildLayer && string.IsNullOrEmpty(PaintBase64))
                { var restored = PaintLayer; }
            if (_paintLayer == null)
            {
                if (_paintDecoded) ForgetTiles();   // painting was wiped
                return;
            }
            // An undo snapshot of a canvas with a journal needs no picture (and its PNG encode
            // is skipped, which also makes taking the snapshot cheap).
            if (snapshot && _ink != null && !_ink.IsEmpty) { SyncInkForSave(true); return; }
            // Any other layer goes into a snapshot as tiles: only the blocks that changed are encoded.
            if (snapshot) { SyncTilesForSnapshot(); SyncInkForSave(true); return; }
            bool current;
            lock (_paintSync) current = !_paintDirty && !string.IsNullOrEmpty(PaintBase64);
            if (!current)
            {
                string b64 = Encode(_paintLayer);
                lock (_paintSync)
                {
                    PaintBase64 = b64;
                    _paintDirty = false;
                }
            }
            SyncInkForSave();
        }

        // The journal's saved form, kept in step with the PNG it belongs to.
        private byte[] _inkBaseSaved;   // the base picture InkBaseBase64 was made from

        private void SyncInkForSave(bool snapshot = false)
        {
            if (_ink == null || _ink.IsEmpty) { InkBase64 = null; InkBaseBase64 = null; InkOps = null; _inkBaseSaved = null; return; }
            uint stamp;
            lock (_paintSync)
            {
                // A snapshot rebuilds the layer from the journal, so it needs no fingerprint of pixels
                // (computing one is a full pass over the layer); a saved file does.
                if (!_paintStampValid && !snapshot) { _paintStamp = InkJournal.Stamp(_paintLayer); _paintStampValid = true; }
                stamp = _paintStampValid ? _paintStamp : 0;
            }
            if (!ReferenceEquals(_inkBaseSaved, _ink.BasePng))
            {
                InkBaseBase64 = _ink.BasePng == null ? null : Convert.ToBase64String(_ink.BasePng);
                _inkBaseSaved = _ink.BasePng;
            }
            if (!string.IsNullOrEmpty(InkBase64) && _inkSavedRev == _ink.Revision && _inkSavedStamp == stamp) return;
            InkBase64 = Convert.ToBase64String(_ink.SerializeHeader(stamp, _paintLayer.Size));
            InkOps = _ink.OpRecords();
            _inkSavedRev = _ink.Revision;
            _inkSavedStamp = stamp;
        }

        // Called when a stroke ends: PNG-encode the layer on a worker thread, so the
        // undo snapshot taken at the *next* stroke's mouse-down finds it ready instead
        // of encoding synchronously (≈100–250 ms on a 1024–2048 layer). If painting
        // continues meanwhile, the stale result is dropped and saving encodes afresh.
        public void EncodePaintInBackground()
        {
            if (_paintLayer == null) return;
            int version;
            lock (_paintSync)
            {
                if (!_paintDirty) return;
                version = PaintVersion;
            }
            var copy = new Bitmap(_paintLayer);   // the worker never touches the live layer
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string b64; uint stamp;
                try { b64 = Encode(copy); stamp = InkJournal.Stamp(copy); }
                catch { return; }
                finally { copy.Dispose(); }
                lock (_paintSync)
                {
                    if (PaintVersion != version) return;
                    PaintBase64 = b64;
                    _paintStamp = stamp; _paintStampValid = true;
                    _paintDirty = false;
                }
            });
        }

        private static string Encode(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public void EmbedTexture()
        {
            if (string.IsNullOrEmpty(TextureBase64) && HasTexture && Texture != null)
            {
                TextureBase64 = Encode(Texture);
                _loadedData = TextureBase64;
            }
        }

        public Material Clone()
        {
            var c = new Material
            {
                ColorArgb = ColorArgb, Mode = Mode, TexturePath = TexturePath, NoTile = NoTile,
                Metallic = Metallic, Roughness = Roughness, Opacity = Opacity,
                EmissionArgb = EmissionArgb, EmissionStrength = EmissionStrength,
                AoTexturePath = AoTexturePath, TextureBase64 = TextureBase64,
                AlphaMode = AlphaMode, AlphaCutoff = AlphaCutoff, PixelTexture = PixelTexture,
                TransmitLight = TransmitLight, LightEnabled = LightEnabled, LightPower = LightPower,
                LightRange = LightRange, LightFlicker = LightFlicker, LocalLightArgb = LocalLightArgb
            };
            if (PaintLayer != null)
            {
                c._paintLayer   = new Bitmap(PaintLayer);
                c._paintDecoded = true;
                c._paintDirty   = true;
                if (_ink != null) c._ink = _ink.Clone();
                if (_tileCache != null) c._tileCache = new Dictionary<int, TileState>(_tileCache);   // entries are never edited
            }
            else
            {
                c.PaintBase64 = PaintBase64;
                c.PaintTilesLayer = PaintTilesLayer;
                if (PaintTiles != null) c.PaintTiles = new List<PaintTile>(PaintTiles);
                c.InkBase64   = InkBase64;
                c.InkBaseBase64 = InkBaseBase64;
                if (InkOps != null) c.InkOps = new System.Collections.Generic.List<string>(InkOps);
            }
            return c;
        }
    }
}
