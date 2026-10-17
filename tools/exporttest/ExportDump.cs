using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aplat.IO;
using Aplat.Models;
using Aplat.Primitives;

// Writes real OBJ / STL / FBX files of a small scene into a folder, to open them in other programs.
//   tools\paneltest\run.ps1 -Source tools\exporttest\ExportDump.cs     (files go to the folder printed at the end, in the temp directory)
class ExportDump
{
    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aplat-tests", "export"); Directory.CreateDirectory(dir);
        var list = new List<SceneObject>();
        foreach (PrimitiveKind k in Enum.GetValues(typeof(PrimitiveKind)))
        {
            var o = new SceneObject { Name = k.ToString(), PrimitiveType = k.ToString(), Mesh = PrimitiveFactory.Create(k), Position = new Vec3(list.Count * 1.5f, 0, 0) };
            o.Material.Color = System.Drawing.Color.FromArgb(255, 40 + 25 * list.Count, 200 - 20 * list.Count, 90);   // distinct colours: importers keep one material per object
            list.Add(o);
        }
        var r = ExportManager.BuildOBJ(list, "scene.mtl");
        File.WriteAllText(Path.Combine(dir, "scene.obj"), r.Obj); File.WriteAllText(Path.Combine(dir, "scene.mtl"), r.Mtl);
        File.WriteAllBytes(Path.Combine(dir, "scene.stl"), ExportManager.ToBinarySTL(list, true));
        File.WriteAllText(Path.Combine(dir, "scene-ascii.stl"), ExportManager.ToSTL(list));
        File.WriteAllBytes(Path.Combine(dir, "scene.fbx"), ExportManager.ToFBX(list));
        // a painted-looking material and a canvas, to check Ks/Ns/Ke/d and the canvas switch
        var mat = new SceneObject { Name = "Shiny", PrimitiveType = "Sphere", Mesh = PrimitiveFactory.Create(PrimitiveKind.Sphere), Position = new Vec3(0, 3, 0) };
        mat.Material.Color = System.Drawing.Color.FromArgb(255, 200, 100, 50); mat.Material.Opacity = 0.5f; mat.Material.Metallic = 0.8f;
        mat.Material.Roughness = 0.2f; mat.Material.EmissionColor = System.Drawing.Color.Lime; mat.Material.EmissionStrength = 0.4f;
        var withExtra = new List<SceneObject>(list) { mat, new SceneObject { Name = "Sheet", PrimitiveType = "Canvas", Mesh = CanvasFactory.CreateSheet(4, 3) } };
        var r2 = ExportManager.BuildOBJ(withExtra, "extra.mtl");
        File.WriteAllText(Path.Combine(dir, "extra.obj"), r2.Obj); File.WriteAllText(Path.Combine(dir, "extra.mtl"), r2.Mtl);
        File.WriteAllBytes(Path.Combine(dir, "extra.fbx"), ExportManager.ToFBX(withExtra));
        File.WriteAllBytes(Path.Combine(dir, "extra-with-sheet.stl"), ExportManager.ToBinarySTL(withExtra, true));
        File.WriteAllBytes(Path.Combine(dir, "extra-no-sheet.stl"), ExportManager.ToBinarySTL(withExtra, false));
        foreach (var o in list)
        {
            var one = new[] { o };
            File.WriteAllBytes(Path.Combine(dir, o.Name + ".stl"), ExportManager.ToBinarySTL(one, true));
        }
        Console.WriteLine("written " + list.Count + " objects to " + dir);
        return 0;
    }
}
