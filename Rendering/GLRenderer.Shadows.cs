using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Rendering
{
    // Shadows of the sun by a shadow map.
    //
    // The scene is drawn from the sun (an orthographic view fitted to the scene) into a depth
    // texture; that happens only when the scene, the sun or the floor changes, never when the
    // camera orbits. Fixed-function lighting stays as it was. Afterwards every lit object, and the
    // floor, gets one more translucent black pass drawn by a small GLSL program: it looks each
    // pixel up in the map (soft 5×5 filter, slope-scaled bias, a nudge along the normal) and
    // darkens it by the shadow density where the sun cannot reach. Metal reflections, local lights,
    // paint and the transparency pass are drawn first, so the darkening covers them too.
    //
    // Needs OpenGL 2.0 and framebuffer objects (see ShadowMath.IsSupported). Where they are missing,
    // or the framebuffer or the program fails to build, there are simply no shadows: the first
    // frame reports it through ShadowSupport and nothing else changes.
    public partial class GLRenderer
    {
        private bool _shInit, _shOk;
        private int _shProg, _shFbo, _shTex, _shSize;
        private int _uMap, _uVP, _uSun, _uParam, _uFlip;

        private readonly ShadowMath.LightFit _shFit = new ShadowMath.LightFit();
        private readonly List<SceneObject> _shCasters = new List<SceneObject>();
        private bool  _shKeyValid, _shHave, _shActive;
        private int   _shSig;
        private float _shAz, _shEl, _shFloorY;
        private Vec3  _shSun;

        private const int ShadowTextureUnit = 3;

        // Vertex stage: world position and normal go to the fragment stage as they are (objects are
        // drawn with world coordinates; the model-view matrix is only the camera).
        private const string ShadowVertexSource =
            "#version 110\n" +
            "varying vec3 vPos;\n" +
            "varying vec3 vNrm;\n" +
            "void main()\n" +
            "{\n" +
            "    vPos = gl_Vertex.xyz;\n" +
            "    vNrm = gl_Normal;\n" +
            "    gl_Position = ftransform();\n" +
            "}\n";

        // Fragment stage: black with alpha = shadowed share of the sun's light. uParam is
        // (texel world size, depth range, density, 1 / map size); the bias is ShadowMath.Bias.
        private const string ShadowFragmentSource =
            "#version 110\n" +
            "uniform sampler2D uMap;\n" +
            "uniform mat4 uLightVP;\n" +
            "uniform vec3 uSun;\n" +
            "uniform vec4 uParam;\n" +
            "uniform float uFlip;\n" +
            "varying vec3 vPos;\n" +
            "varying vec3 vNrm;\n" +
            "void main()\n" +
            "{\n" +
            "    vec3 n = normalize(vNrm);\n" +
            "    if (uFlip > 0.5 && !gl_FrontFacing) n = -n;\n" +
            "    float ndl = dot(n, uSun);\n" +
            "    float direct = clamp(ndl * 4.0, 0.0, 1.0);\n" +
            "    if (direct <= 0.0) discard;\n" +
            "    float sinT = sqrt(max(0.0, 1.0 - ndl * ndl));\n" +
            "    float tanT = min(sinT / max(ndl, 0.2), 5.0);\n" +
            "    vec3 p = vPos + n * (uParam.x * 1.5 * sinT);\n" +
            "    vec3 q = (uLightVP * vec4(p, 1.0)).xyz * 0.5 + 0.5;\n" +
            "    if (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0 || q.z > 1.0) discard;\n" +
            "    float depth = q.z - uParam.x * (1.0 + 3.0 * tanT) / uParam.y;\n" +
            "    float lit = 0.0;\n" +
            "    for (int j = -2; j <= 2; j++)\n" +
            "    {\n" +
            "        for (int i = -2; i <= 2; i++)\n" +
            "        {\n" +
            "            float d = texture2D(uMap, q.xy + vec2(float(i), float(j)) * (1.5 * uParam.w)).r;\n" +
            "            if (depth <= d) lit += 1.0;\n" +
            "        }\n" +
            "    }\n" +
            "    float a = (1.0 - lit / 25.0) * direct * uParam.z;\n" +
            "    if (a < 0.002) discard;\n" +
            "    gl_FragColor = vec4(0.0, 0.0, 0.0, a);\n" +
            "}\n";

        // First frame, with the context current: is the shadow map possible here?
        private void InitShadows()
        {
            _shInit = true;
            string version = "";
            string extensions = "";
            try
            {
                version = GL.GetString(StringName.Version) ?? "";
                extensions = GL.GetString(StringName.Extensions) ?? "";
            }
            catch { }

            string reason;
            bool ok = ShadowMath.IsSupported(version, extensions, Environment.GetEnvironmentVariable(ShadowMath.EnvName), out reason);
            // A driver's version string can promise more than it exports; calling a missing entry
            // point would crash the process, so ask the context for every function first.
            if (ok && !EntryPointsExist(out reason)) ok = false;
            if (ok) ok = BuildShadowResources(out reason);
            _shOk = ok;
            ShadowSupport.Report(ok, version);
            System.Diagnostics.Debug.WriteLine("Aplat: shadow map " + (ok ? "ready, " + _shSize + "×" + _shSize : "off (" + reason + ")")
                + " — OpenGL " + version);
        }

        private static readonly string[] ShadowEntryPoints =
        {
            "glCreateShader", "glShaderSource", "glCompileShader", "glGetShaderiv", "glGetShaderInfoLog", "glDeleteShader",
            "glCreateProgram", "glAttachShader", "glLinkProgram", "glGetProgramiv", "glGetProgramInfoLog", "glDeleteProgram",
            "glUseProgram", "glGetUniformLocation", "glUniform1i", "glUniform1f", "glUniform3f", "glUniform4f", "glUniformMatrix4fv",
            "glGenFramebuffers", "glBindFramebuffer", "glFramebufferTexture2D", "glCheckFramebufferStatus", "glDeleteFramebuffers",
            "glActiveTexture"
        };

        private static bool EntryPointsExist(out string reason)
        {
            reason = null;
            var ctx = OpenTK.Graphics.GraphicsContext.CurrentContext as OpenTK.Graphics.IGraphicsContextInternal;
            if (ctx == null) { reason = "no OpenGL context"; return false; }
            foreach (string name in ShadowEntryPoints)
            {
                long address = ctx.GetAddress(name).ToInt64();
                if (address >= -1 && address <= 3)      // some drivers answer 0, 1, 2, 3 or -1 for "not there"
                {
                    reason = name + " is missing";
                    return false;
                }
            }
            return true;
        }

        private bool BuildShadowResources(out string reason)
        {
            reason = null;
            int prevFbo = 0;
            try
            {
                _shSize = ShadowMath.ChooseMapSize(MaxTextureSize);
                if (_shSize == 0) { reason = "texture limit too small"; return false; }

                while (GL.GetError() != ErrorCode.NoError) { }
                int vs = Compile(ShaderType.VertexShader, ShadowVertexSource, out reason);
                int fs = vs != 0 ? Compile(ShaderType.FragmentShader, ShadowFragmentSource, out reason) : 0;
                if (vs == 0 || fs == 0)
                {
                    if (vs != 0) GL.DeleteShader(vs);
                    if (fs != 0) GL.DeleteShader(fs);
                    return false;
                }
                _shProg = GL.CreateProgram();
                GL.AttachShader(_shProg, vs);
                GL.AttachShader(_shProg, fs);
                GL.LinkProgram(_shProg);
                int linked;
                GL.GetProgram(_shProg, GetProgramParameterName.LinkStatus, out linked);
                GL.DeleteShader(vs);
                GL.DeleteShader(fs);
                if (linked == 0)
                {
                    reason = "link failed: " + GL.GetProgramInfoLog(_shProg);
                    ReleaseShadowResources();
                    return false;
                }
                _uMap   = GL.GetUniformLocation(_shProg, "uMap");
                _uVP    = GL.GetUniformLocation(_shProg, "uLightVP");
                _uSun   = GL.GetUniformLocation(_shProg, "uSun");
                _uParam = GL.GetUniformLocation(_shProg, "uParam");
                _uFlip  = GL.GetUniformLocation(_shProg, "uFlip");
                if (_uMap < 0 || _uVP < 0 || _uSun < 0 || _uParam < 0 || _uFlip < 0)
                {
                    reason = "uniforms not found";
                    ReleaseShadowResources();
                    return false;
                }

                _shTex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, _shTex);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, _shSize, _shSize, 0,
                    OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
                _shFbo = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shFbo);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                    TextureTarget.Texture2D, _shTex, 0);
                GL.DrawBuffer(DrawBufferMode.None);
                GL.ReadBuffer(ReadBufferMode.None);
                FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    reason = "framebuffer incomplete (" + status + ")";
                    ReleaseShadowResources();
                    return false;
                }
                if (GL.GetError() != ErrorCode.NoError)
                {
                    reason = "OpenGL error while creating the map";
                    ReleaseShadowResources();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                try { GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo); } catch { }
                ReleaseShadowResources();
                return false;
            }
        }

        private static int Compile(ShaderType type, string source, out string reason)
        {
            reason = null;
            int sh = GL.CreateShader(type);
            GL.ShaderSource(sh, source);
            GL.CompileShader(sh);
            int ok;
            GL.GetShader(sh, ShaderParameter.CompileStatus, out ok);
            if (ok == 0)
            {
                reason = type + " failed: " + GL.GetShaderInfoLog(sh);
                GL.DeleteShader(sh);
                return 0;
            }
            return sh;
        }

        private void ReleaseShadowResources()
        {
            try
            {
                if (_shProg != 0) GL.DeleteProgram(_shProg);
                if (_shFbo != 0) GL.DeleteFramebuffer(_shFbo);
                if (_shTex != 0) GL.DeleteTexture(_shTex);
            }
            catch { }
            _shProg = _shFbo = _shTex = 0;
        }

        // Per frame, before anything is drawn: brings the map up to date (when the scene, the sun or
        // the floor changed) and decides whether this frame has shadows.
        private void PrepareShadows(Scene scene, LightSettings light, float floorY)
        {
            _shActive = false;
            if (!_shInit) InitShadows();
            if (!_shOk || scene == null || !_lit || light == null || !light.Shadows || !light.AboveHorizon) return;

            Vec3 sun = light.Direction;
            if (!ShadowMath.SunUsable(sun)) return;
            _shSun = sun;
            _shStrength = light.ShadowStrength;

            try
            {
                bool same = _shKeyValid && _shSig == _lightingSignature && _shAz == light.Azimuth &&
                            _shEl == light.Elevation && _shFloorY == floorY;
                if (!same)
                {
                    _shSig = _lightingSignature; _shAz = light.Azimuth; _shEl = light.Elevation; _shFloorY = floorY;
                    _shKeyValid = true;
                    if (!RenderShadowMap(scene, sun, floorY)) throw new InvalidOperationException("OpenGL error in the shadow pass");
                }
                if (!_shHave) return;

                // Constants of the frame; the program keeps them until the next change.
                GL.UseProgram(_shProg);
                GL.UniformMatrix4(_uVP, 1, false, _shFit.MatrixF);
                GL.Uniform3(_uSun, sun.X, sun.Y, sun.Z);
                GL.Uniform1(_uMap, ShadowTextureUnit);
                GL.UseProgram(0);
                GL.ActiveTexture(TextureUnit.Texture0 + ShadowTextureUnit);
                GL.BindTexture(TextureTarget.Texture2D, _shTex);
                GL.ActiveTexture(TextureUnit.Texture0);
                _shActive = true;
            }
            catch (Exception ex)
            {
                // The driver let us down mid-way: no shadows from now on, quietly.
                System.Diagnostics.Debug.WriteLine("Aplat: shadow map failed, shadows off — " + ex.Message);
                _shOk = false;
                _shHave = false;
                _shActive = false;
                ReleaseShadowResources();
                ShadowSupport.Report(false, GL.GetString(StringName.Version));
            }
        }

        private bool RenderShadowMap(Scene scene, Vec3 sun, float floorY)
        {
            _shHave = false;
            _shCasters.Clear();
            Vec3 min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vec3 max = new Vec3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
            foreach (var o in scene.Objects) GatherCasters(o, ref min, ref max);
            if (_shCasters.Count == 0) return true;               // nothing casts: an empty frame, not an error
            if (!ShadowMath.Fit(sun, min, max, floorY, _shSize, _shFit)) return true;

            while (GL.GetError() != ErrorCode.NoError) { }
            int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shFbo);
            GL.Viewport(0, 0, _shSize, _shSize);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.CullFace);
            GL.ColorMask(false, false, false, false);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadMatrix(_shFit.Matrix);

            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(1.1f, 2f);
            foreach (var o in _shCasters) DrawCaster(o);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
            GL.PopAttrib();

            int casters = _shCasters.Count;
            _shCasters.Clear();
            if (GL.GetError() != ErrorCode.NoError) return false;
            _shHave = true;
            DumpShadowMap(casters, sun, floorY, min, max);
            return true;
        }

        // Developer aid: with APLAT_SHADOW_DUMP=<folder> every redraw of the map writes shadowmap.pgm (the depth
        // texture, near = dark) and shadowmap.txt (the fit and the GL strings) there.
        private void DumpShadowMap(int casters, Vec3 sun, float floorY, Vec3 min, Vec3 max)
        {
            string dir = Environment.GetEnvironmentVariable("APLAT_SHADOW_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                var depth = new float[_shSize * _shSize];
                GL.BindTexture(TextureTarget.Texture2D, _shTex);
                GL.GetTexImage(TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.Float, depth);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                var bytes = new byte[depth.Length];
                int hit = 0;
                for (int i = 0; i < depth.Length; i++)
                {
                    float d = depth[i];
                    if (d < 0.99999f) hit++;
                    bytes[i] = (byte)(Math.Max(0f, Math.Min(1f, d)) * 255f);
                }
                using (var f = System.IO.File.Create(System.IO.Path.Combine(dir, "shadowmap.pgm")))
                {
                    var head = System.Text.Encoding.ASCII.GetBytes("P5\n" + _shSize + " " + _shSize + "\n255\n");
                    f.Write(head, 0, head.Length);
                    f.Write(bytes, 0, bytes.Length);
                }
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "shadowmap.txt"),
                    "GL " + GL.GetString(StringName.Version) + " | " + GL.GetString(StringName.Renderer) + "\n" +
                    "size " + _shSize + " casters " + casters + " covered texels " + hit + "\n" +
                    "sun " + sun.X.ToString(ic) + " " + sun.Y.ToString(ic) + " " + sun.Z.ToString(ic) + " floorY " + floorY.ToString(ic) + "\n" +
                    "bounds " + min + " .. " + max + "\n" +
                    "texel " + _shFit.Texel.ToString(ic) + " span " + _shFit.Span.ToString(ic) + " floor " +
                    _shFit.FloorMinX.ToString(ic) + " " + _shFit.FloorMinZ.ToString(ic) + " " + _shFit.FloorMaxX.ToString(ic) + " " + _shFit.FloorMaxZ.ToString(ic) + "\n" +
                    "matrix " + string.Join(" ", Array.ConvertAll(_shFit.MatrixF, v => v.ToString("R", ic))) + "\n");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Aplat: shadow dump failed — " + ex.Message); }
        }

        private void GatherCasters(SceneObject o, ref Vec3 min, ref Vec3 max)
        {
            if (!o.IsVisible) return;
            if (o.IsGroup)
            {
                foreach (var ch in o.Children) GatherCasters(ch, ref min, ref max);
                return;
            }
            if (o.Mesh == null || o.Material == null || o.Mesh.Faces.Count == 0) return;
            var m = o.Material;
            bool cutout  = m.HasTexture && m.AlphaMode == TextureAlphaMode.Cutout;
            if (!ShadowMath.CastsShadow(m.Opacity, cutout)) return;
            Vec3 mn, mx;
            if (!SceneGeometry.WorldBounds(o, out mn, out mx)) return;
            if (o.PrimitiveType == "Canvas")
            {
                // The sheet is a board with a thickness behind it (see ShadowCanvas).
                float pad = CanvasBack.Depth * Math.Max(mx.X - mn.X, Math.Max(mx.Y - mn.Y, mx.Z - mn.Z));
                mn = mn - new Vec3(pad, pad, pad);
                mx = mx + new Vec3(pad, pad, pad);
            }
            if (mn.X < min.X) min.X = mn.X; if (mn.Y < min.Y) min.Y = mn.Y; if (mn.Z < min.Z) min.Z = mn.Z;
            if (mx.X > max.X) max.X = mx.X; if (mx.Y > max.Y) max.Y = mx.Y; if (mx.Z > max.Z) max.Z = mx.Z;
            _shCasters.Add(o);
        }

        private void DrawCaster(SceneObject obj)
        {
            var mesh = obj.Mesh;
            var xf = SceneGeometry.Xform.For(obj);
            if (obj.PrimitiveType == "Canvas") { ShadowCanvas(mesh, xf); return; }
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face.Length < 3) continue;
                GL.Begin(PrimitiveType.TriangleFan);
                for (int i = 0; i < face.Length; i++)
                {
                    Vec3 w = xf.Apply(mesh.Vertices[face[i]]);
                    GL.Vertex3(w.X, w.Y, w.Z);
                }
                GL.End();
            }
        }

        // The sheet is an opaque board with thickness (see DrawCanvasBack), so it shades like any
        // object: the paper and its depth, as the six faces of a thin box.
        private static void ShadowCanvas(Mesh mesh, SceneGeometry.Xform xf)
        {
            if (mesh.Faces.Count == 0 || mesh.Faces[0].Length != 4) return;
            var face = mesh.Faces[0];
            var front = new Vec3[4];
            for (int i = 0; i < 4; i++) front[i] = xf.Apply(mesh.Vertices[face[i]]);
            float width = (front[1] - front[0]).Length, height = (front[3] - front[0]).Length;
            if (width < 1e-5f || height < 1e-5f) return;

            Vec3 n = Vec3.Cross(front[1] - front[0], front[2] - front[0]).Normalized();
            Vec3 back = n * -(CanvasBack.Depth * Math.Min(width, height));
            var rear = new Vec3[4];
            for (int i = 0; i < 4; i++) rear[i] = front[i] + back;

            ShadowQuad(front[0], front[1], front[2], front[3]);
            ShadowQuad(rear[0], rear[1], rear[2], rear[3]);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                ShadowQuad(front[i], front[j], rear[j], rear[i]);
            }
        }

        private static void ShadowQuad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex3(a.X, a.Y, a.Z); GL.Vertex3(b.X, b.Y, b.Z);
            GL.Vertex3(c.X, c.Y, c.Z); GL.Vertex3(d.X, d.Y, d.Z);
            GL.End();
        }

        // The floor is not geometry, so it gets an invisible quad that shows only the shadow.
        private void DrawShadowFloor(LightSettings L, float floorY)
        {
            if (!_shActive) return;
            float pad = _shFit.Texel * 2f;
            float x0 = _shFit.FloorMinX - pad, x1 = _shFit.FloorMaxX + pad;
            float z0 = _shFit.FloorMinZ - pad, z1 = _shFit.FloorMaxZ + pad;
            float y = floorY + 0.0015f;

            BeginShadowPass();
            GL.Uniform1(_uFlip, 0f);
            GL.Uniform4(_uParam, _shFit.Texel, _shFit.Span, ShadowMath.FloorStrength(L.ShadowStrength, _shSun.Y), 1f / _shSize);
            GL.PolygonOffset(-2.5f, -2.5f);
            GL.Normal3(0f, 1f, 0f);
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex3(x0, y, z0); GL.Vertex3(x0, y, z1); GL.Vertex3(x1, y, z1); GL.Vertex3(x1, y, z0);
            GL.End();
            EndShadowPass(false);
        }

        // Darkens an object where the map says the sun does not reach it. Called after everything
        // else of the object is drawn; the same vertices as the base pass, so the depth test is exact.
        private void DrawShadowOnObject(SceneObject obj, Mesh mesh, SceneGeometry.Xform xf, Vec3[][] smooth,
                                        float alpha, int onlyFace, bool transparentPass)
        {
            BeginShadowPass();
            GL.Uniform1(_uFlip, 1f);
            GL.Uniform4(_uParam, _shFit.Texel, _shFit.Span, Clamp01(_shStrength) * alpha, 1f / _shSize);
            GL.PolygonOffset(-1.5f, -1.5f);

            int first = onlyFace < 0 ? 0 : onlyFace;
            int last  = onlyFace < 0 ? mesh.Faces.Count : onlyFace + 1;
            for (int fi = first; fi < last; fi++)
            {
                var face = mesh.Faces[fi];
                int fl = face.Length;
                if (fl < 3) continue;
                EnsureScratch(fl);
                for (int i = 0; i < fl; i++) _wv[i] = xf.Apply(mesh.Vertices[face[i]]);
                Vec3 n = FaceNormal(_wv, fl);
                Vec3[] fn = smooth != null && fi < smooth.Length ? smooth[fi] : null;
                GL.Begin(PrimitiveType.TriangleFan);
                for (int i = 0; i < fl; i++)
                {
                    Normal(fn, n, i, xf, true);
                    GL.Vertex3(_wv[i].X, _wv[i].Y, _wv[i].Z);
                }
                GL.End();
            }
            EndShadowPass(transparentPass);
        }

        private float _shStrength;

        private void BeginShadowPass()
        {
            GL.UseProgram(_shProg);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.AlphaTest);
            GL.DepthMask(false);
            GL.Enable(EnableCap.PolygonOffsetFill);
        }

        private void EndShadowPass(bool keepDepthMaskOff)
        {
            GL.UseProgram(0);
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(0f, 0f);
            GL.DepthMask(!keepDepthMaskOff);
            if (_lit) GL.Enable(EnableCap.Lighting);
        }

        // Which objects take a shadow: lit solids, glass included (scaled by its opacity). The paper
        // stays white, cut-out textures would be darkened in their holes, and a lamp is its own light.
        private bool ReceivesShadow(SceneObject obj, bool lit, bool flatPaper)
        {
            if (!_shActive || !lit || flatPaper) return false;
            var m = obj.Material;
            if (m.HasTexture && m.AlphaMode == TextureAlphaMode.Cutout) return false;
            if (m.LightEnabled || m.EmissionStrength >= 0.5f) return false;
            return true;
        }
    }
}
