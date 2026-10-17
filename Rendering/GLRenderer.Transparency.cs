using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL;
using Aplat.Models;

namespace Aplat.Rendering
{
    public partial class GLRenderer
    {
        private sealed class TransparentFace
        {
            public SceneObject Object;
            public int Index;
            public bool ParentSelected;
            public float Depth;
        }
        private void DrawTransparentScene(Scene scene, SceneGeometry settings, LightSettings light)
        {
            var faces = new List<TransparentFace>();
            foreach (var o in scene.Objects) GatherTransparent(o, false, faces);
            // Sort faces across all objects and groups, back to front in camera space.
            faces.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var face in faces)
                DrawObject(face.Object, settings, face.ParentSelected, light, true, face.Index);
            if (!settings.OutlineSelection)
            {
                var marked = new HashSet<SceneObject>();
                foreach (var face in faces)
                    if ((face.Object.IsSelected || face.ParentSelected) && marked.Add(face.Object)) DrawSelectionBox(face.Object);
            }
        }
        private void GatherTransparent(SceneObject o, bool parentSelected, List<TransparentFace> faces)
        {
            if (!o.IsVisible) return;
            if (o.IsGroup)
            {
                foreach (var ch in o.Children) GatherTransparent(ch, parentSelected || o.IsSelected, faces);
                return;
            }
            if (o.Mesh == null || !o.Material.IsTransparent || o.Material.Opacity <= 0f) return;
            var xf = SceneGeometry.Xform.For(o);
            for (int fi = 0; fi < o.Mesh.Faces.Count; fi++)
            {
                var f = o.Mesh.Faces[fi];
                if (f.Length < 3) continue;
                Vec3 center = Vec3.Zero;
                foreach (int vi in f) center = center + xf.Apply(o.Mesh.Vertices[vi]);
                center = center * (1f / f.Length);
                faces.Add(new TransparentFace { Object = o, Index = fi, ParentSelected = parentSelected,
                    Depth = _cam.GetDepth(center) });
            }
        }
        private int MaterialTexture(Material material)
        {
            Bitmap source = material.Texture;
            if (source == null) return 0;
            string key = material.TextureKey + ":alpha:" + (int)material.AlphaMode + ":" + material.AlphaCutoff;
            _fileSeen.Add(key);
            if (!_fileTex.TryGetValue(key, out int id))
            {
                if (material.AlphaMode == TextureAlphaMode.Blend) id = Upload(source);
                else
                {
                    using (var copy = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(copy))
                        { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                        var data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadWrite,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            byte[] row = new byte[copy.Width * 4];
                            for (int y = 0; y < copy.Height; y++)
                            {
                                IntPtr ptr = IntPtr.Add(data.Scan0, y * data.Stride);
                                System.Runtime.InteropServices.Marshal.Copy(ptr, row, 0, row.Length);
                                for (int x = 0; x < copy.Width; x++)
                                    row[x * 4 + 3] = material.AlphaMode == TextureAlphaMode.Opaque ||
                                        row[x * 4 + 3] / 255f > material.AlphaCutoff ? (byte)255 : (byte)0;
                                System.Runtime.InteropServices.Marshal.Copy(row, 0, ptr, row.Length);
                            }
                        }
                        finally { copy.UnlockBits(data); }
                        id = Upload(copy);
                    }
                }
                _fileTex[key] = id;
            }
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                material.PixelTexture ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                material.PixelTexture ? (int)TextureMagFilter.Nearest : (int)TextureMagFilter.Linear);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return id;
        }
    }
}
