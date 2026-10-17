using System;
using System.Collections.Generic;

namespace Aplat.Models
{
    [Serializable]
    public class Mesh
    {
        public List<Vec3> Vertices { get; set; }
        public List<int[]> Faces   { get; set; }

        public Mesh()
        {
            Vertices = new List<Vec3>();
            Faces    = new List<int[]>();
        }

        // A mesh read from a file may hold faces that point outside the vertex list or are
        // missing (null); everything downstream indexes blindly, so such faces are dropped.
        // Returns how many were removed.
        public int Sanitize()
        {
            if (Vertices == null) Vertices = new List<Vec3>();
            if (Faces == null) Faces = new List<int[]>();
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                var v = Vertices[i];
                if (float.IsNaN(v.X + v.Y + v.Z) || float.IsInfinity(v.X + v.Y + v.Z)) Vertices[i] = Vec3.Zero;   // NaN would poison bounds, camera and export
            }
            return Faces.RemoveAll(f =>
            {
                if (f == null || f.Length < 3) return true;
                foreach (int i in f) if (i < 0 || i >= n) return true;
                return false;
            });
        }

        public Mesh Clone()
        {
            var m = new Mesh();
            foreach (var v in Vertices) m.Vertices.Add(v);
            foreach (var f in Faces)    m.Faces.Add((int[])f.Clone());
            return m;
        }
    }
}
