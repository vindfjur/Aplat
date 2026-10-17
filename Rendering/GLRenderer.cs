using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Rendering
{
    // GPU renderer (OpenTK / OpenGL, fixed-function immediate mode): real depth
    // buffer (correct occlusion), perspective-correct textures and good
    // performance. Settings (grid, brightness, wireframe…) are read from the
    // existing SceneGeometry instance so there is a single source of truth.
    //
    // Lighting: the scene carries a real directional source (the "sun", see
    // Models.LightSettings). It enables the OpenGL fixed-function light — diffuse by
    // the normal, a specular highlight from the material's PBR parameters (metalness /
    // roughness) — and casts shadows with a shadow map (GLRenderer.Shadows.cs).
    // With the light off, the renderer falls back to the old uniform Shade() shading.
    public partial class GLRenderer
    {
        private readonly Camera _cam;
        private static readonly Vec3 LightDir = new Vec3(0.5f, 1f, -0.7f).Normalized();
        private static readonly LightSettings NoLight = new LightSettings { Enabled = false };

        // GPU texture caches.
        private readonly HashSet<string> _fileSeen = new HashSet<string>();
        private readonly Dictionary<string, int>   _fileTex   = new Dictionary<string, int>();
        private readonly Dictionary<Material, int> _paintTex  = new Dictionary<Material, int>();
        private readonly Dictionary<Material, int> _paintVer  = new Dictionary<Material, int>();
        // Materials whose paint texture was used this frame; entries not in this set
        // belong to dead Material instances (every undo/redo/open builds new ones) and
        // their GL textures would otherwise leak GPU memory for the whole session.
        private readonly HashSet<Material> _paintSeen = new HashSet<Material>();
        private readonly List<Material>    _paintDead = new List<Material>();

        // Smoothed normals (one per vertex of each face) are computed once per mesh and
        // cached: without them a sphere / torus under real light looks faceted. The
        // cache is purged the same way as the paint textures — by "seen" marks.
        private readonly Dictionary<Mesh, Vec3[][]> _normals    = new Dictionary<Mesh, Vec3[][]>();
        private readonly HashSet<Mesh>              _normalSeen = new HashSet<Mesh>();
        private readonly List<Mesh>                 _normalDead = new List<Mesh>();

        // Environment map for metals (sphere map). Rebuilt only when the scene
        // background colour changes — a metal should reflect the room it stands in.
        private int _envTex = -1;
        private int _envBg;

        private bool _lit;                // whether the fixed-function light is on in this frame

        // Reusable per-face scratch buffers (world + local verts). Grown on demand so
        // the hot draw loop never allocates — this killed millions of short-lived
        // Vec3[] arrays per second on large meshes (the main GC-stutter source).
        private Vec3[] _wv = new Vec3[8];
        private Vec3[] _lv = new Vec3[8];

        private void EnsureScratch(int n)
        {
            if (_wv.Length < n) { _wv = new Vec3[n]; _lv = new Vec3[n]; }
        }

        public GLRenderer(Camera cam) { _cam = cam; }

        public void Render(Scene scene, int w, int h, SceneGeometry s)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            GL.Viewport(0, 0, w, h);
            Color bg = s.BackgroundColor;
            GL.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.DepthMask(true);
            GL.Disable(EnableCap.CullFace);          // draw double-sided; depth resolves
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.LineSmooth);

            _fileSeen.Clear();
            SetupMatrices(w, h);

            LightSettings light = scene != null && scene.Light != null ? scene.Light : NoLight;
            SetupLight(light);
            PrepareLocalLights(scene);

            // The scene floor is needed by both the grid and the shadow, so it is computed once.
            float ext = s.GridExtent, floorY = 0f;
            bool needFloor = s.ShowGrid || (_lit && light.Shadows);
            if (needFloor)
            {
                SceneFloor(scene, s.GridExtent, out ext, out float minY);
                floorY = s.FixedGrid ? 0f : minY;
            }

            PrepareShadows(scene, light, floorY);

            if (s.ShowGrid) DrawGrid(s, ext, floorY);

            DrawShadowFloor(light, floorY);

            _paintSeen.Clear();
            _normalSeen.Clear();
            if (scene != null)
            {
                // Two passes: opaque objects first, then translucent ones — otherwise glass
                // drawn before its neighbour hides it through the depth buffer.
                foreach (var obj in scene.Objects) DrawObject(obj, s, false, light, false);
                DrawTransparentScene(scene, s, light);
            }

            if (_lit && light.ShowSun) DrawLightGizmo(light, ext);

            var unusedTextures = new List<string>();
            foreach (var entry in _fileTex) if (!_fileSeen.Contains(entry.Key)) unusedTextures.Add(entry.Key);
            foreach (string key in unusedTextures) { if (_fileTex[key] != 0) GL.DeleteTexture(_fileTex[key]); _fileTex.Remove(key); }
            PrunePaintTextures();
            PruneNormals();

            // Gizmos / guides are drawn by the caller on top of the frame — switch the
            // light off for them, otherwise they get shaded like geometry.
            GL.Disable(EnableCap.Lighting);
            _lit = false;
        }

        // ── Light (the "sun") ─────────────────────────────────────
        // The light position is set AFTER the model-view matrix is loaded, so the
        // direction is given directly in world coordinates — OpenGL converts it to
        // camera space itself, and the light stays fixed while the view rotates.
        private void SetupLight(LightSettings L)
        {
            _lit = L != null && L.Enabled;
            if (!_lit)
            {
                GL.Disable(EnableCap.Lighting);
                GL.Disable(EnableCap.Light1);
                return;
            }

            GL.Enable(EnableCap.Lighting);
            GL.Enable(EnableCap.Light0);
            GL.Enable(EnableCap.Normalize);
            GL.Enable(EnableCap.ColorMaterial);
            GL.ColorMaterial(MaterialFace.FrontAndBack, ColorMaterialParameter.AmbientAndDiffuse);

            // Two-sided light — geometry is drawn without back-face culling.
            GL.LightModel(LightModelParameter.LightModelTwoSide, 1);
            GL.LightModel(LightModelParameter.LightModelLocalViewer, 1);
            // GL_LIGHT_MODEL_COLOR_CONTROL = GL_SEPARATE_SPECULAR_COLOR: the highlight is
            // applied after the texture, otherwise a dark texture swallows it.
            GL.LightModel((LightModelParameter)0x81F8, 0x81FA);

            Color amb = L.AmbientColor;
            float a = Clamp01(L.Ambient);
            GL.LightModel(LightModelParameter.LightModelAmbient,
                new float[] { amb.R / 255f * a, amb.G / 255f * a, amb.B / 255f * a, 1f });

            Vec3 d = L.Direction;
            GL.Light(LightName.Light0, LightParameter.Position, new float[] { d.X, d.Y, d.Z, 0f });

            Color c = L.Color;
            float k = L.AboveHorizon ? Math.Max(0f, L.Intensity) : 0f;
            GL.Light(LightName.Light0, LightParameter.Diffuse,
                new float[] { c.R / 255f * k, c.G / 255f * k, c.B / 255f * k, 1f });
            float sk = L.Specular ? k : 0f;
            GL.Light(LightName.Light0, LightParameter.Specular,
                new float[] { c.R / 255f * sk, c.G / 255f * sk, c.B / 255f * sk, 1f });
            GL.Light(LightName.Light0, LightParameter.Ambient, new float[] { 0f, 0f, 0f, 1f });

            SetupRimLight(L, d, k);
        }

        // A second, weak source from the opposite side. A single light gives a metal
        // exactly one highlight, and that reads as plastic; a rim highlight along the
        // far edge of the form is what makes metal look like metal. It adds almost no
        // illumination (its diffuse is several times weaker), works only on the
        // highlight and switches off together with the highlights.
        private static void SetupRimLight(LightSettings L, Vec3 sun, float k)
        {
            if (!L.Specular || k <= 0f)
            {
                GL.Disable(EnableCap.Light1);
                return;
            }

            GL.Enable(EnableCap.Light1);
            Vec3 rim = new Vec3(-sun.X, Math.Max(0.3f, sun.Y * 0.4f), -sun.Z).Normalized();
            GL.Light(LightName.Light1, LightParameter.Position,
                new float[] { rim.X, rim.Y, rim.Z, 0f });

            // A slightly cool tint: warm key + cool rim is the trick by which the eye
            // immediately reads a metallic surface.
            Color c = L.AmbientColor;
            float dk = 0.10f * k;
            float sk = 0.55f * k;
            GL.Light(LightName.Light1, LightParameter.Diffuse,
                new float[] { c.R / 255f * dk, c.G / 255f * dk, c.B / 255f * dk, 1f });
            GL.Light(LightName.Light1, LightParameter.Specular,
                new float[] { c.R / 255f * sk, c.G / 255f * sk, c.B / 255f * sk, 1f });
            GL.Light(LightName.Light1, LightParameter.Ambient, new float[] { 0f, 0f, 0f, 1f });
        }

        // Material PBR parameters → the fixed-function specular colour and highlight
        // sharpness. Metal tints the highlight with its own colour and gives almost no
        // diffuse; roughness blurs the highlight (a small Phong exponent).
        private void ApplyPbrMaterial(Material m, LightSettings L, out float diffuseScale)
        {
            diffuseScale = 1f;
            if (!_lit) return;

            float rough = Clamp01(m.Roughness);
            float metal = Clamp01(m.Metallic);
            float gloss = (1f - rough) * (1f - rough);

            Color c = m.Color;
            float dielectric = 0.05f + 0.45f * gloss;          // a dielectric's highlight is white
            // Metal reflects almost everything and tints the reflection with its colour; the
            // base-colour contribution is lifted (at least 0.35), otherwise dark metal looks
            // like dirty plastic instead of a shiny surface.
            float metallic   = 0.35f + 0.65f * gloss;
            float mr = 0.35f + 0.65f * (c.R / 255f);
            float mg = 0.35f + 0.65f * (c.G / 255f);
            float mb = 0.35f + 0.65f * (c.B / 255f);
            float sr = (1f - metal) * dielectric + metal * metallic * mr;
            float sg = (1f - metal) * dielectric + metal * metallic * mg;
            float sb = (1f - metal) * dielectric + metal * metallic * mb;

            // The fixed pipeline computes the specular term per vertex: on large faces a
            // narrow highlight smears into polygonal blobs. For metal it is almost fully
            // damped — there the environment reflection, which is exact per pixel, takes
            // over. Dielectrics keep the vertex highlight: it is wide and shows no such
            // artifacts.
            float vertexSpec = 1f - 0.85f * metal;
            sr *= vertexSpec; sg *= vertexSpec; sb *= vertexSpec;
            if (!L.Specular) { sr = sg = sb = 0f; }

            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Specular,
                new float[] { sr, sg, sb, 1f });
            // Metal gathers its highlight noticeably tighter than a dielectric at the same roughness.
            float shininess = 2f + 126f * gloss * (1f + 0.6f * metal);
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Shininess,
                shininess > 128f ? 128f : shininess);

            float em = Clamp01(m.EmissionStrength);
            if (em > 0.001f)
            {
                Color e = m.EmissionColor;
                GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Emission,
                    new float[] { e.R / 255f * em, e.G / 255f * em, e.B / 255f * em, 1f });
            }
            else
            {
                GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Emission,
                    new float[] { 0f, 0f, 0f, 1f });
            }

            // Metal has almost no diffuse reflection — damp the base colour. But not to
            // zero: in a single-light scene a fully "desaturated" metal turns into a
            // black silhouette with a dot of highlight.
            diffuseScale = 1f - 0.32f * metal;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        // Delete GL textures of materials that no longer exist in the rendered scene.
        private void PrunePaintTextures()
        {
            if (_paintTex.Count == _paintSeen.Count) return;
            _paintDead.Clear();
            foreach (var kv in _paintTex)
                if (!_paintSeen.Contains(kv.Key)) _paintDead.Add(kv.Key);
            foreach (var m in _paintDead)
            {
                GL.DeleteTexture(_paintTex[m]);
                _paintTex.Remove(m);
                _paintVer.Remove(m);
                _paintSize.Remove(m);
            }
        }

        // The mesh's points moved: its smooth normals are rebuilt on the next frame.
        public void InvalidateNormals(Mesh mesh)
        {
            if (mesh != null) _normals.Remove(mesh);
        }

        private void PruneNormals()
        {
            if (_normals.Count == _normalSeen.Count) return;
            _normalDead.Clear();
            foreach (var kv in _normals)
                if (!_normalSeen.Contains(kv.Key)) _normalDead.Add(kv.Key);
            foreach (var m in _normalDead) _normals.Remove(m);
        }
        // The floor grid grows to cover every object's XZ footprint (so shapes never
        // hang off the edge) and rides at the bottom of the lowest object (so it sits
        // just under the scene instead of cutting through raised shapes). Both come
        // from a single traversal — WorldBounds transforms every vertex, so doing it
        // once instead of twice halves that per-frame CPU cost.
        private static void SceneFloor(Scene scene, float baseExtent, out float extent, out float minY)
        {
            float r = baseExtent;
            float y = 0f;
            bool any = false;
            if (scene != null)
                foreach (var obj in scene.Objects)
                    if (SceneGeometry.WorldBounds(obj, out Vec3 min, out Vec3 max))
                    {
                        r = Math.Max(r, Math.Max(Math.Abs(min.X), Math.Abs(max.X)));
                        r = Math.Max(r, Math.Max(Math.Abs(min.Z), Math.Abs(max.Z)));
                        if (!any || min.Y < y) y = min.Y;
                        any = true;
                    }
            extent = (float)Math.Ceiling(r) + 1f;
            minY   = any ? y : 0f;
        }

        // ── Camera matrices (match the CPU Camera.Project for picking parity) ──
        private void SetupMatrices(int w, int h)
        {
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            if (_cam.Mode == ViewMode.Perspective)
            {
                float fov = Math.Min(w, h) * 0.72f;
                float n = 0.05f, f = 4000f;
                float r = n * w / (2f * fov);
                float t = n * h / (2f * fov);
                GL.Frustum(-r, r, -t, t, n, f);

                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadIdentity();
                GL.Translate(0f, 0f, -_cam.Distance);
                GL.Rotate(_cam.AngleX, 1f, 0f, 0f);
                GL.Rotate(_cam.AngleY, 0f, 1f, 0f);
                GL.Translate(-_cam.Target.X, -_cam.Target.Y, -_cam.Target.Z);
            }
            else
            {
                float scale = Math.Min(w, h) / (_cam.OrthoScale * 2f);
                float r = w / (2f * scale);
                float t = h / (2f * scale);
                GL.Ortho(-r, r, -t, t, -4000f, 4000f);

                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadMatrix(OrthoModelView(_cam.Mode, _cam.Target));
            }
        }

        // Column-major model-view replicating Camera.ToCameraSpace for ortho modes.
        private static double[] OrthoModelView(ViewMode mode, Vec3 tgt)
        {
            // 3x3 map M such that cam = M * world; translation = -M * target.
            float[,] m;
            switch (mode)
            {
                case ViewMode.Top:   m = new float[,] { { 1, 0, 0 }, { 0, 0, 1 }, { 0, 1, 0 } }; break;
                case ViewMode.Side:  m = new float[,] { { 0, 0, -1 }, { 0, 1, 0 }, { 1, 0, 0 } }; break;
                default:             m = new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }; break; // Front
            }
            float tx = -(m[0, 0] * tgt.X + m[0, 1] * tgt.Y + m[0, 2] * tgt.Z);
            float ty = -(m[1, 0] * tgt.X + m[1, 1] * tgt.Y + m[1, 2] * tgt.Z);
            float tz = -(m[2, 0] * tgt.X + m[2, 1] * tgt.Y + m[2, 2] * tgt.Z);

            return new double[]
            {
                m[0,0], m[1,0], m[2,0], 0,
                m[0,1], m[1,1], m[2,1], 0,
                m[0,2], m[1,2], m[2,2], 0,
                tx,     ty,     tz,     1
            };
        }

        // Selection colour of the viewport — Blender's orange (#FFA028).
        private const float SelectionR = 1f, SelectionG = 0.627f, SelectionB = 0.157f;

        // ── Grid ──────────────────────────────────────────────────
        private void DrawGrid(SceneGeometry s, float extent, float y)
        {
            GL.Disable(EnableCap.Texture2D);
            // The grid is helper graphics, not geometry: light does not affect it.
            if (_lit) GL.Disable(EnableCap.Lighting);
            float e    = Math.Max(0.5f, extent);
            float step = Math.Max(0.05f, s.GridSpacing);
            Color c    = s.GridColor;

            GL.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            GL.Begin(PrimitiveType.Lines);
            int guard = 4000;
            for (float f = -e; f <= e + 1e-4f && guard-- > 0; f += step)
            {
                GL.Vertex3(f, y, -e); GL.Vertex3(f, y, e);
                GL.Vertex3(-e, y, f); GL.Vertex3(e, y, f);
            }
            GL.End();

            // World axes on the floor, coloured like the navigation gizmo (Blender
            // convention): X red, Z blue (Y is up here). A hair above the grid so
            // they win the depth test against it.
            float ay = y + 0.001f;
            GL.LineWidth(1.6f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color4(0.96f, 0.26f, 0.34f, 0.85f);
            GL.Vertex3(-e, ay, 0f); GL.Vertex3(e, ay, 0f);
            GL.Color4(0.26f, 0.55f, 0.96f, 0.85f);
            GL.Vertex3(0f, ay, -e); GL.Vertex3(0f, ay, e);
            GL.End();
            GL.LineWidth(1f);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        // ── Objects ───────────────────────────────────────────────
        private void DrawObject(SceneObject obj, SceneGeometry s, bool parentSel,
                                LightSettings light, bool transparentPass, int onlyFace = -1)
        {
            if (!obj.IsVisible) return;   // hidden objects (and their children) are not drawn
            if (obj.IsGroup)
            {
                foreach (var ch in obj.Children)
                    DrawObject(ch, s, obj.IsSelected || parentSel, light, transparentPass);
                return;
            }
            if (obj.Mesh == null) return;

            float alpha = Clamp01(obj.Material.Opacity);
            if (obj.Material.IsTransparent != transparentPass) return;
            if (alpha <= 0f) return;

            bool sel = obj.IsSelected || parentSel;
            var mesh = obj.Mesh;
            int faceCount = mesh.Faces.Count;

            int fileTex = obj.Material.HasTexture ? MaterialTexture(obj.Material) : 0;
            int aoTex   = obj.Material.HasAoTexture ? FileTexture(obj.Material.AoTexturePath, obj.Material.AoTexture) : 0;
            int paintT  = obj.Material.HasPaint     ? PaintTexture(obj.Material) : 0;
            bool wire   = s.ShowWireframe || obj.Material.Mode == MaterialMode.Wireframe;

            // The canvas is "paper": drawn without shading or highlights so that white
            // stays white and paint shows in its own colour.
            // The ruler is printed, too: its wood and its figures keep their own colour under any light.
            bool paperLike = obj.PrimitiveType == "Canvas" || obj.PrimitiveType == "Ruler";
            bool flatPaper = paperLike;
            bool lit = _lit && !flatPaper && !wire;
            if (_lit && !lit) GL.Disable(EnableCap.Lighting);
            else if (lit)     GL.Enable(EnableCap.Lighting);

            float diffuseScale = 1f;
            if (lit) ApplyPbrMaterial(obj.Material, light, out diffuseScale);

            // Smoothed normals are only needed under real light.
            Vec3[][] smooth = lit ? SmoothNormals(mesh) : null;

            // Metal reflects the environment. A single highlight from a point source is
            // not enough: it is computed per vertex, and on a mesh like a torus (16×10)
            // a narrow highlight simply falls between the vertices, leaving a dull blotch.
            // The reflection is sampled from a texture per pixel, so the sun stays
            // crisp regardless of mesh density.
            float metal = Clamp01(obj.Material.Metallic);
            bool reflect = lit && light.Specular && metal > 0.05f && !wire && !transparentPass;

            // A translucent object must not hide what is behind it.
            if (transparentPass) GL.DepthMask(false);

            // When tiling is off for this material, normalize planar UV by the mesh's
            // local extent so the image fits exactly once (no repeated little squares).
            bool fit = fileTex != 0 && obj.Material.NoTile;
            Vec3 lmin = Vec3.Zero, lext = Vec3.One;
            if (fit) LocalBounds(mesh, out lmin, out lext);

            // One trig evaluation per object; vertices are then pure multiply-adds.
            var xf = SceneGeometry.Xform.For(obj);

            // The sheet is one-sided: its front is the paper, its back is the reverse of a
            // stretched canvas (DrawCanvasBack) — so the paper pass must not show through.
            bool twoFaced = obj.PrimitiveType == "Canvas" && !wire && onlyFace < 0;
            if (twoFaced) { GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Back); }

            for (int fi = onlyFace < 0 ? 0 : onlyFace; fi < (onlyFace < 0 ? faceCount : onlyFace + 1); fi++)
            {
                var face = mesh.Faces[fi];
                int fl = face.Length;
                if (fl < 3) continue;

                EnsureScratch(fl);
                Vec3[] wv = _wv, lv = _lv;   // reused buffers; iterate only [0, fl)
                for (int i = 0; i < fl; i++)
                {
                    lv[i] = mesh.Vertices[face[i]];
                    wv[i] = xf.Apply(lv[i]);
                }

                Vec3 n = FaceNormal(wv, fl);
                Vec3[] fn = smooth != null && fi < smooth.Length ? smooth[fi] : null;
                float shade = lit || flatPaper ? 1f : (_localLights.Count > 0 ? 0.10f : Shade(n, s.Brightness));

                if (wire)
                {
                    GL.Disable(EnableCap.Texture2D);
                    Color wc = obj.Material.Color;
                    GL.Color4(wc.R / 255f, wc.G / 255f, wc.B / 255f, 1f);
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int i = 0; i < fl; i++) GL.Vertex3(wv[i].X, wv[i].Y, wv[i].Z);
                    GL.End();
                    continue;
                }

                if (lit) GL.Normal3(n.X, n.Y, n.Z);

                // Ambient-occlusion (AO) map — a second texture unit that multiplies the base layer.
                bool useAo = aoTex != 0;
                if (useAo) BindAo(aoTex);

                GL.Enable(EnableCap.AlphaTest);
                GL.AlphaFunc(AlphaFunction.Greater, (obj.Material.AlphaMode == TextureAlphaMode.Cutout ? 0.5f : 0.002f) * alpha);

                // Base pass: file texture, or flat colour.
                if (fileTex != 0)
                {
                    Vec3 nl = Vec3.Cross(lv[1] - lv[0], lv[2] - lv[0]).Normalized();
                    GL.Enable(EnableCap.Texture2D);
                    GL.BindTexture(TextureTarget.Texture2D, fileTex);
                    GL.Color4(shade * diffuseScale, shade * diffuseScale, shade * diffuseScale, alpha);
                    GL.Begin(PrimitiveType.TriangleFan);
                    for (int i = 0; i < fl; i++)
                    {
                        PointF uv = fit ? FitUV(lv[i], nl, lmin, lext)
                                        : SceneGeometry.PlanarUV(lv[i], nl);
                        if (useAo) { GL.MultiTexCoord2(TextureUnit.Texture0, uv.X, uv.Y);
                                     GL.MultiTexCoord2(TextureUnit.Texture1, uv.X, uv.Y); }
                        else GL.TexCoord2(uv.X, uv.Y);
                        Normal(fn, n, i, xf, lit);
                        GL.Vertex3(wv[i].X, wv[i].Y, wv[i].Z);
                    }
                    GL.End();
                    GL.Disable(EnableCap.Texture2D);
                }
                else
                {
                    Color bc = obj.Material.Color;
                    GL.Disable(EnableCap.Texture2D);
                    float ds = shade * diffuseScale;
                    GL.Color4(bc.R / 255f * ds, bc.G / 255f * ds, bc.B / 255f * ds, alpha);
                    GL.Begin(PrimitiveType.TriangleFan);
                    for (int i = 0; i < fl; i++)
                    {
                        if (useAo)
                        {
                            Vec3 nl2 = Vec3.Cross(lv[1] - lv[0], lv[2] - lv[0]).Normalized();
                            PointF uv = SceneGeometry.PlanarUV(lv[i], nl2);
                            GL.MultiTexCoord2(TextureUnit.Texture1, uv.X, uv.Y);
                        }
                        Normal(fn, n, i, xf, lit);
                        GL.Vertex3(wv[i].X, wv[i].Y, wv[i].Z);
                    }
                    GL.End();
                }

                if (useAo) UnbindAo();
                if (!flatPaper) DrawLocalLightFace(obj, fi, fileTex, alpha);
                GL.Disable(EnableCap.AlphaTest);

                // Paint pass (atlas UV, blended on top).
                if (paintT != 0)
                {
                    GL.Enable(EnableCap.Texture2D);
                    GL.BindTexture(TextureTarget.Texture2D, paintT);
                    GL.Color4(shade, shade, shade, alpha);
                    GL.Begin(PrimitiveType.TriangleFan);
                    for (int i = 0; i < fl; i++)
                    {
                        PointF uv = SceneGeometry.PaintUV(obj, fi, faceCount, i, fl);
                        GL.TexCoord2(uv.X, uv.Y);
                        Normal(fn, n, i, xf, lit);
                        GL.Vertex3(wv[i].X, wv[i].Y, wv[i].Z);
                    }
                    GL.End();
                    GL.Disable(EnableCap.Texture2D);
                }

                // Selected → orange outline (Blender's selection colour).
                if (sel && s.OutlineSelection)
                {
                    if (lit) GL.Disable(EnableCap.Lighting);
                    GL.Color4(SelectionR, SelectionG, SelectionB, 1f);
                    GL.LineWidth(1.5f);
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int i = 0; i < fl; i++) GL.Vertex3(wv[i].X, wv[i].Y, wv[i].Z);
                    GL.End();
                    if (lit) GL.Enable(EnableCap.Lighting);
                }
            }

            if (twoFaced)
            {
                GL.Disable(EnableCap.CullFace);
                DrawCanvasBack(obj, mesh, xf, alpha);
            }

            // Without a per-object outline the selection must still be visible —
            // show a bounding rectangle around the object.
            if (sel && !s.OutlineSelection && onlyFace < 0) DrawSelectionBox(obj);

            if (reflect) DrawReflection(obj, mesh, xf, smooth, light, metal, s.BackgroundColor);

            if (ReceivesShadow(obj, lit, flatPaper)) DrawShadowOnObject(obj, mesh, xf, smooth, alpha, onlyFace, transparentPass);

            if (s.ShowVertices && onlyFace < 0)
            {
                if (lit) GL.Disable(EnableCap.Lighting);
                GL.Disable(EnableCap.Texture2D);
                GL.Color4(sel ? 0f : 1f, 1f, 1f, 1f);
                GL.PointSize(sel ? 6f : 4f);
                GL.Begin(PrimitiveType.Points);
                foreach (var v in mesh.Vertices)
                {
                    Vec3 wv = xf.Apply(v);
                    GL.Vertex3(wv.X, wv.Y, wv.Z);
                }
                GL.End();
                if (lit) GL.Enable(EnableCap.Lighting);
            }

            if (transparentPass) GL.DepthMask(true);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        // ── The back of the canvas ────────────────────────────────
        private int _backTex, _backKey;

        private static Vec3 CanvasPoint(Vec3 tl, Vec3 right, Vec3 down, Vec3 back, float u, float v, float z)
        {
            return tl + right * u + down * v + back * z;
        }

        // The sheet is a stretched canvas with real depth behind it: a frame of solid
        // bars stands off the cloth, the fabric wraps round the edge. From behind the
        // raw linen lies at the paper's plane and the bars, margin and crossbar stand
        // proud of it by CanvasBack.Depth, with walls between — so the camera really
        // moves round the frame (parallax) instead of over a picture of one. From the
        // front only the thin wrapped edge shows, at oblique angles.
        //
        // Pure decoration of the render: picking, bounds and painting still see the
        // flat sheet. The picture (CanvasBack) is generated only when the back is seen.
        private void DrawCanvasBack(SceneObject obj, Mesh mesh, SceneGeometry.Xform xf, float alpha)
        {
            if (mesh.Faces.Count == 0 || mesh.Faces[0].Length != 4) return;
            var face = mesh.Faces[0];
            Vec3 bl = xf.Apply(mesh.Vertices[face[0]]), br = xf.Apply(mesh.Vertices[face[1]]);
            Vec3 tr = xf.Apply(mesh.Vertices[face[2]]), tl = xf.Apply(mesh.Vertices[face[3]]);
            float width = (br - bl).Length, height = (tl - bl).Length;
            if (width < 1e-5f || height < 1e-5f) return;

            Vec3 n = Vec3.Cross(br - bl, tr - bl).Normalized();      // the paper's side
            Vec3 toCam = _cam.Mode == ViewMode.Perspective ? (_cam.GetPosition() - bl).Normalized() : _cam.GetOrthoViewDir();
            bool fromBack = Vec3.Dot(n, toCam) < 0f;

            Vec3 right = tr - tl, down = bl - tl;
            Vec3 back = n * -(CanvasBack.Depth * Math.Min(width, height));

            // The wrapped edge: four walls round the sheet, fabric-coloured, shaded
            // darker at the paper's plane so the thickness reads.
            GL.Disable(EnableCap.Texture2D);
            Wall(tl, right, down, back, 0f, 0f, 1f, 0f, alpha, 0.80f, 0.97f);   // top
            Wall(tl, right, down, back, 0f, 1f, 1f, 1f, alpha, 0.72f, 0.90f);   // bottom
            Wall(tl, right, down, back, 0f, 0f, 0f, 1f, alpha, 0.76f, 0.94f);   // left
            Wall(tl, right, down, back, 1f, 0f, 1f, 1f, alpha, 0.70f, 0.88f);   // right
            if (!fromBack) return;

            float aspect = width / height;
            int key = CanvasBack.AspectKey(aspect);
            if (_backTex == 0 || _backKey != key)
            {
                if (_backTex != 0) GL.DeleteTexture(_backTex);
                using (var bmp = CanvasBack.Create(key)) _backTex = Upload(bmp, out Size _, mipmaps: true);
                _backKey = key;
            }
            var L = CanvasBack.LayoutFor(key);

            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, _backTex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);
            GL.Color4(1f, 1f, 1f, alpha);

            // The cloth, at the paper's plane.
            Plate(tl, right, down, back, 0f, 0f, 1f, 1f, 0f, alpha, 1f);

            // What stands proud, at depth: the margin and the four bars as bands around
            // the openings, and the crossbar. Each shows its own part of the picture.
            float iu0 = L.InnerU0, iv0 = L.InnerV0, iu1 = L.InnerU1, iv1 = L.InnerV1;
            Plate(tl, right, down, back, 0f, 0f, 1f, iv0, 1f, alpha, 1f);        // top band
            Plate(tl, right, down, back, 0f, iv1, 1f, 1f, 1f, alpha, 1f);        // bottom band
            Plate(tl, right, down, back, 0f, iv0, iu0, iv1, 1f, alpha, 1f);      // left band
            Plate(tl, right, down, back, iu1, iv0, 1f, iv1, 1f, alpha, 1f);      // right band
            if (L.HasCross) Plate(tl, right, down, back, L.CrossU0, L.CrossV0, L.CrossU1, L.CrossV1, 1f, alpha, 1f);

            // Walls down into each opening, wood-coloured, dark at the cloth: the step.
            float tu = 0.30f, tv = (L.WrapV + L.InnerV0) / 2f;     // a plain spot of the top bar
            GL.Color4(1f, 1f, 1f, alpha);
            if (L.HasCross && L.CrossVertical)
            {
                OpeningWalls(tl, right, down, back, iu0, iv0, L.CrossU0, iv1, tu, tv, alpha);
                OpeningWalls(tl, right, down, back, L.CrossU1, iv0, iu1, iv1, tu, tv, alpha);
            }
            else if (L.HasCross)
            {
                OpeningWalls(tl, right, down, back, iu0, iv0, iu1, L.CrossV0, tu, tv, alpha);
                OpeningWalls(tl, right, down, back, iu0, L.CrossV1, iu1, iv1, tu, tv, alpha);
            }
            else OpeningWalls(tl, right, down, back, iu0, iv0, iu1, iv1, tu, tv, alpha);

            GL.Disable(EnableCap.Texture2D);
        }

        // A rectangle (u0,v0)-(u1,v1) of the sheet lifted to depth z (0 = the paper's
        // plane, 1 = the frame's back), showing the same part of the picture.
        private static void Plate(Vec3 tl, Vec3 right, Vec3 down, Vec3 back,
                                  float u0, float v0, float u1, float v1, float z, float alpha, float shade)
        {
            if (u1 - u0 < 1e-5f || v1 - v0 < 1e-5f) return;
            GL.Color4(shade, shade, shade, alpha);
            GL.Begin(PrimitiveType.Quads);
            PlateVertex(tl, right, down, back, u0, v0, z);
            PlateVertex(tl, right, down, back, u1, v0, z);
            PlateVertex(tl, right, down, back, u1, v1, z);
            PlateVertex(tl, right, down, back, u0, v1, z);
            GL.End();
        }

        // Seen from behind, the picture runs the other way: flip u so its light stays top-left.
        private static void PlateVertex(Vec3 tl, Vec3 right, Vec3 down, Vec3 back, float u, float v, float z)
        {
            Vec3 p = CanvasPoint(tl, right, down, back, u, v, z);
            GL.TexCoord2(1f - u, v);
            GL.Vertex3(p.X, p.Y, p.Z);
        }

        // One side of the sheet's outline from the paper's plane back to the frame's
        // back; plain fabric colour, `shadeNear` at the plane, `shadeFar` at depth.
        private static void Wall(Vec3 tl, Vec3 right, Vec3 down, Vec3 back,
                                 float u0, float v0, float u1, float v1, float alpha, float shadeNear, float shadeFar)
        {
            Vec3 a = CanvasPoint(tl, right, down, back, u0, v0, 0f), b = CanvasPoint(tl, right, down, back, u1, v1, 0f);
            Vec3 c = CanvasPoint(tl, right, down, back, u1, v1, 1f), d = CanvasPoint(tl, right, down, back, u0, v0, 1f);
            const float r = 0.91f, g = 0.89f, bl = 0.86f;
            GL.Begin(PrimitiveType.Quads);
            GL.Color4(r * shadeNear, g * shadeNear, bl * shadeNear, alpha); GL.Vertex3(a.X, a.Y, a.Z); GL.Vertex3(b.X, b.Y, b.Z);
            GL.Color4(r * shadeFar,  g * shadeFar,  bl * shadeFar,  alpha); GL.Vertex3(c.X, c.Y, c.Z); GL.Vertex3(d.X, d.Y, d.Z);
            GL.End();
        }

        // The four inner walls of one opening, textured with a plain bit of wood and
        // shaded from dark at the cloth to light at the bar's face (the top and left
        // walls, facing away from the light, darker).
        private static void OpeningWalls(Vec3 tl, Vec3 right, Vec3 down, Vec3 back,
                                         float u0, float v0, float u1, float v1, float tu, float tv, float alpha)
        {
            if (u1 - u0 < 1e-5f || v1 - v0 < 1e-5f) return;
            GL.Begin(PrimitiveType.Quads);
            OpeningWall(tl, right, down, back, u0, v0, u1, v0, tu, tv, alpha, 0.34f, 0.80f);   // under the top bar
            OpeningWall(tl, right, down, back, u1, v0, u1, v1, tu, tv, alpha, 0.30f, 0.70f);   // right
            OpeningWall(tl, right, down, back, u1, v1, u0, v1, tu, tv, alpha, 0.38f, 0.92f);   // bottom
            OpeningWall(tl, right, down, back, u0, v1, u0, v0, tu, tv, alpha, 0.32f, 0.76f);   // left
            GL.End();
        }

        private static void OpeningWall(Vec3 tl, Vec3 right, Vec3 down, Vec3 back,
                                        float u0, float v0, float u1, float v1, float tu, float tv,
                                        float alpha, float shadeNear, float shadeFar)
        {
            Vec3 a = CanvasPoint(tl, right, down, back, u0, v0, 0f), b = CanvasPoint(tl, right, down, back, u1, v1, 0f);
            Vec3 c = CanvasPoint(tl, right, down, back, u1, v1, 1f), d = CanvasPoint(tl, right, down, back, u0, v0, 1f);
            GL.Color4(shadeNear, shadeNear, shadeNear, alpha); GL.TexCoord2(tu, tv); GL.Vertex3(a.X, a.Y, a.Z); GL.Vertex3(b.X, b.Y, b.Z);
            GL.Color4(shadeFar,  shadeFar,  shadeFar,  alpha); GL.Vertex3(c.X, c.Y, c.Z); GL.Vertex3(d.X, d.Y, d.Z);
        }

        // Bounding box of the selected object: twelve edges along its AABB.
        private void DrawSelectionBox(SceneObject obj)
        {
            Vec3 mn, mx;
            if (!SceneGeometry.WorldBounds(obj, out mn, out mx)) return;

            // A small margin so the frame does not blend into the surface.
            Vec3 pad = (mx - mn) * 0.02f;
            mn = mn - pad; mx = mx + pad;

            bool wasLit = _lit;
            if (wasLit) GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Texture2D);
            GL.Color4(SelectionR, SelectionG, SelectionB, 0.95f);
            GL.LineWidth(1.4f);

            GL.Begin(PrimitiveType.Lines);
            for (int axis = 0; axis < 3; axis++)
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                    {
                        Vec3 p1 = mn, p2 = mn;
                        // Two corners that differ only in the coordinate along axis.
                        int u = (axis + 1) % 3, v = (axis + 2) % 3;
                        p1 = p1.With(u, a == 0 ? mn.Get(u) : mx.Get(u));
                        p2 = p2.With(u, a == 0 ? mn.Get(u) : mx.Get(u));
                        p1 = p1.With(v, b == 0 ? mn.Get(v) : mx.Get(v));
                        p2 = p2.With(v, b == 0 ? mn.Get(v) : mx.Get(v));
                        p1 = p1.With(axis, mn.Get(axis));
                        p2 = p2.With(axis, mx.Get(axis));
                        GL.Vertex3(p1.X, p1.Y, p1.Z);
                        GL.Vertex3(p2.X, p2.Y, p2.Z);
                    }
            GL.End();

            if (wasLit) GL.Enable(EnableCap.Lighting);
        }

        // Vertex normal: the smoothed one if the mesh provided it, otherwise the face normal.
        private static void Normal(Vec3[] faceNormals, Vec3 flat, int i,
                                   SceneGeometry.Xform xf, bool lit)
        {
            if (!lit) return;
            if (faceNormals == null || i >= faceNormals.Length)
            {
                GL.Normal3(flat.X, flat.Y, flat.Z);
                return;
            }
            Vec3 w = xf.ApplyNormal(faceNormals[i]);
            GL.Normal3(w.X, w.Y, w.Z);
        }

        private static void BindAo(int tex)
        {
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode,
                (int)TextureEnvMode.Modulate);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private static void UnbindAo()
        {
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.Disable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        // ── Smoothed normals ──────────────────────────────────────
        // For every face, the normals of its vertices averaged over neighbouring faces,
        // but only if the angle between them is below a threshold (crease). That keeps
        // a sphere smooth and a cube's edges sharp. The result is cached per mesh.
        private Vec3[][] SmoothNormals(Mesh mesh)
        {
            if (mesh == null || mesh.Faces.Count == 0) return null;
            _normalSeen.Add(mesh);
            Vec3[][] cached;
            if (_normals.TryGetValue(mesh, out cached)) return cached;

            int fc = mesh.Faces.Count;
            int vc = mesh.Vertices.Count;
            var faceN = new Vec3[fc];
            var scratch = new Vec3[8];
            for (int f = 0; f < fc; f++)
            {
                var face = mesh.Faces[f];
                if (face.Length < 3) { faceN[f] = new Vec3(0, 1, 0); continue; }
                if (scratch.Length < face.Length) scratch = new Vec3[face.Length];
                for (int i = 0; i < face.Length; i++) scratch[i] = mesh.Vertices[face[i]];
                faceN[f] = FaceNormal(scratch, face.Length);
            }

            // Vertices with identical coordinates count as one. A sphere's longitude seam
            // and poles are duplicated under separate indices: without welding, the
            // neighbouring faces there were averaged over different lists, leaving a
            // visible stripe along the seam and a dark spot at the pole.
            var weld = WeldMap(mesh, out int weldCount);

            // welded vertex → faces (a CSR table, no per-vertex lists).
            var count = new int[weldCount + 1];
            for (int f = 0; f < fc; f++)
                foreach (int vi in mesh.Faces[f])
                    if (vi >= 0 && vi < vc) count[weld[vi] + 1]++;
            for (int i = 0; i < weldCount; i++) count[i + 1] += count[i];
            var adj  = new int[count[weldCount]];
            var fill = (int[])count.Clone();
            for (int f = 0; f < fc; f++)
                foreach (int vi in mesh.Faces[f])
                    if (vi >= 0 && vi < vc) adj[fill[weld[vi]]++] = f;

            const float crease = 0.64f;    // cos(50°)
            var result = new Vec3[fc][];
            for (int f = 0; f < fc; f++)
            {
                var face = mesh.Faces[f];
                var ns = new Vec3[face.Length];
                for (int k = 0; k < face.Length; k++)
                {
                    int vi = face[k];
                    Vec3 sum = Vec3.Zero;
                    if (vi >= 0 && vi < vc)
                    {
                        int w = weld[vi];
                        for (int j = count[w]; j < count[w + 1]; j++)
                        {
                            Vec3 nf = faceN[adj[j]];
                            if (nf.Length < 1e-6f) continue;          // degenerate face
                            if (Vec3.Dot(nf, faceN[f]) >= crease) sum = sum + nf;
                        }
                    }
                    ns[k] = sum.Length > 1e-5f ? sum.Normalized() : faceN[f];
                }
                result[f] = ns;
            }

            _normals[mesh] = result;
            return result;
        }

        // Vertex welding by coordinates: vertex index → index of the "merged" point.
        // Coordinates are rounded to 1e-4 — primitives are built with trigonometry,
        // so a bit-exact match cannot be expected at a seam.
        private struct WeldKey : IEquatable<WeldKey>
        {
            public int X, Y, Z;
            public bool Equals(WeldKey o) { return X == o.X && Y == o.Y && Z == o.Z; }
            public override bool Equals(object o) { return o is WeldKey && Equals((WeldKey)o); }
            public override int GetHashCode()
            {
                unchecked { return ((X * 397) ^ Y) * 397 ^ Z; }
            }
        }

        private static int[] WeldMap(Mesh mesh, out int weldCount)
        {
            int vc = mesh.Vertices.Count;
            var map = new int[vc];
            var seen = new Dictionary<WeldKey, int>(vc);
            for (int i = 0; i < vc; i++)
            {
                Vec3 v = mesh.Vertices[i];
                var key = new WeldKey
                {
                    X = (int)Math.Round(v.X * 10000f),
                    Y = (int)Math.Round(v.Y * 10000f),
                    Z = (int)Math.Round(v.Z * 10000f)
                };
                int id;
                if (!seen.TryGetValue(key, out id))
                {
                    id = seen.Count;
                    seen[key] = id;
                }
                map[i] = id;
            }
            weldCount = seen.Count;
            return map;
        }

        // ── Metal reflections (sphere map) ────────────────────────
        // The classic fixed-pipeline trick: a hemisphere texture whose coordinates
        // OpenGL derives itself from the normal and the view direction (GL_SPHERE_MAP).
        // The result is a real environment reflection — it lives on the surface, moves
        // as the camera turns and does not depend on mesh density, unlike a highlight,
        // which is interpolated between vertices.
        private void DrawReflection(SceneObject obj, Mesh mesh, SceneGeometry.Xform xf,
                                    Vec3[][] smooth, LightSettings L, float metal,
                                    Color background)
        {
            int tex = EnvTexture(background);
            if (tex == 0) return;

            float rough = Clamp01(obj.Material.Roughness);
            float gloss = (1f - rough) * (1f - rough);
            // Rough metal reflects dimly and blurred, polished metal brightly.
            float k = metal * (0.15f + 0.55f * gloss) * Math.Max(0f, Math.Min(1.5f, L.Intensity));
            if (k < 0.01f) return;

            // The reflection is tinted with the metal's colour and the light colour: gold reflects gold.
            Color c  = obj.Material.Color;
            Color lc = L.Color;
            float rr = c.R / 255f * lc.R / 255f * k;
            float gg = c.G / 255f * lc.G / 255f * k;
            float bb = c.B / 255f * lc.B / 255f * k;

            GL.Disable(EnableCap.Lighting);
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode,
                (int)TextureEnvMode.Modulate);
            GL.TexGen(TextureCoordName.S, TextureGenParameter.TextureGenMode, (int)All.SphereMap);
            GL.TexGen(TextureCoordName.T, TextureGenParameter.TextureGenMode, (int)All.SphereMap);
            GL.Enable(EnableCap.TextureGenS);
            GL.Enable(EnableCap.TextureGenT);

            // The map is drawn with the sun at the centre; shift it so the highlight lands
            // where the sun actually is relative to the camera.
            SunSphereUV(L, out float su, out float sv);
            GL.MatrixMode(MatrixMode.Texture);
            GL.PushMatrix();
            GL.LoadIdentity();
            // Not shifted by the full amount: the further the map slides, the more of its
            // stretched edge the object sees.
            GL.Translate((0.5f - su) * 0.7f, (0.5f - sv) * 0.7f, 0f);
            GL.MatrixMode(MatrixMode.Modelview);

            // Added to the light rather than replacing it: the reflection comes on top.
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            GL.DepthMask(false);
            GL.Color4(rr, gg, bb, 1f);

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                int fl = face.Length;
                if (fl < 3) continue;

                EnsureScratch(fl);
                for (int i = 0; i < fl; i++)
                {
                    _lv[i] = mesh.Vertices[face[i]];
                    _wv[i] = xf.Apply(_lv[i]);
                }
                Vec3 n = FaceNormal(_wv, fl);
                Vec3[] fn = smooth != null && fi < smooth.Length ? smooth[fi] : null;

                GL.Begin(PrimitiveType.TriangleFan);
                for (int i = 0; i < fl; i++)
                {
                    // The normal is required: GL_SPHERE_MAP derives from it
                    // which point of the environment is visible at this spot on the surface.
                    Normal(fn, n, i, xf, true);
                    GL.Vertex3(_wv[i].X, _wv[i].Y, _wv[i].Z);
                }
                GL.End();
            }

            GL.MatrixMode(MatrixMode.Texture);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.Disable(EnableCap.TextureGenS);
            GL.Disable(EnableCap.TextureGenT);
            GL.Disable(EnableCap.Texture2D);
            GL.DepthMask(true);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        // Where the sun lands on the hemisphere map. For a spherical reflection the
        // point with coordinates (s,t) corresponds to the reflection direction r in
        // camera space: s = r.x/m + 0.5, t = r.y/m + 0.5, m = 2·|r + (0,0,1)|.
        private void SunSphereUV(LightSettings L, out float s, out float t)
        {
            Vec3 d = L.Direction;
            // The direction in camera space: the difference of two transformed points —
            // this works for perspective and for orthographic views alike.
            Vec3 a = _cam.ToCameraSpace(_cam.Target);
            Vec3 b = _cam.ToCameraSpace(_cam.Target + d);
            Vec3 e = (b - a).Normalized();

            float m = 2f * (float)Math.Sqrt(e.X * e.X + e.Y * e.Y + (e.Z + 1f) * (e.Z + 1f));
            if (m < 1e-4f) { s = 0.5f; t = 0.5f; return; }
            s = e.X / m + 0.5f;
            t = e.Y / m + 0.5f;
        }

        private int EnvTexture(Color background)
        {
            int bg = background.ToArgb();
            if (_envTex >= 0 && _envBg == bg) return _envTex;
            if (_envTex > 0) GL.DeleteTexture(_envTex);
            _envBg = bg;
            using (var bmp = BuildEnvMap(256, background))
                _envTex = Upload(bmp);
            if (_envTex != 0)
            {
                // The map is shifted with the texture matrix, so the edges have to be clamped:
                // with repeat the disc would be tiled across the whole surface.
                GL.BindTexture(TextureTarget.Texture2D, _envTex);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    (int)TextureWrapMode.ClampToEdge);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
            return _envTex;
        }

        // A procedural greyscale environment map: a bright sun in the centre, a soft
        // halo around it, a light "sky" on top and a dark "ground" below with a
        // horizon band. The colour of the reflection comes from the material and the light.
        private static Bitmap BuildEnvMap(int n, Color background)
        {
            // The environment brightness follows the scene background: metal in a light
            // room reflects light, in a dark one — dark. Without that link the reflection
            // lived its own life and looked foreign.
            float bgLum = (0.299f * background.R + 0.587f * background.G +
                           0.114f * background.B) / 255f;
            float sky    = Clamp01(0.16f + bgLum * 0.50f);
            float ground = sky * 0.55f;

            var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var row = new byte[data.Stride];
            for (int y = 0; y < n; y++)
            {
                float tv = (y + 0.5f) / n;                     // 0 — bottom of the map, 1 — top
                for (int x = 0; x < n; x++)
                {
                    float su = (x + 0.5f) / n;
                    float dx = (su - 0.5f) * 2f;
                    float dy = (tv - 0.5f) * 2f;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    // The gradient fills the WHOLE square, with no "disc" and no dark rim at the
                    // edge. The map is shifted by the texture matrix, and the object used to fall
                    // outside the disc — blackness began there and a sharp arc ran across the
                    // surface. With clamping, the gradient itself now continues at the edge.
                    //
                    // The "sky → ground" transition is deliberately very soft and spread over
                    // almost the whole height. A narrow boundary with a dip at the horizon drew a
                    // hard line across the ball, making it look like an acorn: a light cap and a
                    // dark underside. A mirror ball shows the horizon in real life too, but not
                    // as a chopped edge.
                    float t = Clamp01(dy * 0.5f + 0.5f);
                    t = t * t * (3f - 2f * t);                       // smoothed step
                    float env = ground + (sky - ground) * t;
                    float halo = 0.20f * (float)Math.Exp(-(dist / 0.5f) * (dist / 0.5f));
                    float sun  = 0.85f * (float)Math.Exp(-(dist / 0.17f) * (dist / 0.17f));

                    float lum = env + halo + sun;
                    if (lum < 0f) lum = 0f;
                    if (lum > 1f) lum = 1f;

                    byte v = (byte)(lum * 255f);
                    row[x * 4 + 0] = v;   // B
                    row[x * 4 + 1] = v;   // G
                    row[x * 4 + 2] = v;   // R
                    row[x * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
            bmp.UnlockBits(data);
            return bmp;
        }

        // ── Light source marker ───────────────────────────────────
        // A helper for adjusting the light, not part of the picture: a sun symbol (a disc with
        // long and short rays alternating all round) standing in the direction of the source,
        // facing the camera, and an arrow from it showing where the light travels. Its size
        // follows the camera distance, so it keeps its size on screen at any zoom. Filled in the
        // light's own colour (warmed, so a pale light still shows) with a dark outline, which
        // reads on both a light and a dark background. Off by default — switched on in the
        // lighting dialog. About 70 vertices a frame.
        private const int SunRays = 16;     // even rays are long, odd ones short

        private void DrawLightGizmo(LightSettings L, float extent)
        {
            Vec3 dir  = L.Direction;
            float dist = Math.Max(extent * 1.4f,
                (_cam.Mode == ViewMode.Perspective ? _cam.Distance : _cam.OrthoScale) * 1.25f);
            Vec3 pos = _cam.Target + dir * dist;
            Vec3 r   = _cam.ScreenRight();
            Vec3 u   = _cam.ScreenUp();
            float rad = dist * 0.034f;

            Color c = L.Color;
            Color amber = Color.FromArgb(255, 205, 70);
            float k = 0.45f;   // pale light colours (the default is cream) are pulled towards amber to stay visible
            float fr = c.R / 255f * (1 - k) + amber.R / 255f * k, fg = c.G / 255f * (1 - k) + amber.G / 255f * k, fb = c.B / 255f * (1 - k) + amber.B / 255f * k;
            float a = L.AboveHorizon ? 0.95f : 0.5f;     // a sun under the horizon is drawn fainter

            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Texture2D);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);

            // Rays: thin triangles round the disc.
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 0) GL.Color4(fr, fg, fb, a); else { GL.Color4(0.12f, 0.1f, 0.06f, a * 0.9f); GL.LineWidth(1.3f); }
                for (int i = 0; i < SunRays; i++)
                {
                    bool longRay = (i & 1) == 0;
                    double t = 2.0 * Math.PI * i / SunRays + Math.PI / SunRays;
                    double half = longRay ? 0.17 : 0.2;
                    float tipR = rad * (longRay ? 2.45f : 1.85f), baseR = rad * 1.22f;
                    Vec3 tip = pos + Ring(r, u, t) * tipR;
                    Vec3 b0 = pos + Ring(r, u, t - half) * baseR, b1 = pos + Ring(r, u, t + half) * baseR;
                    GL.Begin(pass == 0 ? PrimitiveType.Triangles : PrimitiveType.LineLoop);
                    GL.Vertex3(b0.X, b0.Y, b0.Z); GL.Vertex3(tip.X, tip.Y, tip.Z); GL.Vertex3(b1.X, b1.Y, b1.Z);
                    GL.End();
                }
            }

            // The disc, with its outline.
            GL.Color4(fr, fg, fb, a);
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex3(pos.X, pos.Y, pos.Z);
            for (int i = 0; i <= 32; i++) { Vec3 p = pos + Ring(r, u, 2.0 * Math.PI * i / 32) * rad; GL.Vertex3(p.X, p.Y, p.Z); }
            GL.End();
            GL.Color4(0.12f, 0.1f, 0.06f, a * 0.9f);
            GL.LineWidth(1.6f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 32; i++) { Vec3 p = pos + Ring(r, u, 2.0 * Math.PI * i / 32) * rad; GL.Vertex3(p.X, p.Y, p.Z); }
            GL.End();

            // The arrow: from just outside the rays towards the scene, along the way the light travels.
            // The head is two crossed triangles, so it shows from any side.
            Vec3 from = pos - dir * (rad * 2.9f), tipA = pos - dir * (rad * 9.6f);
            Vec3 view = _cam.Mode == ViewMode.Perspective ? (_cam.GetPosition() - pos).Normalized() : _cam.GetOrthoViewDir();
            Vec3 side = Vec3.Cross(dir, view);
            if (side.Length < 0.15f) side = r;                      // looking along the light: any sideways will do
            side = side.Normalized();
            Vec3 up2 = Vec3.Cross(dir, side).Normalized();
            Vec3 head = tipA + dir * (rad * 2.6f);                  // where the head's base stands
            float hw = rad * 0.95f;
            // The shaft is a ribbon, not a GL line: software OpenGL draws every line one pixel wide.
            for (int pass = 0; pass < 2; pass++)
            {
                float hs = rad * (pass == 0 ? 0.34f : 0.2f);
                if (pass == 0) GL.Color4(0.12f, 0.1f, 0.06f, a * 0.9f); else GL.Color4(fr, fg, fb, a);
                Vec3 s0 = side * hs;
                GL.Begin(PrimitiveType.Quads);
                GL.Vertex3(from.X + s0.X, from.Y + s0.Y, from.Z + s0.Z); GL.Vertex3(head.X + s0.X, head.Y + s0.Y, head.Z + s0.Z);
                GL.Vertex3(head.X - s0.X, head.Y - s0.Y, head.Z - s0.Z); GL.Vertex3(from.X - s0.X, from.Y - s0.Y, from.Z - s0.Z);
                GL.End();
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int w = 0; w < 2; w++)
                {
                    Vec3 sd = w == 0 ? side : up2;
                    Vec3 e0 = head + sd * hw, e1 = head - sd * hw;
                    if (pass == 0) GL.Color4(fr, fg, fb, a); else { GL.Color4(0.12f, 0.1f, 0.06f, a * 0.9f); GL.LineWidth(1.3f); }
                    GL.Begin(pass == 0 ? PrimitiveType.Triangles : PrimitiveType.LineLoop);
                    GL.Vertex3(e0.X, e0.Y, e0.Z); GL.Vertex3(tipA.X, tipA.Y, tipA.Z); GL.Vertex3(e1.X, e1.Y, e1.Z);
                    GL.End();
                }
            }

            GL.LineWidth(1f);
            GL.DepthMask(true);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        // A direction in the screen plane at angle t (a unit vector in the span of r and u).
        private static Vec3 Ring(Vec3 r, Vec3 u, double t)
        {
            return r * (float)Math.Cos(t) + u * (float)Math.Sin(t);
        }

        // Polygon normal by Newell's formula: sums the contribution of all edges
        // instead of taking the first three vertices. At a sphere's poles the last row
        // of faces is degenerate (two vertices coincide), and cross(v1-v0, v2-v0) gave
        // zero there — the normal vanished and a black spot appeared at the top.
        private static Vec3 FaceNormal(Vec3[] v, int count)
        {
            float nx = 0f, ny = 0f, nz = 0f;
            for (int i = 0; i < count; i++)
            {
                Vec3 a = v[i], b = v[(i + 1) % count];
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            return new Vec3(nx, ny, nz).Normalized();
        }

        private static void LocalBounds(Mesh m, out Vec3 min, out Vec3 ext)
        {
            min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vec3 max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            foreach (var v in m.Vertices)
            {
                if (v.X < min.X) min.X = v.X; if (v.X > max.X) max.X = v.X;
                if (v.Y < min.Y) min.Y = v.Y; if (v.Y > max.Y) max.Y = v.Y;
                if (v.Z < min.Z) min.Z = v.Z; if (v.Z > max.Z) max.Z = v.Z;
            }
            if (m.Vertices.Count == 0) { min = Vec3.Zero; ext = Vec3.One; return; }
            ext = new Vec3(Math.Max(1e-4f, max.X - min.X),
                           Math.Max(1e-4f, max.Y - min.Y),
                           Math.Max(1e-4f, max.Z - min.Z));
        }

        // Fit the texture across the object's local extent once (dominant face axis),
        // matching PlanarUV's axis choice and vertical flip.
        private static PointF FitUV(Vec3 local, Vec3 nLocal, Vec3 min, Vec3 ext)
        {
            float ax = Math.Abs(nLocal.X), ay = Math.Abs(nLocal.Y), az = Math.Abs(nLocal.Z);
            float u, v;
            if (ax >= ay && ax >= az)      { u = (local.Z - min.Z) / ext.Z; v = (local.Y - min.Y) / ext.Y; }
            else if (ay >= ax && ay >= az) { u = (local.X - min.X) / ext.X; v = (local.Z - min.Z) / ext.Z; }
            else                           { u = (local.X - min.X) / ext.X; v = (local.Y - min.Y) / ext.Y; }
            return new PointF(u, 1f - v);
        }

        // Even, gentle shading. The old version multiplied by the (maxed-out) scene
        // brightness, which saturated most faces to full colour while grazing faces
        // dropped to ~0.5 — so neighbouring polygons of one primitive looked very
        // different ("one polygon much darker"). A high ambient floor with a small
        // directional term keeps shapes looking smooth and keeps painted colours true.
        private static float Shade(Vec3 n, float brightness)
        {
            float d = Math.Abs(Vec3.Dot(n, LightDir));   // 0 (grazing) .. 1 (facing the light)
            float lit = 0.82f + 0.18f * d;
            return Math.Max(0.05f, Math.Min(1f, lit));
        }

        // ── Texture upload / cache ────────────────────────────────
        // Texture cache by file path — the same image (base or AO map) is
        // loaded into video memory once for the whole application.
        private int FileTexture(string path, Bitmap bmp)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            _fileSeen.Add(path);
            if (_fileTex.TryGetValue(path, out int id)) return id;
            // Cache failures too (id 0): a missing/unreadable file must not be
            // re-read from disk on every frame. InvalidateFileTexture clears it.
            id = bmp != null ? Upload(bmp) : 0;
            _fileTex[path] = id;
            return id;
        }

        private int PaintTexture(Material m)
        {
            Bitmap bmp = m.PaintLayer;
            if (bmp == null) return 0;
            _paintSeen.Add(m);

            bool have = _paintTex.TryGetValue(m, out int id);
            int ver = m.PaintVersion;
            if (have && _paintVer.TryGetValue(m, out int seen) && seen == ver)
                return id;

            // A brush stroke touches a small area: update just that part of the
            // existing texture (same size, power-of-two, uploaded as-is). If the
            // driver rejects it even once, partial updates are off for the session
            // and the texture is re-uploaded whole — slower, but always correct.
            bool partial = m.TakeGpuDirtyArea(out Rectangle area);
            if (have && partial && _partialUploadOk
                && _paintSize.TryGetValue(m, out Size texSize) && texSize == bmp.Size
                && IsPow2(bmp.Width) && IsPow2(bmp.Height))
            {
                area.Intersect(new Rectangle(Point.Empty, bmp.Size));
                if (area.IsEmpty || UploadRegion(id, bmp, area))
                {
                    _paintVer[m] = ver;
                    return id;
                }
                _partialUploadOk = false;
                System.Diagnostics.Debug.WriteLine("Aplat: partial texture updates rejected by the GL driver — using full uploads.");
            }

            if (have) GL.DeleteTexture(id);
            id = Upload(bmp, out Size uploaded);
            _paintTex[m]  = id;
            _paintVer[m]  = ver;
            _paintSize[m] = uploaded;   // partial updates only when GPU size == layer size
            return id;
        }

        private readonly Dictionary<Material, Size> _paintSize = new Dictionary<Material, Size>();

        private static bool IsPow2(int v) { return v > 0 && (v & (v - 1)) == 0; }

        private static bool _partialUploadOk = true;

        // glTexSubImage2D of one rectangle. The rows are first packed into a tight
        // buffer, so only core GL 1.1 is needed — GL_UNPACK_ROW_LENGTH is not
        // reliably supported by virtual-machine / translation-layer drivers.
        // Returns false if GL reported an error (caller falls back to a full upload).
        private static bool UploadRegion(int id, Bitmap bmp, Rectangle r)
        {
            try
            {
                int rowBytes = r.Width * 4;
                var buf = new byte[rowBytes * r.Height];
                // Lock the whole bitmap and read the rows at their offsets: locking a
                // sub-rectangle makes Mono's libgdiplus write the block back at (0,0)
                // on unlock, stamping a copy of the changed area into the corner.
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly,
                                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < r.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(
                            IntPtr.Add(data.Scan0, (r.Y + y) * data.Stride + r.X * 4), buf, y * rowBytes, rowBytes);
                }
                finally { bmp.UnlockBits(data); }

                while (GL.GetError() != ErrorCode.NoError) { }   // start from a clean slate
                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, r.X, r.Y, r.Width, r.Height,
                    OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, buf);
                bool ok = GL.GetError() == ErrorCode.NoError;
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static int Upload(Bitmap src)
        {
            return Upload(src, out Size _);
        }

        private static int Upload(Bitmap src, out Size uploaded, bool mipmaps = false)
        {
            // Fixed-function GL with REPEAT wrap is unreliable for non-power-of-two
            // textures (many drivers show nothing). Rescale to a power-of-two size
            // so arbitrary user images always display.
            int pw = NextPow2(src.Width), ph = NextPow2(src.Height);
            uploaded = new Size(pw, ph);
            Bitmap bmp = src;
            bool temp = false;
            if (pw != src.Width || ph != src.Height)
            {
                bmp = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, pw, ph);
                }
                temp = true;
            }

            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);

            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                bmp.Width, bmp.Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            bmp.UnlockBits(data);
            // Fine cloth minified without mip levels shimmers. The levels are built here, not by
            // GL_GENERATE_MIPMAP (GL 1.4): the generic Windows renderer is GL 1.1, and a texture
            // with a mip filter but missing levels is "incomplete" — it draws plain white.
            bool mipped = mipmaps && UploadMipChain(bmp);
            if (temp) bmp.Dispose();

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)(mipped ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return id;
        }

        // Levels 1…n of the bound texture, each half the last (box-filtered by GDI+).
        // False (and the caller keeps a plain filter) if GL refuses any of them.
        private static bool UploadMipChain(Bitmap level0)
        {
            while (GL.GetError() != ErrorCode.NoError) { }
            Bitmap prev = level0;
            int w = level0.Width, h = level0.Height, level = 1;
            bool ok = true;
            while ((w > 1 || h > 1) && ok)
            {
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                var next = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(next))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(prev, new Rectangle(0, 0, w, h), 0, 0, prev.Width, prev.Height, GraphicsUnit.Pixel);
                }
                if (!ReferenceEquals(prev, level0)) prev.Dispose();
                prev = next;

                var d = next.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                GL.TexImage2D(TextureTarget.Texture2D, level, PixelInternalFormat.Rgba, w, h, 0,
                    OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, d.Scan0);
                next.UnlockBits(d);
                ok = GL.GetError() == ErrorCode.NoError;
                level++;
            }
            if (!ReferenceEquals(prev, level0)) prev.Dispose();
            return ok;
        }

        // Power of two ≥ v, capped at 2048 and at what the driver supports (some
        // virtual-machine GL drivers report less; an oversized texture shows nothing).
        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return Math.Min(p, Math.Min(2048, MaxTextureSize));
        }

        private static int _maxTextureSize;
        private static int MaxTextureSize
        {
            get
            {
                if (_maxTextureSize <= 0)
                {
                    int v = 0;
                    try { v = GL.GetInteger(GetPName.MaxTextureSize); } catch { }
                    _maxTextureSize = v >= 64 ? v : 1024;   // no answer → a size every GL 1.x has
                    System.Diagnostics.Debug.WriteLine("Aplat: GL_MAX_TEXTURE_SIZE = " + v
                        + " (" + GL.GetString(StringName.Renderer) + ", " + GL.GetString(StringName.Version) + ")");
                }
                return _maxTextureSize;
            }
        }

        // Drop a file texture from the cache (call when the path changes / cleared).
        public void InvalidateFileTexture(string path)
        {
            if (path != null && _fileTex.TryGetValue(path, out int id))
            {
                GL.DeleteTexture(id);
                _fileTex.Remove(path);
            }
        }
    }
}
