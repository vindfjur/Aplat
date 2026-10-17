using System;
using Aplat.Models;

namespace Aplat.Rendering
{
    // Shadow-map arithmetic that needs no OpenGL context, so it can be tested headlessly:
    // is this GL version enough, how big the map is, how the sun's orthographic frustum is
    // fitted to the scene, which objects cast, and how large the depth bias is.
    // GLRenderer.Shadows.cs does the drawing; its shader repeats Bias() and FloorStrength().
    public static class ShadowMath
    {
        public const int DefaultMapSize = 2048;
        public const int MinMapSize = 256;

        // Below this height of the sun (its Y component) the shadows grow to a hundred
        // times the object's height and the map no longer holds them: no shadows.
        public const float MinSunY = 0.09f;

        // A shape thinner than this (opacity) lets the sun through and casts nothing.
        public const float MinCasterOpacity = 0.5f;

        // Environment variable APLAT_SHADOWS: "off" switches the shadows off as if the
        // driver could not do them; "force" skips the version / extension check (a context
        // that then fails to build the map or the shader still ends up without shadows).
        public const string EnvName = "APLAT_SHADOWS";

        // What the shadow map needs: GLSL (OpenGL 2.0), a depth texture (1.4) and framebuffer
        // objects (3.0, or ARB_framebuffer_object on an older driver). OpenGL ES is not used.
        public static bool IsSupported(string version, string extensions, string envOverride, out string reason)
        {
            reason = null;
            string env = (envOverride ?? "").Trim().ToLowerInvariant();
            if (env == "off") { reason = "switched off by " + EnvName; return false; }
            if (env == "force") return true;

            int major, minor;
            if (!TryParseVersion(version, out major, out minor))
            {
                reason = "unknown OpenGL version";
                return false;
            }
            if (major < 2)
            {
                reason = "needs OpenGL 2.0 (GLSL), found " + major + "." + minor;
                return false;
            }
            bool fbo = major >= 3 || HasExtension(extensions, "GL_ARB_framebuffer_object");
            if (!fbo)
            {
                reason = "no framebuffer objects";
                return false;
            }
            return true;
        }

        public static bool IsSupported(string version, string extensions, out string reason)
        {
            return IsSupported(version, extensions, null, out reason);
        }

        // "4.6.0 NVIDIA 551.23" → 4, 6. OpenGL ES ("OpenGL ES 3.0 ...") does not parse: the renderer
        // is built for desktop OpenGL.
        public static bool TryParseVersion(string version, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrEmpty(version)) return false;
            string s = version.TrimStart();
            if (s.StartsWith("OpenGL ES", StringComparison.OrdinalIgnoreCase)) return false;
            int i = 0;
            int a = 0, digits = 0;
            while (i < s.Length && char.IsDigit(s[i]) && digits < 4) { a = a * 10 + (s[i] - '0'); i++; digits++; }
            if (digits == 0 || i >= s.Length || s[i] != '.') return false;
            i++;
            int b = 0; digits = 0;
            while (i < s.Length && char.IsDigit(s[i]) && digits < 4) { b = b * 10 + (s[i] - '0'); i++; digits++; }
            if (digits == 0) return false;
            major = a; minor = b;
            return true;
        }

        // Whole-word match in the space-separated extension list.
        public static bool HasExtension(string extensions, string name)
        {
            if (string.IsNullOrEmpty(extensions) || string.IsNullOrEmpty(name)) return false;
            int at = 0;
            while ((at = extensions.IndexOf(name, at, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = at == 0 || extensions[at - 1] == ' ';
                int end = at + name.Length;
                bool endOk = end == extensions.Length || extensions[end] == ' ';
                if (startOk && endOk) return true;
                at = end;
            }
            return false;
        }

        // 2048 by default; the biggest power of two the GPU's textures allow if that is less;
        // 0 when even the smallest useful map does not fit.
        public static int ChooseMapSize(int maxTextureSize)
        {
            int size = DefaultMapSize;
            while (size > maxTextureSize && size > MinMapSize) size >>= 1;
            return size > maxTextureSize ? 0 : size;
        }

        public static bool SunUsable(Vec3 toSun) { return toSun.Y >= MinSunY; }

        // Whether a shape with this material casts a shadow into the map. A cut-out texture casts nothing
        // (the depth pass has no texture lookup, so its holes would be solid); a textured shape drawn with
        // alpha blending is solid as long as its opacity is.
        public static bool CastsShadow(float opacity, bool cutout)
        {
            if (cutout) return false;
            return opacity >= MinCasterOpacity;
        }

        // Depth bias for the receiving surface, in the map's 0..1 depth: grows with the slope
        // of the surface against the sun (`ndl` is the cosine between normal and sun), so a
        // grazing surface does not shadow itself, and stays tiny on a surface facing the sun,
        // so a shadow does not detach from its caster. `texel` is the world size of one map
        // texel and `span` the depth range of the map in world units. The fragment shader
        // computes the same: texel * (1 + 3 * tan) / span, tan clamped to 5.
        public static float Bias(float ndl, float texel, float span)
        {
            if (span < 1e-6f) span = 1e-6f;
            if (ndl > 1f) ndl = 1f;
            if (ndl < 0f) ndl = 0f;
            float sin = (float)Math.Sqrt(Math.Max(0f, 1f - ndl * ndl));
            float tan = Math.Min(sin / Math.Max(ndl, 0.2f), 5f);
            return texel * (1f + 3f * tan) / span;
        }

        // A low sun lays a longer, paler shadow on the floor, so it does not read as a dirty patch.
        public static float FloorStrength(float strength, float sunY)
        {
            float s = strength < 0f ? 0f : (strength > 1f ? 1f : strength);
            float y = sunY < 0f ? 0f : (sunY > 1f ? 1f : sunY);
            return s * (0.45f + 0.55f * y);
        }

        // The sun's orthographic view of the scene: the matrix, the map's texel size and
        // depth range, and the floor rectangle that can receive a shadow.
        public sealed class LightFit
        {
            public readonly double[] Matrix  = new double[16];   // world → clip, column-major (glLoadMatrixd)
            public readonly float[]  MatrixF = new float[16];    // the same for glUniformMatrix4fv
            public float Texel;                                  // world size of one map texel
            public float Span;                                   // world length of the depth range
            public float FloorMinX, FloorMinZ, FloorMaxX, FloorMaxZ;
        }

        // Fits the sun's frustum around the box min..max AND the places on the floor plane
        // (y = floorY) where its shadow can fall. `toSun` is the unit vector towards the sun.
        // The frustum is square, so texels are square. Returns false for a sun too low to cast.
        public static bool Fit(Vec3 toSun, Vec3 min, Vec3 max, float floorY, int mapSize, LightFit fit)
        {
            if (fit == null || mapSize < 1 || !SunUsable(toSun)) return false;
            Vec3 w = toSun.Normalized();
            Vec3 up = Math.Abs(w.Y) > 0.99f ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            Vec3 r = Vec3.Cross(up, w).Normalized();
            Vec3 u = Vec3.Cross(w, r);

            double xmin = double.MaxValue, xmax = -double.MaxValue;
            double ymin = double.MaxValue, ymax = -double.MaxValue;
            double smin = double.MaxValue, smax = -double.MaxValue;
            double fx0 = double.MaxValue, fx1 = -double.MaxValue, fz0 = double.MaxValue, fz1 = -double.MaxValue;

            for (int i = 0; i < 16; i++)
            {
                // Corner i % 8 of the box; the second eight are the same corners dropped on the floor.
                int c = i & 7;
                Vec3 p = new Vec3((c & 1) == 0 ? min.X : max.X, (c & 2) == 0 ? min.Y : max.Y, (c & 4) == 0 ? min.Z : max.Z);
                if (i >= 8)
                {
                    if (p.Y < floorY) continue;
                    float t = (p.Y - floorY) / w.Y;
                    p = p - w * t;
                }
                double x = Vec3.Dot(r, p), y = Vec3.Dot(u, p), s = -Vec3.Dot(w, p);
                if (x < xmin) xmin = x; if (x > xmax) xmax = x;
                if (y < ymin) ymin = y; if (y > ymax) ymax = y;
                if (s < smin) smin = s; if (s > smax) smax = s;
                if (p.X < fx0) fx0 = p.X; if (p.X > fx1) fx1 = p.X;
                if (p.Z < fz0) fz0 = p.Z; if (p.Z > fz1) fz1 = p.Z;
            }

            double cx = 0.5 * (xmin + xmax), cy = 0.5 * (ymin + ymax);
            double half = 0.5 * Math.Max(xmax - xmin, ymax - ymin) * 1.02 + 0.01;
            double pad = 0.02 * (smax - smin) + 0.01;
            double zlo = smin - pad, span = (smax + pad) - zlo;

            double[] m = fit.Matrix;
            // clip.x = (r·p − cx) / half, clip.y = (u·p − cy) / half, clip.z = 2 (−w·p − zlo) / span − 1
            SetRow(m, 0, r.X / half, r.Y / half, r.Z / half, -cx / half);
            SetRow(m, 1, u.X / half, u.Y / half, u.Z / half, -cy / half);
            SetRow(m, 2, -2 * w.X / span, -2 * w.Y / span, -2 * w.Z / span, -2 * zlo / span - 1);
            SetRow(m, 3, 0, 0, 0, 1);
            for (int i = 0; i < 16; i++) fit.MatrixF[i] = (float)m[i];

            fit.Texel = (float)(2 * half / mapSize);
            fit.Span = (float)span;
            fit.FloorMinX = (float)fx0; fit.FloorMaxX = (float)fx1;
            fit.FloorMinZ = (float)fz0; fit.FloorMaxZ = (float)fz1;
            return true;
        }

        private static void SetRow(double[] m, int row, double a, double b, double c, double d)
        {
            m[row] = a; m[4 + row] = b; m[8 + row] = c; m[12 + row] = d;
        }

        // world point → clip coordinates by a fitted matrix (for tests and sanity checks).
        public static void Project(LightFit fit, Vec3 p, out double x, out double y, out double z)
        {
            double[] m = fit.Matrix;
            x = m[0] * p.X + m[4] * p.Y + m[8]  * p.Z + m[12];
            y = m[1] * p.X + m[5] * p.Y + m[9]  * p.Z + m[13];
            z = m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14];
        }
    }

    // What the running OpenGL context can do, found out by the first frame and read by the
    // Lighting dialog and the View menu.
    public static class ShadowSupport
    {
        public static bool Known { get; private set; }
        public static bool Available { get; private set; }
        public static string Version { get; private set; }
        public static event EventHandler Changed;

        public static void Report(bool available, string version)
        {
            bool changed = !Known || Available != available;
            Known = true;
            Available = available;
            Version = string.IsNullOrEmpty(version) ? "?" : version;
            if (changed && Changed != null) Changed(null, EventArgs.Empty);
        }
    }
}
