using System;
using System.Drawing;
using System.IO;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat.Primitives
{
    // The ruler as a scene object: a group of a wooden bar with real thickness and, on its two broad sides,
    // two thin plates, the face and the back, each carrying its own picture (the same ruler, the back in a
    // darker stain). All of them are real — lit, shadowed, hidden behind what stands in front — and move, turn and scale with
    // the ordinary tools. The group and its plates are of type "Ruler"; exports and painting skip them.
    public static class RulerFactory
    {
        public const string Type = "Ruler";
        public const float Depth = 0.045f;      // the bar's thickness, world units (the sheet is 4 wide)
        public const float Lift = 0.004f + Depth / 2f;   // the bar's middle above the sheet
        private const float Skin = 0.002f;      // the printed plates sit this far off the bar's sides

        public static bool IsRuler(SceneObject o) { return o != null && o.PrimitiveType == Type; }

        // `rotation` is the orientation of the sheet; the plates' own x runs along the ruler, y across.
        // `normal` is the sheet's front, `yAxis` the direction of the plates' y.
        public static SceneObject Create(Vec3 centre, Vec3 rotation, Vec3 normal, Vec3 yAxis,
                                         float length, float thick, Bitmap face, Bitmap back)
        {
            Vec3 middle = centre + normal * Lift;
            var face3 = Plate(Loc.T("Линейка"), middle + normal * (Depth / 2f + Skin), rotation, length, thick, face);
            var back3 = Plate(Loc.T("Линейка"), middle - normal * (Depth / 2f + Skin), rotation, length, thick, back);
            // Turned about its own up axis: the back reads the right way round when seen from behind.
            SceneGeometry.RotateAroundAxis(back3, back3.Position, yAxis, 180f);
            var group = new SceneObject { Name = Loc.T("Линейка"), IsGroup = true, PrimitiveType = Type };
            group.Children.Add(face3);
            group.Children.Add(back3);
            group.Children.Add(Bar(middle, rotation, length, thick));
            group.Position = middle;
            return group;
        }

        // The bar itself, a hair smaller than the plates so that their rounded corners hide its own; its sides
        // are what is seen when the ruler is looked at edge-on, a shade darker than the faces.
        private static SceneObject Bar(Vec3 position, Vec3 rotation, float length, float thick)
        {
            float hx = 0.497f, hy = thick / 2f * 0.96f, hz = Depth / 2f;
            var mesh = new Mesh();
            for (int z = 0; z < 2; z++)
            {
                float zz = z == 0 ? -hz : hz;
                mesh.Vertices.Add(new Vec3(-hx, -hy, zz)); mesh.Vertices.Add(new Vec3(hx, -hy, zz));
                mesh.Vertices.Add(new Vec3(hx, hy, zz));   mesh.Vertices.Add(new Vec3(-hx, hy, zz));
            }
            mesh.Faces.Add(new[] { 0, 1, 2, 3 }); mesh.Faces.Add(new[] { 4, 5, 6, 7 });
            mesh.Faces.Add(new[] { 0, 1, 5, 4 }); mesh.Faces.Add(new[] { 3, 2, 6, 7 });
            mesh.Faces.Add(new[] { 0, 3, 7, 4 }); mesh.Faces.Add(new[] { 1, 2, 6, 5 });
            var o = new SceneObject
            {
                Name = Loc.T("Линейка"), PrimitiveType = Type, Mesh = mesh,
                Position = position, Rotation = rotation, Scale = new Vec3(length, 1f, 1f)
            };
            o.Material.Color = Color.FromArgb(196, 146, 92);
            return o;
        }

        private static SceneObject Plate(string name, Vec3 position, Vec3 rotation, float length, float thick, Bitmap picture)
        {
            var mesh = new Mesh();
            float h = thick / 2f;
            mesh.Vertices.Add(new Vec3(-0.5f, -h, 0f));
            mesh.Vertices.Add(new Vec3( 0.5f, -h, 0f));
            mesh.Vertices.Add(new Vec3( 0.5f,  h, 0f));
            mesh.Vertices.Add(new Vec3(-0.5f,  h, 0f));
            mesh.Faces.Add(new[] { 0, 1, 2, 3 });
            var o = new SceneObject
            {
                Name = name, PrimitiveType = Type, Mesh = mesh,
                Position = position, Rotation = rotation, Scale = new Vec3(length, 1f, 1f)
            };
            SetPicture(o, picture);
            return o;
        }

        // The picture is embedded in the material (it travels with the file) and fitted once over the plate;
        // the transparent rounded corners are cut out, so they cast no shadow either.
        public static void SetPicture(SceneObject plate, Bitmap picture)
        {
            using (var ms = new MemoryStream())
            {
                picture.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                plate.Material.TextureBase64 = Convert.ToBase64String(ms.ToArray());
            }
            plate.Material.NoTile = true;
            plate.Material.AlphaMode = TextureAlphaMode.Cutout;
            plate.Material.Color = Color.White;
        }
    }
}
