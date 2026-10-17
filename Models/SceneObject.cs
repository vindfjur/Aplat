using System;
using System.Collections.Generic;

namespace Aplat.Models
{
    [Serializable]
    public class SceneObject
    {
        public string Id            { get; set; }
        public string Name          { get; set; }
        public string PrimitiveType { get; set; }
        public Mesh Mesh            { get; set; }
        public Vec3 Position        { get; set; }
        public Vec3 Rotation        { get; set; }
        public Vec3 Scale           { get; set; }
        public Material Material    { get; set; }
        public bool IsGroup         { get; set; }

        // Source parameters of a 3D-text object ("Text"), kept so the text can be
        // edited later and the mesh rebuilt. Null/0 in old files — fall back gracefully.
        public string TextContent   { get; set; }
        public string TextFontName  { get; set; }
        public float  TextWorldSize { get; set; }
        public float  TextDepth     { get; set; }   // extrusion / height; 0 = default (0.28)
        public bool IsVisible       { get; set; }
        public bool IsLocked        { get; set; }
        public List<SceneObject> Children { get; set; }

        [System.Xml.Serialization.XmlIgnore]
        public bool IsSelected { get; set; }

        public SceneObject()
        {
            Id            = Guid.NewGuid().ToString();
            Name          = "Object";
            PrimitiveType = "";
            Mesh          = new Mesh();
            Position      = Vec3.Zero;
            Rotation      = Vec3.Zero;
            Scale         = Vec3.One;
            Material      = new Material();
            IsVisible     = true;   // default; old files without the element stay visible
            IsLocked      = false;
            Children      = new List<SceneObject>();
        }

        public SceneObject Clone()
        {
            var c = new SceneObject
            {
                Id            = Guid.NewGuid().ToString(),
                // Don't stack the suffix: copying a copy must not yield "X Copy Copy".
                Name          = Name != null && Name.EndsWith(" Copy", StringComparison.Ordinal)
                                ? Name : Name + " Copy",
                PrimitiveType = PrimitiveType,
                Mesh          = Mesh.Clone(),
                Position      = Position,
                Rotation      = Rotation,
                Scale         = Scale,
                Material      = Material.Clone(),
            IsGroup       = IsGroup,
            IsVisible     = IsVisible,
            IsLocked      = IsLocked,
            TextContent   = TextContent,
            TextFontName  = TextFontName,
            TextWorldSize = TextWorldSize,
            TextDepth     = TextDepth
            };
            foreach (var ch in Children) c.Children.Add(ch.Clone());
            return c;
        }
    }
}
