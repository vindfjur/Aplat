using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Aplat;
using Aplat.IO;
using Aplat.Models;
using Aplat.Primitives;
using Aplat.Rendering;

// Boundary and regression tests of the headless core: geometry, primitives, camera, materials,
// export and hostile scene files. No window is created.
class ModelTests
{
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }
    static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
    static bool Near(float a, float b, float e = 1e-3f) { return Math.Abs(a - b) <= e; }
    static bool NoThrow(Action a, string msg)
    {
        try { a(); Check(true, msg); return true; }
        catch (Exception e) { Check(false, msg + " threw " + e.GetType().Name + ": " + e.Message); return false; }
    }
    static bool ValidMesh(Mesh m)
    {
        if (m == null || m.Vertices.Count == 0 || m.Faces.Count == 0) return false;
        foreach (var v in m.Vertices) if (!Finite(v.X) || !Finite(v.Y) || !Finite(v.Z)) return false;
        foreach (var f in m.Faces) { if (f.Length < 3) return false; foreach (int i in f) if (i < 0 || i >= m.Vertices.Count) return false; }
        return true;
    }

    // Minimal reader of binary FBX 7.4 (what the exporter writes): used to check structure, not to import.
    class FNode
    {
        public string Name; public List<object> P = new List<object>(); public List<FNode> K = new List<FNode>();
        public IEnumerable<FNode> All(string n) { foreach (var k in K) { if (k.Name == n) yield return k; foreach (var d in k.All(n)) yield return d; } }
        public FNode One(string n) { return K.FirstOrDefault(k => k.Name == n); }
    }
    static List<FNode> ParseFbx(byte[] b)
    {
        try
        {
            if (b.Length < 27 || System.Text.Encoding.ASCII.GetString(b, 0, 20) != "Kaydara FBX Binary  " || BitConverter.ToUInt32(b, 23) != 7400) return null;
            int pos = 27;
            var roots = ReadFbxNodes(b, ref pos, b.Length);
            // footer: 16-byte id, 4 zeros, padding to 16, version, 120 zeros, 16-byte magic
            return roots != null && b.Length - pos >= 16 + 4 + 4 + 120 + 16 ? roots : null;
        }
        catch { return null; }
    }
    static List<FNode> ReadFbxNodes(byte[] b, ref int pos, int limit)
    {
        var list = new List<FNode>();
        while (true)
        {
            uint end = BitConverter.ToUInt32(b, pos);
            if (end == 0) { pos += 13; return list; }
            uint np = BitConverter.ToUInt32(b, pos + 4), pl = BitConverter.ToUInt32(b, pos + 8); int nl = b[pos + 12];
            var n = new FNode { Name = System.Text.Encoding.ASCII.GetString(b, pos + 13, nl) };
            int q = pos + 13 + nl, pend = q + (int)pl;
            for (uint i = 0; i < np; i++)
            {
                char t = (char)b[q++];
                switch (t)
                {
                    case 'I': n.P.Add(BitConverter.ToInt32(b, q)); q += 4; break;
                    case 'L': n.P.Add(BitConverter.ToInt64(b, q)); q += 8; break;
                    case 'D': n.P.Add(BitConverter.ToDouble(b, q)); q += 8; break;
                    case 'C': n.P.Add(b[q] != 0); q += 1; break;
                    case 'S': { int len = (int)BitConverter.ToUInt32(b, q); n.P.Add(System.Text.Encoding.ASCII.GetString(b, q + 4, len)); q += 4 + len; break; }
                    case 'R': { int len = (int)BitConverter.ToUInt32(b, q); q += 4 + len; n.P.Add(new byte[len]); break; }
                    case 'd': case 'i':
                    {
                        int cnt = (int)BitConverter.ToUInt32(b, q), enc = (int)BitConverter.ToUInt32(b, q + 4), clen = (int)BitConverter.ToUInt32(b, q + 8); q += 12;
                        if (enc != 0) return null;
                        if (t == 'd') { var a = new double[cnt]; for (int k = 0; k < cnt; k++) a[k] = BitConverter.ToDouble(b, q + 8 * k); n.P.Add(a); }
                        else { var a = new int[cnt]; for (int k = 0; k < cnt; k++) a[k] = BitConverter.ToInt32(b, q + 4 * k); n.P.Add(a); }
                        q += clen; break;
                    }
                    default: return null;
                }
            }
            if (q != pend) return null;
            pos = pend;
            if (pos < end) { n.K = ReadFbxNodes(b, ref pos, (int)end); if (n.K == null) return null; }
            if (pos != end) return null;
            list.Add(n);
        }
    }

    [STAThread]
    static int Main()
    {
        // ── Vec3 ──
        var a = new Vec3(1, 2, 3); var b = new Vec3(-2, 0.5f, 4);
        Check(Near(Vec3.Dot(a, b), -2 + 1 + 12), "dot product");
        var c = Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0));
        Check(Near(c.Z, 1) && Near(c.X, 0), "cross product follows the right hand");
        Check(Near(Vec3.Zero.Normalized().Length, 0) || Finite(Vec3.Zero.Normalized().X), "normalising the zero vector does not give NaN");
        Check(Near(new Vec3(3, 4, 0).Normalized().Length, 1), "normalised length is 1");
        Check(a.Get(0) == 1 && a.Get(1) == 2 && a.Get(2) == 3 && a.With(1, 9).Y == 9 && a.Y == 2, "Get / With per axis");
        Check(Near((a + b - b).X, a.X) && Near((a * 2).Z, 6) && Near((2 * a).Y, 4), "operators");
        Check(!string.IsNullOrEmpty(a.ToString()), "ToString");

        // ── Primitives ──
        foreach (PrimitiveKind k in Enum.GetValues(typeof(PrimitiveKind)))
        {
            var m = PrimitiveFactory.Create(k);
            Check(ValidMesh(m), "primitive " + k + " is a valid mesh (" + (m == null ? 0 : m.Vertices.Count) + " vertices)");
            Check(!string.IsNullOrEmpty(PrimitiveFactory.KindName(k)), "name of " + k);
            var clone = m.Clone();
            Check(clone.Vertices.Count == m.Vertices.Count && !ReferenceEquals(clone.Faces[0], m.Faces[0]), "clone of " + k + " is deep");
        }
        foreach (float size in new[] { 0.001f, 1f, 1000f })
        {
            Check(ValidMesh(PrimitiveFactory.CreateCube(size)), "cube of size " + size);
            Check(ValidMesh(PrimitiveFactory.CreateSphere(size * 0.5f, 3, 3)), "sphere with the fewest segments, radius " + size * 0.5f);
        }
        Check(ValidMesh(PrimitiveFactory.CreateCylinder(0.5f, 1f, 3)) && ValidMesh(PrimitiveFactory.CreateCone(0.5f, 1f, 3)), "cylinder / cone with 3 segments");
        Check(ValidMesh(PrimitiveFactory.CreateTorus(0.35f, 0.15f, 3, 3)), "torus with 3 x 3 segments");
        foreach (var ratio in new[] { 0.05f, 1f, 20f })
            Check(ValidMesh(CanvasFactory.CreateSheet(4f, 4f * ratio)), "canvas sheet, aspect " + ratio);

        // ── Mesh.Sanitize ──
        var bad = new Mesh();
        bad.Vertices.Add(new Vec3(0, 0, 0)); bad.Vertices.Add(new Vec3(1, 0, 0)); bad.Vertices.Add(new Vec3(0, 1, 0));
        bad.Faces.Add(new[] { 0, 1, 2 }); bad.Faces.Add(new[] { 0, 1, 7 }); bad.Faces.Add(new[] { -1, 1, 2 }); bad.Faces.Add(new[] { 0, 1 }); bad.Faces.Add(null);
        Check(bad.Sanitize() == 4 && bad.Faces.Count == 1, "faces with bad / missing indices are dropped, the good one stays");
        var nul = new Mesh { Vertices = null, Faces = null };
        Check(nul.Sanitize() == 0 && nul.Vertices != null && nul.Faces != null, "a mesh with null lists is repaired");

        // ── Text ──
        foreach (string text in new[] { "A", "Привет", "Hello world", "line\nbreak", "😀", new string('W', 120) })
            NoThrow(() => { var m = TextMeshBuilder.Build(text, "Segoe UI", 1.5f); Check(m != null && (m.Faces.Count == 0 || ValidMesh(m)), "3-D text \"" + (text.Length > 12 ? text.Substring(0, 12) + "…" : text.Replace("\n", "\\n")) + "\" builds a valid mesh"); }, "build text");
        NoThrow(() => { var m = TextMeshBuilder.Build("", "NoSuchFont", -5f, -1f); Check(m != null, "empty text, unknown font, negative size: still a mesh object"); }, "text with odd arguments");

        // ── Camera ──
        var cam = new Camera();
        foreach (var mode in new[] { ViewMode.Perspective, ViewMode.Front, ViewMode.Side, ViewMode.Top })
        {
            cam.Mode = mode;
            foreach (var size in new[] { new Size(800, 600), new Size(1, 1), new Size(0, 0) })
            {
                var p = cam.Project(new Vec3(0.3f, 0.2f, 0.1f), size.Width, size.Height);
                Check(Finite(p.X) && Finite(p.Y), "project in " + mode + " at " + size.Width + "x" + size.Height + " is finite");
            }
            Check(Finite(cam.GetDepth(Vec3.Zero)) && Finite(cam.MoveStep()), "depth / move step finite in " + mode);
            Check(Near(cam.RightDir().Length, 1, 0.01f) && Near(cam.UpDir().Length, 1, 0.01f) && Near(cam.ForwardDir().Length, 1, 0.01f), "camera axes are unit length in " + mode);
            Check(Finite(cam.ScreenRight().X) && Finite(cam.ScreenUp().Y), "screen axes finite in " + mode);
        }
        cam.Mode = ViewMode.Perspective;
        foreach (float r in new[] { 0f, -3f, 1e6f, float.NaN })
        {
            NoThrow(() => cam.Frame(Vec3.Zero, r), "frame radius " + r);
            Check(cam.Distance >= Camera.MinDistance && cam.Distance <= Camera.MaxDistance, "distance stays within limits after framing radius " + r + " (" + cam.Distance + ")");
        }
        NoThrow(() => cam.Frame(new Vec3(float.NaN, 0, 0), 1f), "frame on a NaN centre");
        Check(Finite(cam.Target.X), "...the camera target stays finite");
        cam.Mode = ViewMode.Front; Check(Finite(cam.GetOrthoViewDir().X), "ortho view direction");
        cam.Mode = ViewMode.Perspective; cam.AngleX = 89.9f; cam.AngleY = 720f;
        Check(Finite(cam.GetPosition().X) && Finite(cam.GetPosition().Y), "position near the pole is finite");
        var proj = cam.Project(Vec3.Zero, 800, 600);
        Check(Finite(cam.ToCameraSpace(new Vec3(1, 2, 3)).Z) && Finite(proj.X), "camera space finite");

        // ── SceneGeometry ──
        var cube = new SceneObject { Name = "c", PrimitiveType = "Cube", Mesh = PrimitiveFactory.CreateCube(1f), Position = new Vec3(2, 3, 4) };
        var g = new SceneObject { Name = "g", IsGroup = true };
        g.Children.Add(cube);
        g.Children.Add(new SceneObject { Name = "s", Mesh = PrimitiveFactory.CreateSphere(), Position = new Vec3(-2, 0, 0) });
        Vec3 mn, mx;
        Check(SceneGeometry.WorldBounds(cube, out mn, out mx) && Near(mx.X - mn.X, 1f), "world bounds of a unit cube");
        Check(!SceneGeometry.WorldBounds(new SceneObject { Mesh = new Mesh() }, out mn, out mx), "no geometry: no bounds");
        Check(!SceneGeometry.WorldBounds(new SceneObject { IsGroup = true }, out mn, out mx), "empty group: no bounds");
        Check(SceneGeometry.Leaves(g).Count() == 2 && SceneGeometry.Leaves(cube).Count() == 1, "leaves of a group and of a plain object");
        var center0 = SceneGeometry.Center(g);
        SceneGeometry.Translate(g, new Vec3(1, 1, 1));
        Check(Near(SceneGeometry.Center(g).X, center0.X + 1) && Near(SceneGeometry.Center(g).Y, center0.Y + 1), "translate moves the centre by the delta");
        for (int axis = 0; axis < 3; axis++)
        {
            var before = SceneGeometry.Center(g);
            SceneGeometry.ScaleAround(g, before, axis, 2f);
            // The centre is the mean of the vertices, which is not exactly the origin of a UV sphere, so a
            // non-uniform scale of a rotated child shifts it a little: allow a small drift.
            Check(Near((SceneGeometry.Center(g) - before).Length, 0, 0.15f), "scaling about the centre keeps it (axis " + axis + ")");
            SceneGeometry.RotateAround(g, SceneGeometry.Center(g), axis, 90f);
            Check(Near((SceneGeometry.Center(g) - before).Length, 0, 0.15f), "rotating about the centre keeps it (axis " + axis + ")");
        }
        NoThrow(() => SceneGeometry.ScaleAround(g, Vec3.Zero, 0, 0f), "scale by zero does not throw");
        NoThrow(() => SceneGeometry.RotateAroundAxis(g, Vec3.Zero, new Vec3(0, 0, 0), 30f), "rotate about a zero axis does not throw");
        Check(Finite(SceneGeometry.Center(g).X), "...and nothing became NaN");

        // ── Material ──
        foreach (string preset in Material.PresetNames.Concat(new[] { "no such preset", "", null }))
        {
            var m = new Material();
            NoThrow(() => m.ApplyPreset(preset), "preset " + (preset ?? "null"));
            Check(m.Opacity >= 0f && m.Opacity <= 1f && m.Metallic >= 0f && m.Metallic <= 1f && m.Roughness >= 0f && m.Roughness <= 1f, "preset " + (preset ?? "null") + " keeps values in 0..1");
        }
        // A preset is recognised again from the settings it produced, in either language; a tweak makes it custom.
        foreach (var lang in new[] { Lang.Russian, Lang.English })
        {
            Loc.Set(lang);
            foreach (string preset in Material.PresetNames)
            {
                var m = new Material { Color = Color.FromArgb(40, 120, 200) };
                m.ApplyPreset(preset);
                Check(m.MatchPreset() == preset, "preset '" + preset + "' is recognised from its settings (" + lang + ")");
                m.Roughness = Math.Min(1f, m.Roughness + 0.2f);
                Check(m.MatchPreset() == null, "a tweaked '" + preset + "' is custom (" + lang + ")");
            }
            var fresh = new Material();
            fresh.ApplyPreset(Material.PresetNames[2]);
            Check(fresh.Metallic > 0.9f, "choosing the metal preset makes it metallic in " + lang + " too");
        }
        Loc.Set(Lang.Russian);
        Check(new Material().MatchPreset() == null, "a new material is custom");
        var mat = new Material { ColorArgb = Color.Red.ToArgb(), Metallic = 0.4f, TexturePath = "" };
        var mc = mat.Clone();
        Check(mc.Metallic == 0.4f && mc.ColorArgb == mat.ColorArgb && !ReferenceEquals(mc, mat), "material clone copies values");
        Check(!mat.HasTexture && mat.Texture == null && !mat.HasPaint, "a plain material has no texture and no paint");
        mat.TexturePath = @"\\evil.example\share\x.png";
        Check(mat.HasTexture && mat.Texture == null, "a network texture path is reported but never loaded");
        var layer = mat.EnsurePaintLayer(64);
        Check(layer != null && layer.Width == 64 && mat.PaintLayer == layer, "paint layer of the requested size");

        // ── Material: textures, paint layer, dirty tracking ──
        string tdir = Path.Combine(Path.GetTempPath(), "aplat-mat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tdir);
        try
        {
            string opaque = Path.Combine(tdir, "opaque.png"), alphaPng = Path.Combine(tdir, "alpha.png");
            using (var bmp = new Bitmap(8, 8)) { using (var gg = Graphics.FromImage(bmp)) gg.Clear(Color.Orange); bmp.Save(opaque, System.Drawing.Imaging.ImageFormat.Png); }
            using (var bmp = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) { using (var gg = Graphics.FromImage(bmp)) gg.Clear(Color.FromArgb(40, 255, 0, 0)); bmp.Save(alphaPng, System.Drawing.Imaging.ImageFormat.Png); }
            File.WriteAllText(Path.Combine(tdir, "fake.png"), "not an image");

            var tm = new Material { TexturePath = opaque };
            Check(tm.HasTexture && tm.Texture != null && tm.Texture.Width == 8, "a local PNG texture loads");
            Check(!tm.IsTransparent, "an opaque texture is not transparent");
            tm.TexturePath = alphaPng; tm.AlphaMode = TextureAlphaMode.Blend;
            Check(tm.Texture != null && tm.IsTransparent, "a PNG with alpha makes the material transparent in blend mode");
            tm.AlphaMode = TextureAlphaMode.Opaque;
            Check(!tm.IsTransparent, "...but not in opaque mode");
            tm.TexturePath = Path.Combine(tdir, "fake.png");
            Check(tm.HasTexture && tm.Texture == null, "a broken image yields no texture instead of throwing");
            tm.TexturePath = Path.Combine(tdir, "missing.png");
            Check(tm.Texture == null, "a missing file yields no texture");
            tm.TexturePath = opaque; tm.EmbedTexture();
            Check(!string.IsNullOrEmpty(tm.TextureBase64) && tm.Texture != null, "a texture can be embedded");
            var embClone = tm.Clone();
            Check(embClone.TextureBase64 == tm.TextureBase64 && embClone.Texture != null, "an embedded texture survives cloning");
            tm.TexturePath = ""; Check(!tm.HasTexture, "clearing the path clears the embedded texture");
            bool big = false; try { Material.LoadBitmap(Path.Combine(tdir, "missing.png")); } catch (IOException) { big = true; } catch (Exception) { big = true; }
            Check(big, "LoadBitmap of a missing file throws");
            Check(Material.LoadBitmap("") == null && Material.LoadBitmap(null) == null, "LoadBitmap of nothing is null");
            var ao = new Material { AoTexturePath = opaque };
            Check(ao.HasAoTexture && ao.AoTexture != null && ao.AoTexture.Width == 8, "an AO map loads");
            ao.AoTexturePath = @"\\evil.example\a.png"; Check(ao.AoTexture == null, "an AO map on a network path is not loaded");
        }
        finally { try { Directory.Delete(tdir, true); } catch { } }

        var pm = new Material();
        Check(!pm.HasPaint && pm.PaintLayer == null, "fresh material: no paint");
        var pl = pm.EnsurePaintLayer(128);
        using (var gg = Graphics.FromImage(pl)) gg.FillRectangle(Brushes.Red, 10, 10, 20, 20);
        Rectangle da;
        Check(!pm.TakeGpuDirtyArea(out da), "a fresh layer asks for a full upload (not a partial one)");
        pm.MarkPaintDirty(new Rectangle(5, 5, 10, 10)); pm.MarkPaintDirty(new Rectangle(50, 50, 10, 10));
        int ver = pm.PaintVersion;
        Check(pm.TakeGpuDirtyArea(out da) && da.Contains(5, 5) && da.Contains(59, 59), "two dirty rectangles are merged into one partial area");
        Check(!pm.TakeGpuDirtyArea(out da), "...and taking resets the tracking");
        pm.MarkPaintDirty(); Check(pm.PaintVersion > ver && !pm.TakeGpuDirtyArea(out da), "a full mark forces a full upload");
        Check(Material.PaintScale(pl) == 128f / Material.PaintSize && Material.PaintScale(null) == 1f, "paint scale");
        pm.SyncPaintForSave();
        Check(!string.IsNullOrEmpty(pm.PaintBase64), "the layer is encoded for saving");
        var pc = pm.Clone();
        Check(pc.PaintLayer != null && !ReferenceEquals(pc.PaintLayer, pm.PaintLayer) && pc.PaintLayer.GetPixel(15, 15).R == 255, "a cloned material gets its own copy of the paint");
        string encoded = pm.PaintBase64;
        pm.ClearPaint();
        Check(!pm.HasPaint && pm.PaintLayer == null && pc.PaintLayer != null, "clearing paint leaves the clone untouched");
        pm.SyncPaintForSave(true); pm.SyncPaintForSave(false);
        var restored = new Material { PaintBase64 = encoded };
        Check(restored.HasPaint, "paint known only as base64 counts as painted");
        var em = new Material();
        em.EmissionColor = Color.Green;
        Check(em.EmissionColor.ToArgb() == Color.Green.ToArgb(), "emission colour round trips");
        em.LightEnabled = true; em.LightColor = Color.Blue;
        Check(em.LightColor.ToArgb() == Color.Blue.ToArgb(), "light colour round trips");

        // ── Export ──
        var hostile = new SceneObject { Name = "Куб \"quoted\"\nline", PrimitiveType = "Cube", Mesh = PrimitiveFactory.CreateCube(1f) };
        var nomesh = new SceneObject { Name = "nomesh", Mesh = null };
        var hidden = new SceneObject { Name = "hidden", Mesh = PrimitiveFactory.CreateCube(1f), IsVisible = false };
        var objects = new List<SceneObject> { hostile, nomesh, hidden, new SceneObject { Name = "empty group", IsGroup = true } };
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            string stl = null; byte[] fbx = null; ObjExport obj = null;
            NoThrow(() => stl = ExportManager.ToSTL(objects), "STL of hostile names / missing meshes");
            NoThrow(() => fbx = ExportManager.ToFBX(objects), "FBX of hostile names / missing meshes");
            NoThrow(() => obj = ExportManager.BuildOBJ(objects, "m.mtl"), "OBJ of hostile names / missing meshes");
            Check(stl != null && !stl.Contains(",") && stl.Split(new[] { "facet normal" }, StringSplitOptions.None).Length - 1 == 12, "STL: Russian locale still writes dots; a cube is 12 triangles; hidden object skipped");
            var hostileTree = fbx != null ? ParseFbx(fbx) : null;
            var hostileModels = hostileTree != null ? hostileTree.SelectMany(r => r.All("Model")).ToList() : null;
            Check(hostileTree != null && hostileModels.Count == 1 && !((string)hostileModels[0].P[1]).Contains("\"") && !((string)hostileModels[0].P[1]).Contains("\n"), "FBX: parses, only the visible mesh, name has no quotes or line breaks");
            Check(hostileTree != null && hostileTree.SelectMany(r => r.All("PolygonVertexIndex")).All(n => ((int[])n.P[0]).Count(i => i < 0) == 12), "FBX: polygon ends are marked negative (12 triangles)");
            Check(obj != null && !obj.Obj.Contains(",") && obj.Obj.Contains("usemtl") && obj.Mtl.Contains("newmtl"), "OBJ: dots, a material per object");
            Check(obj != null && obj.Obj.Split('\n').Count(l => l.StartsWith("o ")) == 1, "OBJ: only the visible mesh object is written");
            if (obj != null) foreach (var t in obj.Textures.Values) t.Dispose();
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
        Check(ExportManager.SafeAssetName("a b/ü\\c?", "x").All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'), "asset names are file-safe");
        Check(ExportManager.SafeAssetName("???", "model") == "model" && ExportManager.SafeAssetName(null, "model") == "model", "asset name falls back");
        var painted = new SceneObject { Name = "canvas", PrimitiveType = "Canvas", Mesh = CanvasFactory.CreateSheet(4, 3) };
        painted.Material.EnsurePaintLayer(64);
        painted.Material.MarkPaintDirty();
        using (var bmp = ExportManager.BakeCanvasImage(painted)) Check(bmp.Width == 64 && bmp.Height == 64, "canvas image is baked at the sheet's own resolution");
        var withPaint = ExportManager.BuildOBJ(new[] { painted }, "m.mtl");
        Check(withPaint.Textures.Count == 1 && withPaint.Mtl.Contains("map_Kd"), "a painted canvas exports its texture");
        foreach (var t in withPaint.Textures.Values) t.Dispose();

        // ── Export: STL (binary), OBJ materials and normals, FBX structure ──
        {
            var prims = new List<SceneObject>();
            foreach (PrimitiveKind k in Enum.GetValues(typeof(PrimitiveKind)))
                prims.Add(new SceneObject { Name = k.ToString(), PrimitiveType = k.ToString(), Mesh = PrimitiveFactory.Create(k), Position = new Vec3(prims.Count * 3f, 0, 0) });
            foreach (var po in prims)
            {
                byte[] bin = ExportManager.ToBinarySTL(new[] { po }, true);
                int count = (int)BitConverter.ToUInt32(bin, 80);
                bool sized = bin.Length == 84 + 50 * count && count > 0;
                // weld by position, then every directed edge must be met by its reverse exactly once
                var weld = new Dictionary<long, int>(); var edges = new Dictionary<long, int>();
                Func<int, int, int> P = (t, v) =>
                {
                    int o = 84 + 50 * t + 12 + 12 * v; long key = 0;
                    for (int ax = 0; ax < 3; ax++) key = key * 100003L + (long)Math.Round(BitConverter.ToSingle(bin, o + 4 * ax) * 500f);
                    int id; if (!weld.TryGetValue(key, out id)) { id = weld.Count; weld[key] = id; } return id;
                };
                double vol = 0; bool open = false;
                for (int t = 0; sized && t < count; t++)
                {
                    int[] w = { P(t, 0), P(t, 1), P(t, 2) };
                    for (int e = 0; e < 3; e++) { long key = (long)w[e] * 1000003L + w[(e + 1) % 3]; int n; edges.TryGetValue(key, out n); edges[key] = n + 1; }
                    Func<int, Vec3> V = v => { int o = 84 + 50 * t + 12 + 12 * v; return new Vec3(BitConverter.ToSingle(bin, o), BitConverter.ToSingle(bin, o + 4), BitConverter.ToSingle(bin, o + 8)); };
                    vol += Vec3.Dot(V(0), Vec3.Cross(V(1), V(2))) / 6.0;
                }
                foreach (var kv in edges)
                {
                    long a2 = kv.Key / 1000003L, b2 = kv.Key % 1000003L;
                    int rev; if (kv.Value != 1 || !edges.TryGetValue(b2 * 1000003L + a2, out rev) || rev != 1) { open = true; break; }
                }
                Check(sized && !open && vol > 0, "binary STL of " + po.Name + " is watertight with positive volume (" + count + " triangles)");
            }
            var sheet = new SceneObject { Name = "sheet", PrimitiveType = "Canvas", Mesh = CanvasFactory.CreateSheet(4, 3) };
            var withSheet = new List<SceneObject>(prims) { sheet };
            Check(ExportManager.HasCanvas(withSheet) && !ExportManager.HasCanvas(prims), "the canvas sheet is detected");
            Check(ExportManager.ToBinarySTL(withSheet, true).Length == ExportManager.ToBinarySTL(prims, true).Length + 100
               && ExportManager.ToBinarySTL(withSheet, false).Length == ExportManager.ToBinarySTL(prims, true).Length, "STL can leave the canvas sheet out (2 triangles)");
            Check(ExportManager.ToSTL(withSheet, false).Split(new[] { "facet normal" }, StringSplitOptions.None).Length == ExportManager.ToSTL(prims).Split(new[] { "facet normal" }, StringSplitOptions.None).Length, "ASCII STL honours the same switch");
            var hid = new SceneObject { Name = "h", Mesh = PrimitiveFactory.CreateCube(1f), IsVisible = false };
            Check(ExportManager.ToBinarySTL(new[] { hid }, true).Length == 84, "a hidden object is not in the binary STL");

            // OBJ: smooth normals shared on a sphere, hard ones on a cube
            Func<SceneObject, ObjExport> build = ob => ExportManager.BuildOBJ(new[] { ob }, "m.mtl");
            Func<ObjExport, int> vn = r => r.Obj.Split('\n').Count(l => l.StartsWith("vn "));
            var oSphere = build(prims[1]); var oCube = build(prims[0]); var oTet = build(prims[4]);
            int sphereTris = oSphere.Obj.Split('\n').Count(l => l.StartsWith("f "));
            Check(vn(oSphere) < sphereTris, "OBJ sphere: normals are shared between triangles (" + vn(oSphere) + " vn for " + sphereTris + " triangles)");
            Check(vn(oCube) == 6, "OBJ cube: six hard normals (" + vn(oCube) + ")");
            Check(vn(oTet) == 4, "OBJ tetrahedron: four hard normals (" + vn(oTet) + ")");
            bool unit = oSphere.Obj.Split('\n').Where(l => l.StartsWith("vn ")).All(l =>
            { var q = l.Split(' '); double x = double.Parse(q[1], CultureInfo.InvariantCulture), y = double.Parse(q[2], CultureInfo.InvariantCulture), z = double.Parse(q[3], CultureInfo.InvariantCulture); return Math.Abs(x * x + y * y + z * z - 1) < 1e-3; });
            Check(unit, "OBJ normals are unit length");

            // MTL carries the material
            var shiny = new SceneObject { Name = "shiny", PrimitiveType = "Cube", Mesh = PrimitiveFactory.CreateCube(1f) };
            shiny.Material.Color = Color.FromArgb(255, 200, 100, 50);
            shiny.Material.Opacity = 0.4f; shiny.Material.Metallic = 1f; shiny.Material.Roughness = 0.2f;
            shiny.Material.EmissionColor = Color.FromArgb(255, 0, 255, 0); shiny.Material.EmissionStrength = 0.5f;
            var oShiny = build(shiny);
            Func<string, string> line = key => oShiny.Mtl.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(key + " "));
            Check(line("d") == "d 0.400000", "MTL: d follows Opacity");
            Check(line("Ks") == "Ks 0.784314 0.392157 0.196078", "MTL: Ks of a metal takes the surface colour");
            Check(line("Ns").StartsWith("Ns 640.0"), "MTL: Ns follows Roughness");
            Check(line("Ke") == "Ke 0.000000 0.500000 0.000000", "MTL: Ke is the emission colour times strength");
            var plain = build(prims[0]);
            Check(plain.Mtl.Contains("d 1.000000") && plain.Mtl.Contains("Ks 0.04") , "MTL: an ordinary material is opaque with a faint highlight");

            // FBX has every part an importer needs
            var fxTree = ParseFbx(ExportManager.ToFBX(prims));
            Check(fxTree != null, "FBX: binary file with the right header, node offsets and footer");
            Func<string, List<FNode>> all = nm => fxTree.SelectMany(r => r.All(nm)).Concat(fxTree.Where(r => r.Name == nm)).ToList();
            Check(fxTree.Any(r => r.Name == "FBXHeaderExtension") && fxTree.Any(r => r.Name == "GlobalSettings") && fxTree.Any(r => r.Name == "Definitions") && fxTree.Any(r => r.Name == "Objects") && fxTree.Any(r => r.Name == "Connections"), "FBX: header, settings, definitions, objects, connections");
            var fxObjs = fxTree.First(r => r.Name == "Objects");
            Check(fxObjs.K.Count(k => k.Name == "Model") == 8 && fxObjs.K.Count(k => k.Name == "Geometry") == 8 && fxObjs.K.Count(k => k.Name == "Material") == 8, "FBX: a model, geometry and material per primitive");
            var fxConn = fxTree.First(r => r.Name == "Connections").K;
            var modelIds = new HashSet<long>(fxObjs.K.Where(k => k.Name == "Model").Select(k => (long)k.P[0]));
            Check(fxConn.Count == 24 && fxConn.Count(c => (long)c.P[2] == 0L && modelIds.Contains((long)c.P[1])) == 8 && fxConn.Count(c => modelIds.Contains((long)c.P[2])) == 16, "FBX: three connections per object, models to the root");
            Check(fxObjs.K.Where(k => k.Name == "Geometry").All(g => g.One("LayerElementNormal") != null && g.One("LayerElementUV") != null && g.One("LayerElementMaterial") != null && g.One("Layer") != null), "FBX: normals, UVs and material layers");
            var gs = fxTree.First(r => r.Name == "GlobalSettings").One("Properties70").K;
            Check(gs.Any(p => (string)p.P[0] == "UpAxis" && (int)p.P[4] == 1), "FBX: Y up");
            var spun = new SceneObject { Name = "spun", Mesh = PrimitiveFactory.CreateCube(1f), Position = new Vec3(1, 2, 3), Rotation = new Vec3(10, 20, 30), Scale = new Vec3(2, 3, 4) };
            var spunTree = ParseFbx(ExportManager.ToFBX(new[] { spun }));
            Func<string, double[]> lcl = nm => { var p = spunTree.First(r => r.Name == "Objects").One("Model").One("Properties70").K.First(q => (string)q.P[0] == nm); return new[] { (double)p.P[4], (double)p.P[5], (double)p.P[6] }; };
            Check(lcl("Lcl Translation").SequenceEqual(new[] { 1.0, 2, 3 }) && lcl("Lcl Rotation").SequenceEqual(new[] { 10.0, 20, 30 }) && lcl("Lcl Scaling").SequenceEqual(new[] { 2.0, 3, 4 }), "FBX: the model node carries position, rotation and scale");
            var emptyTree = ParseFbx(ExportManager.ToFBX(new SceneObject[0]));
            Check(emptyTree != null && emptyTree.Any(r => r.Name == "Connections") && !emptyTree.First(r => r.Name == "Objects").K.Any(), "FBX of an empty scene is still a valid file");
            var shMat = ParseFbx(ExportManager.ToFBX(new[] { shiny })).First(r => r.Name == "Objects").One("Material").One("Properties70").K;
            Func<string, double> mp = nm => (double)shMat.First(q => (string)q.P[0] == nm).P[4];
            Check(Near((float)mp("Opacity"), 0.4f) && Near((float)mp("Shininess"), 640f, 0.1f) && Near((float)mp("SpecularColor"), 200 / 255f), "FBX: material carries opacity, shininess and specular colour");

            // atomic writing
            string adir = Path.Combine(Path.GetTempPath(), "aplat-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(adir);
            try
            {
                string target = Path.Combine(adir, "m.stl");
                ExportManager.WriteFileAtomic(target, st => st.Write(new byte[] { 1, 2, 3 }, 0, 3));
                Check(File.ReadAllBytes(target).Length == 3 && !File.Exists(target + ".tmp"), "atomic write: new file, no temp left");
                bool threw = false;
                try { ExportManager.WriteFileAtomic(target, st => { st.WriteByte(9); throw new IOException("disk full"); }); } catch (IOException) { threw = true; }
                Check(threw && File.ReadAllBytes(target).Length == 3 && !File.Exists(target + ".tmp"), "atomic write: a failure keeps the old file and leaves no temp");
                ExportManager.WriteFileAtomic(target, st => st.Write(new byte[] { 7, 7 }, 0, 2));
                Check(File.ReadAllBytes(target).Length == 2 && !File.Exists(target + ".tmp"), "atomic write: replaces an existing file");
            }
            finally { try { Directory.Delete(adir, true); } catch { } }
        }

        // ── Hostile scene files go through the real loader ──
        string dir = Path.Combine(Path.GetTempPath(), "aplat-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scene = new Scene { Name = "hostile" };
            var broken = new SceneObject { Name = "broken", Mesh = new Mesh() };
            broken.Mesh.Vertices.Add(Vec3.Zero);
            broken.Mesh.Faces.Add(new[] { 0, 5, 9 });
            scene.Objects.Add(broken);
            var deep = new SceneObject { Name = "deep", IsGroup = true };
            var cur = deep;
            for (int i = 0; i < 120; i++) { var nx = new SceneObject { Name = "n" + i, IsGroup = true }; cur.Children.Add(nx); cur = nx; }
            scene.Objects.Add(deep);
            string path = Path.Combine(dir, "h.aplat");
            SceneFileManager.Save(scene, path);
            Scene back = null;
            NoThrow(() => back = SceneFileManager.Load(path), "a scene with bad faces and 120 nested groups loads");
            Check(back != null && back.Objects[0].Mesh.Faces.Count == 0, "bad faces were dropped on load");
            int depth = 0; var d = back == null ? null : back.Objects[1];
            while (d != null && d.Children.Count > 0) { depth++; d = d.Children[0]; }
            Check(depth <= 66, "nesting is cut off at a sane depth (" + depth + ")");
            var nanObj = new SceneObject { Name = "nan", Mesh = PrimitiveFactory.CreateCube(1f), Position = new Vec3(float.NaN, 0, 0), Scale = new Vec3(float.PositiveInfinity, 1, 1) };
            nanObj.Mesh.Vertices[0] = new Vec3(float.NaN, 1, 1);
            var nanScene = new Scene(); nanScene.Objects.Add(nanObj);
            string nanPath = Path.Combine(dir, "nan.aplat");
            SceneFileManager.Save(nanScene, nanPath);
            var nanBack = SceneFileManager.Load(nanPath);
            var no = nanBack.Objects[0];
            Check(Finite(no.Position.X) && Finite(no.Scale.X) && Finite(no.Mesh.Vertices[0].X), "NaN / infinity in a file's transform and mesh is replaced by finite values");
            string xml = "<?xml version=\"1.0\"?><Scene xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Objects><SceneObject><Name>x</Name><Mesh xsi:nil=\"true\"/><Material xsi:nil=\"true\"/><Children xsi:nil=\"true\"/></SceneObject><SceneObject xsi:nil=\"true\"/></Objects></Scene>";
            var m2 = typeof(SceneFileManager).GetMethod("DeserializeScene", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Scene hs = null;
            NoThrow(() => hs = (Scene)m2.Invoke(null, new object[] { new StringReader(xml) }), "xsi:nil mesh / material / children / object deserialise");
            Check(hs != null && hs.Objects.Count == 1 && hs.Objects[0].Mesh != null && hs.Objects[0].Material != null && hs.Objects[0].Children != null, "...and are replaced by empty ones, the nil object is dropped");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }


        // ── Vec3, Mesh, Scene and the sun: the plain helpers ──
        {
            var v = new Vec3(3, 4, 12);
            Check(Near(v.Get(0), 3) && Near(v.Get(1), 4) && Near(v.Get(2), 12), "Vec3.Get reads each axis");
            Check(v.With(0, 9).X == 9 && v.With(1, 9).Y == 9 && v.With(2, 9).Z == 9 && v.With(1, 9).X == 3, "Vec3.With replaces one axis only");
            Check(Near(v.Length, 13) && Near(v.Normalized().Length, 1f), "a vector normalises to length 1");
            Check(Vec3.Zero.Normalized().Length == 0f, "the zero vector stays zero when normalised");
            var sum = new Vec3(1, 2, 3) + new Vec3(1, 1, 1); var diff = new Vec3(1, 2, 3) - new Vec3(1, 1, 1);
            Check(sum.Z == 4 && diff.Y == 1 && (new Vec3(1, 2, 3) * 2f).Y == 4 && (2f * new Vec3(1, 2, 3)).Z == 6, "Vec3 + - and scalar * work both ways");
            Check(Near(Vec3.Dot(new Vec3(1, 0, 0), new Vec3(0, 1, 0)), 0) && Near(Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)).Z, 1), "dot and cross of the unit axes");

            var mesh = new Mesh { Vertices = null, Faces = null };
            Check(mesh.Sanitize() == 0 && mesh.Vertices != null && mesh.Faces != null, "Sanitize gives a mesh with null lists empty lists");
            var m2 = PrimitiveFactory.CreateCube(1f);
            m2.Vertices[1] = new Vec3(float.PositiveInfinity, 0, 0);
            m2.Faces.Add(null); m2.Faces.Add(new[] { 0, 1 }); m2.Faces.Add(new[] { 0, 1, 99 });
            Check(m2.Sanitize() == 3 && Finite(m2.Vertices[1].X), "Sanitize drops null, short and out-of-range faces and fixes a bad vertex");
            var copy = m2.Clone(); copy.Faces[0][0] = 5;
            Check(copy.Vertices.Count == m2.Vertices.Count && m2.Faces[0][0] != 5, "a cloned mesh does not share its faces");

            var sc = new Scene();
            sc.BackgroundColor = Color.Red; sc.GridColor = Color.Blue;
            Check(sc.BackgroundColor.ToArgb() == Color.Red.ToArgb() && sc.GridColor.ToArgb() == Color.Blue.ToArgb(), "scene colours round-trip through their ARGB fields");
            var sun = sc.Light; sun.Color = Color.Orange; sun.AmbientColor = Color.Gray;
            Check(sun.Color.ToArgb() == Color.Orange.ToArgb() && sun.AmbientColor.ToArgb() == Color.Gray.ToArgb(), "sun colours round-trip");
            Check(Near(sun.Direction.Length, 1f), "the sun direction is a unit vector");
            sun.Elevation = 1f; Check(!sun.AboveHorizon, "a sun at 1° is below the horizon cut-off");
            sun.Elevation = 40f; Check(sun.AboveHorizon, "a sun at 40° is above it");
            foreach (float h in new[] { -5f, 0f, 6f, 12f, 18f, 24f, 30f })
            {
                sun.ApplyTimeOfDay(h);
                Check(Finite(sun.Azimuth) && sun.Elevation >= -10f && sun.Elevation <= 90f && sun.Ambient >= 0.1f, "time of day " + h + " h gives a sane sun (el " + sun.Elevation.ToString("0.#", CultureInfo.InvariantCulture) + ")");
            }
            sun.ApplyTimeOfDay(12f); float noon = sun.Elevation; sun.ApplyTimeOfDay(6f);
            Check(noon > sun.Elevation + 30f, "noon is much higher than sunrise");
            var sunCopy = sun.Clone(); sunCopy.Azimuth = 1f;
            Check(sunCopy.Intensity == sun.Intensity && sun.Azimuth != 1f, "a cloned sun is independent");
        }

        // ── OBJ export of a textured, painted, non-canvas shape (the baked atlas) ──
        {
            var texCube = new SceneObject { Name = "tex", PrimitiveType = "Cube", Mesh = PrimitiveFactory.CreateCube(1f), Material = new Material { Color = Color.Gray } };
            using (var bmp = new Bitmap(16, 16)) using (var tg = Graphics.FromImage(bmp)) using (var ms = new MemoryStream())
            {
                tg.Clear(Color.Orange); tg.FillRectangle(Brushes.Blue, 0, 0, 8, 8); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                texCube.Material.TextureBase64 = Convert.ToBase64String(ms.ToArray());
            }
            texCube.Material.EnsurePaintLayer(Material.PaintSize);
            using (var tg = Graphics.FromImage(texCube.Material.PaintLayer)) tg.FillEllipse(Brushes.Red, 10, 10, 200, 200);
            // a degenerate face (all vertices equal) must be skipped, not crash the bake
            texCube.Mesh.Vertices.Add(Vec3.Zero); int z = texCube.Mesh.Vertices.Count - 1; texCube.Mesh.Faces.Add(new[] { z, z, z });
            ObjExport ex = null;
            NoThrow(() => ex = ExportManager.BuildOBJ(new[] { texCube }, "t.mtl"), "OBJ export of a textured and painted texCube");
            Check(ex != null && ex.Obj.Contains("vt ") && ex.Mtl.Length > 0 && ex.Textures.Count == 1, "...has texture coordinates, a material and one baked atlas");
            if (ex != null) foreach (var tb in ex.Textures.Values) tb.Dispose();
        }

        // ── Shape editing (Models.MeshEditor): weld, pick, move, guards, file, export ──
        {
            Func<SceneObject, bool> meClosed = po =>
            {
                byte[] bin = ExportManager.ToBinarySTL(new[] { po }, true);
                int count = (int)BitConverter.ToUInt32(bin, 80);
                if (bin.Length != 84 + 50 * count || count == 0) return false;
                var weld = new Dictionary<long, int>(); var edges = new Dictionary<long, int>();
                Func<int, int, int> P = (t, v) =>
                {
                    int o = 84 + 50 * t + 12 + 12 * v; long key = 0;
                    for (int ax = 0; ax < 3; ax++) key = key * 100003L + (long)Math.Round(BitConverter.ToSingle(bin, o + 4 * ax) * 500f);
                    int id; if (!weld.TryGetValue(key, out id)) { id = weld.Count; weld[key] = id; } return id;
                };
                for (int t = 0; t < count; t++)
                {
                    int[] w = { P(t, 0), P(t, 1), P(t, 2) };
                    for (int e = 0; e < 3; e++) { long key = (long)w[e] * 1000003L + w[(e + 1) % 3]; int n; edges.TryGetValue(key, out n); edges[key] = n + 1; }
                }
                foreach (var kv in edges)
                {
                    long a2 = kv.Key / 1000003L, b2 = kv.Key % 1000003L;
                    int rev; if (kv.Value != 1 || !edges.TryGetValue(b2 * 1000003L + a2, out rev) || rev != 1) return false;
                }
                return true;
            };
            Func<Mesh, Mesh, bool> meSameTopology = (x, y) =>
            {
                if (x.Vertices.Count != y.Vertices.Count || x.Faces.Count != y.Faces.Count) return false;
                for (int i = 0; i < x.Faces.Count; i++) if (!x.Faces[i].SequenceEqual(y.Faces[i])) return false;
                return true;
            };
            // an orthographic "camera" looking along -Z: x right, y up, nearer = larger z; 100 px per unit
            int meIdx0;
            Func<Vec3, Vec3> meView = v => new Vec3(200f + v.X * 100f, 200f - v.Y * 100f, v.Z);

            var meCube = PrimitiveFactory.CreateCube(1f);
            var meEd = new MeshEditor(meCube);
            int meShown = 0; for (int e = 0; e < meEd.EdgeCount; e++) if (meEd.EdgeShown(e)) meShown++;
            Check(meEd.PointCount == 8 && meShown == 12 && meEd.EdgeCount == 18, "cube: 8 points, 12 edges shown (the 6 diagonals of its quads are inside flat patches)");
            // a cube whose corners are separate vertices per face (24 vertices, 6 quads)
            Func<Mesh> meSplit = () =>
            {
                var m = new Mesh(); float h = 0.5f;
                Vec3[][] sides = {
                    new[] { new Vec3(-h,-h,-h), new Vec3(-h, h,-h), new Vec3( h, h,-h), new Vec3( h,-h,-h) },
                    new[] { new Vec3( h,-h, h), new Vec3( h, h, h), new Vec3(-h, h, h), new Vec3(-h,-h, h) },
                    new[] { new Vec3(-h,-h, h), new Vec3(-h, h, h), new Vec3(-h, h,-h), new Vec3(-h,-h,-h) },
                    new[] { new Vec3( h,-h,-h), new Vec3( h, h,-h), new Vec3( h, h, h), new Vec3( h,-h, h) },
                    new[] { new Vec3(-h, h,-h), new Vec3(-h, h, h), new Vec3( h, h, h), new Vec3( h, h,-h) },
                    new[] { new Vec3(-h,-h, h), new Vec3(-h,-h,-h), new Vec3( h,-h,-h), new Vec3( h,-h, h) } };
                foreach (var side in sides) { int b = m.Vertices.Count; m.Vertices.AddRange(side); m.Faces.Add(new[] { b, b + 1, b + 2, b + 3 }); }
                return m;
            };
            var meSp = meSplit(); var meSpE = new MeshEditor(meSp);
            Check(meSp.Vertices.Count == 24 && meSpE.PointCount == 8 && meSpE.VertexCountOf(0) == 3 && meSpE.EdgeCount == 12, "a cube of 24 separate vertices welds into 8 points of 3 vertices");
            var meSp0 = meSp.Clone();
            meSpE.SelectPoint(2); meSpE.BeginDrag(); meSpE.MoveBy(new Vec3(0.2f, 0.3f, 0.4f)); meSpE.EndDrag();
            int meSpMoved = 0; for (int i = 0; i < 24; i++) if (!Near((meSp.Vertices[i] - meSp0.Vertices[i]).Length, 0, 1e-6f)) meSpMoved++;
            Check(meSpMoved == 3, "dragging one corner of the split cube moves all 3 of its vertices (the cube does not tear)");
            Check(meClosed(new SceneObject { Mesh = meSp }), "...and the split cube stays watertight in STL");
            Check(meSpE.Pick(v => new Vec3(200f + v.X * 100f, 200f - v.Y * 100f, v.Z), 170f, 190f, 10f, 7f, out meIdx0) == MeshPick.Face, "a quad face of the split cube is pickable");
            meSpE.SelectFace(meIdx0);
            Check(meSpE.PickedCount == 4 && meSpE.PatchCount == 1, "a quad picks its four points");
            var meSphere = PrimitiveFactory.CreateSphere(1f, 12, 8);
            var meSE = new MeshEditor(meSphere);
            int meSeam = 0; for (int p = 0; p < meSE.PointCount; p++) if (meSE.VertexCountOf(p) > 1) meSeam++;
            Check(meSE.PointCount < meSphere.Vertices.Count && meSeam > 0, "sphere: the seam and poles are welded (" + meSphere.Vertices.Count + " vertices, " + meSE.PointCount + " points)");

            // a point moves with every vertex it welds, nothing else moves, topology is unchanged
            var meBefore = meCube.Clone();
            meEd.SelectPoint(0);
            Vec3 meP0 = meEd.PointAt(0);
            Check(meEd.BeginDrag() && meEd.MoveBy(new Vec3(0.25f, 0.5f, -0.125f)), "a picked point can be dragged");
            int meMoved = 0, meWrong = 0;
            for (int i = 0; i < meCube.Vertices.Count; i++)
            {
                bool was = Near((meBefore.Vertices[i] - meP0).Length, 0, 1e-4f);
                bool now = !Near((meCube.Vertices[i] - meBefore.Vertices[i]).Length, 0, 1e-6f);
                if (now) meMoved++;
                if (was != now) meWrong++;
            }
            Check(meMoved == 1 && meWrong == 0, "...only that vertex moved");
            Check(meSameTopology(meCube, meBefore), "...the vertex and face lists keep their size and order");
            meEd.MoveBy(new Vec3(1, 0, 0));
            Check(Near(meEd.PointAt(0).X, meP0.X + 1) && Near(meEd.PointAt(0).Y, meP0.Y), "a repeated move is from the start of the drag, not cumulative");
            meEd.CancelDrag();
            Check(meCube.Vertices.SequenceEqual(meBefore.Vertices), "cancelling a drag restores the vertices");

            // an edge and a face
            meEd.SelectEdge(0);
            Check(meEd.Kind == MeshPick.Edge && meEd.PickedCount == 2, "an edge picks its two points");
            meEd.BeginDrag(); meEd.MoveBy(new Vec3(0, 1, 0)); meEd.EndDrag();
            int meUp = 0; for (int i = 0; i < meCube.Vertices.Count; i++) if (Near(meCube.Vertices[i].Y - meBefore.Vertices[i].Y, 1)) meUp++;
            Check(meUp == 2 && meSameTopology(meCube, meBefore), "an edge drag moves both its points with their welded vertices");
            meCube = PrimitiveFactory.CreateCube(1f); meEd = new MeshEditor(meCube); meBefore = meCube.Clone();
            meEd.SelectFace(0);
            Check(meEd.Kind == MeshPick.Face && meEd.PickedCount == 4 && meEd.PatchCount == 2, "a click on one triangle of a cube side picks the whole flat side: 2 triangles, 4 points");
            meEd.BeginDrag(); meEd.MoveBy(new Vec3(0.3f, 0.2f, 0.1f)); meEd.EndDrag();
            Check(meCube.Faces[0].Concat(meCube.Faces[1]).All(i => Near((meCube.Vertices[i] - meBefore.Vertices[i]).Length, new Vec3(0.3f, 0.2f, 0.1f).Length)), "a face drag moves all its corners by the same offset");
            var meObj = new SceneObject { Name = "e", PrimitiveType = "Cube", Mesh = meCube };
            Check(meClosed(meObj), "an edited cube stays watertight in binary STL");
            var meExp = ExportManager.BuildOBJ(new[] { meObj }, "m.mtl");
            Check(meExp.Obj.Contains((0.5f + 0.2f).ToString("F6", CultureInfo.InvariantCulture)), "OBJ export reads the edited mesh");
            Check(ParseFbx(ExportManager.ToFBX(new[] { meObj })).SelectMany(r => r.All("Vertices")).Any(v => ((double[])v.P[0]).Any(d => Math.Abs(d - 0.7) < 1e-4)), "FBX export reads the edited mesh");

            // NaN / infinity / runaway deltas
            var meG = PrimitiveFactory.CreateCube(1f); var meGE = new MeshEditor(meG); var meG0 = meG.Clone();
            meGE.SelectPoint(3); meGE.BeginDrag();
            Check(!meGE.MoveBy(new Vec3(float.NaN, 0, 0)) && !meGE.MoveBy(new Vec3(0, float.PositiveInfinity, 0)) && meG.Vertices.SequenceEqual(meG0.Vertices), "a NaN or infinite delta is ignored");
            meGE.MoveBy(new Vec3(1e30f, -1e30f, 5f));
            Check(meG.Vertices.All(v => Finite(v.X) && Finite(v.Y) && Finite(v.Z) && Math.Abs(v.X) <= MeshEditor.MaxCoord && Math.Abs(v.Y) <= MeshEditor.MaxCoord), "a runaway delta is clamped");
            meGE.CancelDrag();
            Check(!meGE.MoveBy(new Vec3(1, 0, 0)) && !new MeshEditor(meG).BeginDrag(), "no drag without a drag started or a pick");

            // collapsing a face to zero area: allowed, finite everywhere, nothing throws
            var meC = PrimitiveFactory.CreateCube(1f); var meCE = new MeshEditor(meC);
            meCE.SelectFace(0); int meA = meCE.PickedPoint(0), meB = meCE.PickedPoint(1);
            meCE.SelectPoint(meB); meCE.BeginDrag(); meCE.MoveBy(meCE.PointAt(meA) - meCE.PointAt(meB)); meCE.EndDrag();
            var meCO = new SceneObject { Name = "c", PrimitiveType = "Cube", Mesh = meC };
            NoThrow(() => { ExportManager.ToBinarySTL(new[] { meCO }, true); ExportManager.ToFBX(new[] { meCO }); ExportManager.BuildOBJ(new[] { meCO }, "m.mtl"); new MeshEditor(meC); }, "a collapsed edge exports and re-welds without throwing");
            Check(meC.Vertices.All(v => Finite(v.X) && Finite(v.Y) && Finite(v.Z)), "...and every vertex stays finite");

            // picking in screen space: point, then edge, then face; nearest first, nearer wins ties
            var meK = PrimitiveFactory.CreateCube(1f); var meKE = new MeshEditor(meK); int meIdx;
            Check(meKE.Pick(meView, 250f, 150f, 10f, 7f, out meIdx) == MeshPick.Vertex && Near(Math.Abs(meKE.PointAt(meIdx).X), 0.5f) && meKE.PointAt(meIdx).Z > 0, "a click near a corner picks that vertex (the nearer one where front and back overlap)");
            Check(meKE.Pick(meView, 200f, 153f, 10f, 7f, out meIdx) == MeshPick.Edge, "a click near the middle of a side picks the edge");
            var meKind = meKE.Pick(meView, 170f, 190f, 10f, 7f, out meIdx);
            Check(meKind == MeshPick.Face && meK.Faces[meIdx].All(i => meK.Vertices[i].Z > 0.4f), "a click inside picks the face towards the viewer");
            Check(meKE.Pick(meView, 20f, 20f, 10f, 7f, out meIdx) == MeshPick.None && meIdx == -1, "a click on nothing picks nothing");
            Check(meKE.Pick(v => new Vec3(-99999f, -99999f, 0f), 200f, 200f, 10f, 7f, out meIdx) == MeshPick.None, "points behind the camera cannot be picked");
            Check(meKE.Pick(meView, 200f, 215f, 10f, 7f, out meIdx) == MeshPick.Face, "a click away from every edge lands on a face");

            // seen at a slant, a cube shows three sides: seven corners can be picked, the eighth is behind
            Func<Vec3, Vec3> meSlant = v => new Vec3(200f + (v.X + 0.3f * v.Z) * 100f, 200f - (v.Y + 0.2f * v.Z) * 100f, v.Z);
            int meVisible = 0;
            for (int p = 0; p < meKE.PointCount; p++)
            {
                Vec3 sp = meSlant(meKE.PointAt(p));
                if (meKE.Pick(meSlant, sp.X, sp.Y, 3f, 3f, out meIdx) == MeshPick.Vertex && meIdx == p) meVisible++;
            }
            Check(meVisible == 7, "a corner on the far side of the cube cannot be picked (" + meVisible + " of 8 can)");

            // world delta → local delta of a rotated, non-uniformly scaled object
            var meT = new SceneObject { Mesh = meK, Position = new Vec3(1, 2, 3), Rotation = new Vec3(30, 50, 70), Scale = new Vec3(2f, 0.5f, 3f) };
            Vec3 meWd = new Vec3(0.4f, -0.2f, 0.7f), meLd;
            bool meOk = MeshEditor.TryLocalDelta(meT, meWd, out meLd);
            Vec3 meBack = SceneGeometry.TransformVertex(new Vec3(0.2f, 0.1f, 0.3f) + meLd, meT) - SceneGeometry.TransformVertex(new Vec3(0.2f, 0.1f, 0.3f), meT);
            Check(meOk && (meBack - meWd).Length < 1e-4f, "a world drag becomes the local delta under rotation and non-uniform scale");
            meT.Scale = new Vec3(0f, 1f, 1f);
            Check(!MeshEditor.TryLocalDelta(meT, meWd, out meLd) && Finite(meLd.X), "a flattened (zero-scale) object cannot be dragged, and it is not NaN");
            Check(MeshEditor.Snap(0.74f, 0.5f) == 0.5f && MeshEditor.Snap(0.76f, 0.5f) == 1f && MeshEditor.Snap(0.3f, 0f) == 0.3f, "grid snap");

            // the editor goes stale when its mesh changes
            Check(meKE.Matches(meK) && !meKE.Matches(meK.Clone()) && !meKE.Matches(new Mesh()), "an editor matches only its own mesh");

            // save and load keep the edit; a second editor sees the same points
            string meDir = Path.Combine(Path.GetTempPath(), "aplat-me-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(meDir);
            try
            {
                var meS = PrimitiveFactory.CreateSphere(1f, 12, 8); var meSEd = new MeshEditor(meS);
                meSEd.SelectPoint(5); meSEd.BeginDrag(); meSEd.MoveBy(new Vec3(0.123457f, -0.3f, 0.77f)); meSEd.EndDrag();
                var meScene = new Scene { Name = "edited" };
                meScene.Objects.Add(new SceneObject { Name = "ball", PrimitiveType = "Sphere", Mesh = meS, Scale = new Vec3(1, 2, 1) });
                string mePath = Path.Combine(meDir, "e.aplat");
                SceneFileManager.Save(meScene, mePath);
                var meLoaded = SceneFileManager.Load(mePath).Objects[0].Mesh;
                Check(meSameTopology(meS, meLoaded) && meS.Vertices.Zip(meLoaded.Vertices, (x, y) => (x - y).Length).All(d => d < 1e-6f), "an edited mesh round-trips through .aplat exactly");
                Check(new MeshEditor(meLoaded).PointCount == meSEd.PointCount, "...and welds to the same points after loading");
                var meCl = meScene.Objects[0].Clone();
                meCl.Mesh.Vertices[0] = new Vec3(9, 9, 9);
                Check(meS.Vertices[0].X != 9f, "a copy of an edited object does not share its mesh");
            }
            finally { try { Directory.Delete(meDir, true); } catch { } }
        }

        // ── Shadow map: GL gate, map size, light fitting, casters, bias ──
        {
            string shWhy;
            Check(ShadowMath.IsSupported("4.6.0 NVIDIA 551.23", "", out shWhy), "OpenGL 4.6 is enough for shadows");
            Check(ShadowMath.IsSupported("3.0 Mesa 20.0.8", "", out shWhy), "OpenGL 3.0 is enough (framebuffers are core)");
            Check(!ShadowMath.IsSupported("1.1.0", "GL_EXT_bgra GL_EXT_framebuffer_object", out shWhy) && shWhy != null, "the Windows GDI generic GL 1.1 is not enough, with a reason");
            Check(!ShadowMath.IsSupported("2.1 Mesa 18.3", "GL_ARB_vertex_buffer_object GL_EXT_framebuffer_object", out shWhy), "GL 2.1 with only the EXT framebuffer extension is refused");
            Check(ShadowMath.IsSupported("2.1 Mesa 18.3", "GL_ARB_multitexture GL_ARB_framebuffer_object GL_ARB_shadow", out shWhy), "GL 2.1 with ARB_framebuffer_object is enough");
            Check(!ShadowMath.IsSupported("2.1 Mesa 18.3", "GL_XARB_framebuffer_object_x GL_ARB_framebuffer_object2", out shWhy), "an extension name is matched as a whole word");
            Check(!ShadowMath.IsSupported("OpenGL ES 3.2 Mesa", "", out shWhy), "OpenGL ES is not used");
            Check(!ShadowMath.IsSupported(null, null, out shWhy) && !ShadowMath.IsSupported("", "", out shWhy) && !ShadowMath.IsSupported("garbage", "", out shWhy), "a missing or garbled version string gives no shadows, not an exception");
            Check(ShadowMath.IsSupported("1.1.0", "", "force", out shWhy), "APLAT_SHADOWS=force skips the check");
            Check(!ShadowMath.IsSupported("4.6.0", "", "off", out shWhy), "APLAT_SHADOWS=off disables shadows");
            int shMa, shMi;
            Check(ShadowMath.TryParseVersion("4.6.0 NVIDIA", out shMa, out shMi) && shMa == 4 && shMi == 6 && ShadowMath.TryParseVersion(" 10.12", out shMa, out shMi) && shMa == 10 && shMi == 12, "version strings parse");

            Check(ShadowMath.ChooseMapSize(16384) == 2048 && ShadowMath.ChooseMapSize(2048) == 2048, "the map is 2048 by default");
            Check(ShadowMath.ChooseMapSize(1024) == 1024 && ShadowMath.ChooseMapSize(1500) == 1024 && ShadowMath.ChooseMapSize(512) == 512, "the map shrinks to the GPU's texture limit");
            Check(ShadowMath.ChooseMapSize(128) == 0 && ShadowMath.ChooseMapSize(0) == 0, "a GPU with tiny textures gets no map");

            Check(ShadowMath.CastsShadow(1f, false) && ShadowMath.CastsShadow(0.5f, false) && ShadowMath.CastsShadow(0.7f, false), "solid and mostly opaque shapes cast");
            Check(!ShadowMath.CastsShadow(0.49f, false) && !ShadowMath.CastsShadow(0f, false), "strongly transparent shapes (opacity below 0.5) cast nothing");
            Check(!ShadowMath.CastsShadow(1f, true), "cut-out textures cast nothing");

            var shLow = new LightSettings { Elevation = 3f }; var shHigh = new LightSettings { Elevation = 50f };
            Check(!ShadowMath.SunUsable(shLow.Direction) && ShadowMath.SunUsable(shHigh.Direction), "a sun on the horizon casts no map, a high one does");

            // bias: grows with the slope, stays tiny facing the sun, is finite everywhere
            float shB1 = ShadowMath.Bias(1f, 0.01f, 20f), shB2 = ShadowMath.Bias(0.5f, 0.01f, 20f), shB3 = ShadowMath.Bias(0.05f, 0.01f, 20f);
            Check(shB1 > 0f && shB1 < shB2 && shB2 < shB3, "the depth bias grows as a surface turns away from the sun");
            Check(shB3 <= 0.01f * (1f + 3f * 5f) / 20f + 1e-9f, "...and is capped for grazing surfaces");
            Check(Finite(ShadowMath.Bias(-1f, 0.01f, 0f)) && Finite(ShadowMath.Bias(2f, 0f, 20f)), "the bias survives out-of-range input");
            Check(Near(ShadowMath.Bias(0.7f, 0.02f, 10f), 2f * ShadowMath.Bias(0.7f, 0.01f, 10f), 1e-7f), "the bias scales with the texel size");
            Check(Near(ShadowMath.FloorStrength(0.5f, 1f), 0.5f) && ShadowMath.FloorStrength(0.5f, 0.1f) < ShadowMath.FloorStrength(0.5f, 1f) && ShadowMath.FloorStrength(2f, 1f) <= 1f, "a low sun gives a paler floor shadow, density stays in 0..1");

            // fitting: every corner of the scene, and its shadow on the floor, lands inside the map
            var shBoxes = new[] { new[] { new Vec3(-3, 0, -2), new Vec3(4, 5, 6) }, new[] { new Vec3(0, 0, 0), new Vec3(1, 1, 1) }, new[] { new Vec3(-50, -2, -50), new Vec3(60, 30, 40) } };
            foreach (float shEl in new[] { 12f, 30f, 48f, 75f, 89.5f })
                foreach (float shAz in new[] { 0f, 135f, 270f })
                    foreach (var shBox in shBoxes)
                    {
                        var shL = new LightSettings { Azimuth = shAz, Elevation = shEl };
                        Vec3 shSun = shL.Direction; float shFloor = shBox[0].Y;
                        var shFit = new ShadowMath.LightFit();
                        bool shOk = ShadowMath.Fit(shSun, shBox[0], shBox[1], shFloor, 2048, shFit);
                        bool shAll = shOk;
                        for (int shI = 0; shOk && shI < 8; shI++)
                        {
                            Vec3 shC = new Vec3((shI & 1) == 0 ? shBox[0].X : shBox[1].X, (shI & 2) == 0 ? shBox[0].Y : shBox[1].Y, (shI & 4) == 0 ? shBox[0].Z : shBox[1].Z);
                            Vec3 shG = shC - shSun * ((shC.Y - shFloor) / shSun.Y);        // its shadow on the floor
                            foreach (Vec3 shP in new[] { shC, shG })
                            {
                                double shX, shY, shZ; ShadowMath.Project(shFit, shP, out shX, out shY, out shZ);
                                if (Math.Abs(shX) > 1.0001 || Math.Abs(shY) > 1.0001 || shZ < -1.0001 || shZ > 1.0001) shAll = false;
                            }
                            if (shG.X < shFit.FloorMinX - 1e-3f || shG.X > shFit.FloorMaxX + 1e-3f || shG.Z < shFit.FloorMinZ - 1e-3f || shG.Z > shFit.FloorMaxZ + 1e-3f) shAll = false;
                        }
                        Check(shAll, "the sun frustum holds the scene and its floor shadow (sun " + shEl + "°/" + shAz + "°, box " + shBox[1] + ")");
                    }
            {
                var shFit2 = new ShadowMath.LightFit();
                Vec3 shSun2 = new LightSettings { Azimuth = 135f, Elevation = 48f }.Direction;
                ShadowMath.Fit(shSun2, new Vec3(-2, 0, -2), new Vec3(2, 3, 2), 0f, 2048, shFit2);
                double shAx, shAy, shAz, shBx, shBy, shBz;
                ShadowMath.Project(shFit2, new Vec3(0, 3, 0), out shAx, out shAy, out shAz);
                ShadowMath.Project(shFit2, new Vec3(0, 3, 0) + shSun2 * 1f, out shBx, out shBy, out shBz);
                Check(Near((float)shBz, (float)shAz - 2f / shFit2.Span, 1e-4f) && Near((float)shBx, (float)shAx, 1e-4f) && Near((float)shBy, (float)shAy, 1e-4f), "moving towards the sun changes only the depth, and by 1 / span");
                Check(shFit2.Texel > 0f && shFit2.Texel < 0.02f && shFit2.Span > 5f, "texel and depth range are sensible for a small scene (" + shFit2.Texel + ", " + shFit2.Span + ")");
                Check(!ShadowMath.Fit(new Vec3(0.99f, 0.05f, 0f).Normalized(), new Vec3(0, 0, 0), new Vec3(1, 1, 1), 0f, 2048, shFit2) && !ShadowMath.Fit(shSun2, Vec3.Zero, Vec3.One, 0f, 0, shFit2), "a too-low sun or an empty map size does not fit");
                Check(ShadowMath.Fit(new Vec3(0f, 1f, 0f), new Vec3(-1, 0, -1), new Vec3(1, 1, 1), 0f, 1024, shFit2) && Finite(shFit2.Texel), "a sun straight overhead fits (no degenerate basis)");
                Check(ShadowMath.Fit(shSun2, new Vec3(1, 1, 1), new Vec3(1, 1, 1), 1f, 512, shFit2) && shFit2.Texel > 0f && shFit2.Span > 0f, "a single point still gives a usable frustum");
            }

            // the switch is in the scene file: absent in an old file → on; off survives the round trip
            Check(new Scene().Light.Shadows, "shadows are on in a new scene");
            Check(new LightSettings { Shadows = false }.Clone().Shadows == false, "Clone keeps the shadow switch");
            string shDir = Path.Combine(Path.GetTempPath(), "aplat-sh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(shDir);
            try
            {
                var shS = new Scene { Name = "shadows" };
                shS.Light.Shadows = false; shS.Light.ShadowStrength = 0.7f;
                string shPath = Path.Combine(shDir, "s.aplat");
                SceneFileManager.Save(shS, shPath);
                var shBack = SceneFileManager.Load(shPath);
                Check(!shBack.Light.Shadows && Near(shBack.Light.ShadowStrength, 0.7f), "the shadow switch and density round-trip through .aplat");
                var shSer = new System.Xml.Serialization.XmlSerializer(typeof(LightSettings));
                string shXml;
                using (var shSw = new StringWriter()) { shSer.Serialize(shSw, new LightSettings { Shadows = false }); shXml = shSw.ToString(); }
                Check(shXml.Contains("<Shadows>false</Shadows>"), "...as <Shadows> in the Light element");
                string shOldXml = System.Text.RegularExpressions.Regex.Replace(shXml, "<Shadows>false</Shadows>\\s*", "");
                Check(!shOldXml.Contains("<Shadows>"), "(the old-format element really has no <Shadows> field)");
                LightSettings shOld;
                using (var shSr = new StringReader(shOldXml)) shOld = (LightSettings)shSer.Deserialize(shSr);
                Check(shOld.Shadows, "a Light element without the <Shadows> field reads with shadows on");
            }
            finally { try { Directory.Delete(shDir, true); } catch { } }
        }

        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
