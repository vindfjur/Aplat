using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Rendering
{
    public partial class GLRenderer
    {
        private sealed class LocalSource
        {
            public SceneObject Object;
            public Vec3 Position;
            public float Flicker;
        }
        private sealed class Occluder
        {
            public SceneObject Object;
            public Vec3 Min, Max;
            public Vec3[] World;
        }
        private sealed class LightVertex
        {
            public Vec3 Position;
            public PointF UV;
            public Vec3[] Contributions;
        }
        private readonly List<LocalSource> _localLights = new List<LocalSource>();
        private readonly List<Occluder> _occluders = new List<Occluder>();
        private readonly Dictionary<SceneObject, Dictionary<int, LightVertex[]>> _lightSamples =
            new Dictionary<SceneObject, Dictionary<int, LightVertex[]>>();
        private readonly System.Diagnostics.Stopwatch _lightClock = System.Diagnostics.Stopwatch.StartNew();
        private int _lightingSignature;

        public bool HasAnimatedLights { get; private set; }

        private void PrepareLocalLights(Scene scene)
        {
            _localLights.Clear();
            var leaves = new List<SceneObject>();
            if (scene != null) foreach (var root in scene.Objects) CollectVisible(root, leaves);
            HasAnimatedLights = false;
            int hash = 17;
            unchecked
            {
                foreach (var o in leaves)
                {
                    var m = o.Material;
                    hash = hash * 31 + o.GetHashCode();
                    hash = hash * 31 + o.Position.GetHashCode();
                    hash = hash * 31 + o.Rotation.GetHashCode();
                    hash = hash * 31 + o.Scale.GetHashCode();
                    hash = hash * 31 + m.Opacity.GetHashCode();
                    hash = hash * 31 + m.ColorArgb;
                    hash = hash * 31 + (m.TextureKey ?? "").GetHashCode();
                    hash = hash * 31 + (int)m.AlphaMode;
                    hash = hash * 31 + m.AlphaCutoff.GetHashCode();
                    hash = hash * 31 + m.NoTile.GetHashCode();
                    hash = hash * 31 + m.TransmitLight.GetHashCode();
                    hash = hash * 31 + m.LightEnabled.GetHashCode();
                    hash = hash * 31 + m.LightPower.GetHashCode();
                    hash = hash * 31 + m.LightRange.GetHashCode();
                    hash = hash * 31 + m.LightColor.ToArgb();
                    hash = hash * 31 + m.Metallic.GetHashCode();
                    if (o.Mesh != null)
                    {
                        hash = hash * 31 + o.Mesh.GetHashCode();
                        foreach (var v in o.Mesh.Vertices) hash = hash * 31 + v.GetHashCode();
                        foreach (var f in o.Mesh.Faces)
                            foreach (int index in f) hash = hash * 31 + index;
                    }
                    if (!m.LightEnabled || m.LightPower <= 0f || m.LightRange <= 0f) continue;
                    double t = _lightClock.Elapsed.TotalSeconds;
                    double phase = (o.Id ?? "").GetHashCode() % 997;
                    float flicker = m.LightFlicker
                        ? 0.9f + 0.07f * (float)Math.Sin(t * 11.7 + phase) + 0.03f * (float)Math.Sin(t * 23.1 + phase)
                        : 1f;
                    _localLights.Add(new LocalSource { Object = o, Position = SceneGeometry.Center(o), Flicker = flicker });
                    HasAnimatedLights |= m.LightFlicker;
                }
            }
            if (hash == _lightingSignature) return;
            _lightingSignature = hash;
            _lightSamples.Clear();
            _occluders.Clear();
            if (_localLights.Count == 0) return;
            foreach (var o in leaves)
            {
                if (o.Mesh == null || o.Material.Opacity <= 0f || o.PrimitiveType == "Canvas") continue;
                if (!SceneGeometry.WorldBounds(o, out Vec3 min, out Vec3 max)) continue;
                var xf = SceneGeometry.Xform.For(o);
                var world = new Vec3[o.Mesh.Vertices.Count];
                for (int i = 0; i < world.Length; i++) world[i] = xf.Apply(o.Mesh.Vertices[i]);
                _occluders.Add(new Occluder { Object = o, Min = min, Max = max, World = world });
            }
        }

        private static void CollectVisible(SceneObject o, List<SceneObject> leaves)
        {
            if (!o.IsVisible) return;
            if (o.IsGroup) foreach (var ch in o.Children) CollectVisible(ch, leaves);
            else leaves.Add(o);
        }

        // The extra diffuse pass needs only GL 1.1, including in a Windows VM.
        // Subdivision is render-only: it does not change meshes or paint atlas indices.
        private void DrawLocalLightFace(SceneObject obj, int fi, int texture, float alpha)
        {
            if (_localLights.Count == 0 || obj.Material.LightEnabled || alpha <= 0f) return;
            if (!_lightSamples.TryGetValue(obj, out var faces))
                _lightSamples[obj] = faces = new Dictionary<int, LightVertex[]>();
            if (!faces.TryGetValue(fi, out var vertices))
                faces[fi] = vertices = BuildLightFace(obj, fi);
            if (vertices.Length == 0) return;
            GL.Disable(EnableCap.Lighting);
            GL.DepthMask(false);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(-1f, -1f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            if (texture != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, texture); }
            else GL.Disable(EnableCap.Texture2D);
            Color baseColor = texture != 0 ? Color.White : obj.Material.Color;
            float metal = 1f - 0.65f * Clamp01(obj.Material.Metallic);
            GL.Begin(PrimitiveType.Triangles);
            foreach (var v in vertices)
            {
                Vec3 light = Vec3.Zero;
                for (int i = 0; i < v.Contributions.Length; i++)
                    light = light + v.Contributions[i] * _localLights[i].Flicker;
                GL.Color4(Clamp01(light.X * baseColor.R / 255f * metal),
                    Clamp01(light.Y * baseColor.G / 255f * metal),
                    Clamp01(light.Z * baseColor.B / 255f * metal), alpha);
                GL.TexCoord2(v.UV.X, v.UV.Y);
                GL.Vertex3(v.Position.X, v.Position.Y, v.Position.Z);
            }
            GL.End();
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.Disable(EnableCap.Texture2D);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(!obj.Material.IsTransparent);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        private LightVertex[] BuildLightFace(SceneObject obj, int fi)
        {
            var result = new List<LightVertex>();
            var face = obj.Mesh.Faces[fi];
            if (face.Length < 3) return result.ToArray();
            var xf = SceneGeometry.Xform.For(obj);
            Vec3 a = obj.Mesh.Vertices[face[0]];
            // Adaptive density: a unit wall gets 4 steps; small faces get 1.
            for (int tri = 1; tri < face.Length - 1; tri++)
            {
                Vec3 b = obj.Mesh.Vertices[face[tri]], c = obj.Mesh.Vertices[face[tri + 1]];
                Vec3 n = Vec3.Cross(xf.Apply(b) - xf.Apply(a), xf.Apply(c) - xf.Apply(a)).Normalized();
                Vec3 nl = Vec3.Cross(b - a, c - a).Normalized();
                float edge = Math.Max((xf.Apply(b) - xf.Apply(a)).Length, (xf.Apply(c) - xf.Apply(a)).Length);
                int steps = Math.Max(1, Math.Min(8, (int)Math.Ceiling(edge * 4f)));
                LocalBounds(obj.Mesh, out Vec3 min, out Vec3 ext);
                var grid = new LightVertex[steps + 1, steps + 1];
                for (int i = 0; i <= steps; i++)
                for (int j = 0; j <= steps - i; j++)
                {
                    Vec3 local = a + (b - a) * (i / (float)steps) + (c - a) * (j / (float)steps);
                    Vec3 world = xf.Apply(local);
                    var v = new LightVertex { Position = world, UV = obj.Material.NoTile ? FitUV(local, nl, min, ext)
                        : SceneGeometry.PlanarUV(local, nl), Contributions = new Vec3[_localLights.Count] };
                    for (int k = 0; k < _localLights.Count; k++)
                    {
                        var light = _localLights[k];
                        var lm = light.Object.Material;
                        Vec3 delta = light.Position - world;
                        float distance = delta.Length;
                        if (distance >= lm.LightRange || distance < 0.001f) continue;
                        Vec3 direction = delta * (1f / distance);
                        float cosine = Math.Max(0f, Vec3.Dot(n, direction));
                        if (obj.Material.IsTransparent) cosine = Math.Abs(Vec3.Dot(n, direction));
                        if (cosine <= 0f) continue;
                        float falloff = 1f - distance / lm.LightRange;
                        float energy = lm.LightPower * falloff * falloff / (1f + distance * distance * 0.35f) * cosine;
                        Vec3 transmission = TraceTransmission(world + direction * 0.003f, light.Position, obj, light.Object);
                        Color color = lm.LightColor;
                        v.Contributions[k] = new Vec3(color.R / 255f * transmission.X,
                            color.G / 255f * transmission.Y, color.B / 255f * transmission.Z) * energy;
                    }
                    grid[i, j] = v;
                }
                for (int i = 0; i < steps; i++)
                for (int j = 0; j < steps - i; j++)
                {
                    result.Add(grid[i, j]); result.Add(grid[i + 1, j]); result.Add(grid[i, j + 1]);
                    if (i + j < steps - 1)
                    { result.Add(grid[i + 1, j]); result.Add(grid[i + 1, j + 1]); result.Add(grid[i, j + 1]); }
                }
            }
            return result.ToArray();
        }

        private Vec3 TraceTransmission(Vec3 start, Vec3 end, SceneObject receiver, SceneObject source)
        {
            Vec3 transmission = Vec3.One, ray = end - start;
            foreach (var blocker in _occluders)
            {
                var o = blocker.Object;
                if (o == receiver || o == source || !SegmentBounds(start, ray, blocker.Min, blocker.Max)) continue;
                float closest = 1f; Vec3 hit = Vec3.Zero, normal = Vec3.Zero;
                foreach (var face in o.Mesh.Faces)
                {
                    for (int i = 1; i + 1 < face.Length; i++)
                    {
                        int ia = face[0], ib = face[i], ic = face[i + 1];
                        if (!Intersect(start, ray, blocker.World[ia], blocker.World[ib], blocker.World[ic], out float t, out float u, out float v) || t >= closest) continue;
                        closest = t;
                        Vec3 a = o.Mesh.Vertices[ia], b = o.Mesh.Vertices[ib], c = o.Mesh.Vertices[ic];
                        hit = a + (b - a) * u + (c - a) * v;
                        normal = Vec3.Cross(b - a, c - a).Normalized();
                    }
                }
                if (closest >= 1f) continue;
                Material m = o.Material;
                float opacity = Clamp01(m.Opacity);
                Color tint = m.Color;
                if (m.HasTexture && m.Texture != null)
                {
                    LocalBounds(o.Mesh, out Vec3 min, out Vec3 ext);
                    PointF uv = m.NoTile ? FitUV(hit, normal, min, ext) : SceneGeometry.PlanarUV(hit, normal);
                    Bitmap bmp = m.Texture;
                    float u = uv.X - (float)Math.Floor(uv.X), v = uv.Y - (float)Math.Floor(uv.Y);
                    tint = bmp.GetPixel(Math.Min(bmp.Width - 1, (int)(u * bmp.Width)), Math.Min(bmp.Height - 1, (int)(v * bmp.Height)));
                    float texAlpha = tint.A / 255f;
                    if (m.AlphaMode == TextureAlphaMode.Cutout) texAlpha = texAlpha > m.AlphaCutoff ? 1f : 0f;
                    if (m.AlphaMode != TextureAlphaMode.Opaque) opacity *= texAlpha;
                }
                if (opacity < 0.001f) continue;
                if (!m.TransmitLight || opacity >= 0.995f) return Vec3.Zero;
                float pass = 1f - opacity;
                transmission = new Vec3(transmission.X * pass * (1f - opacity + opacity * tint.R / 255f),
                    transmission.Y * pass * (1f - opacity + opacity * tint.G / 255f),
                    transmission.Z * pass * (1f - opacity + opacity * tint.B / 255f));
                if (transmission.Length < 0.01f) return Vec3.Zero;
            }
            return transmission;
        }
        private static bool SegmentBounds(Vec3 origin, Vec3 ray, Vec3 min, Vec3 max)
        {
            float lo = 0f, hi = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = ray.Get(axis), p = origin.Get(axis);
                if (Math.Abs(d) < 1e-7f) { if (p < min.Get(axis) || p > max.Get(axis)) return false; continue; }
                float a = (min.Get(axis) - p) / d, b = (max.Get(axis) - p) / d;
                if (a > b) { float swap = a; a = b; b = swap; }
                lo = Math.Max(lo, a); hi = Math.Min(hi, b);
                if (lo > hi) return false;
            }
            return true;
        }
        private static bool Intersect(Vec3 origin, Vec3 ray, Vec3 a, Vec3 b, Vec3 c, out float t, out float u, out float v)
        {
            t = u = v = 0f;
            Vec3 e1 = b - a, e2 = c - a, p = Vec3.Cross(ray, e2);
            float determinant = Vec3.Dot(e1, p);
            if (Math.Abs(determinant) < 1e-7f) return false;
            float inv = 1f / determinant;
            Vec3 s = origin - a;
            u = Vec3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vec3 q = Vec3.Cross(s, e1);
            v = Vec3.Dot(ray, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vec3.Dot(e2, q) * inv;
            return t > 0.0001f && t < 0.9999f;
        }

    }
}
