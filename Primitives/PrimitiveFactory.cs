using System;
using Aplat.Models;

namespace Aplat.Primitives
{
    public enum PrimitiveKind { Cube, Sphere, Cylinder, Cone, Tetrahedron, Pyramid, Torus, Capsule }

    public static class PrimitiveFactory
    {
        public static Mesh Create(PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Sphere:      return CreateSphere();
                case PrimitiveKind.Cylinder:    return CreateCylinder();
                case PrimitiveKind.Cone:        return CreateCone();
                case PrimitiveKind.Tetrahedron: return CreateTetrahedron();
                case PrimitiveKind.Pyramid:     return CreatePyramid();
                case PrimitiveKind.Torus:       return CreateTorus();
                case PrimitiveKind.Capsule:     return CreateCapsule();
                default:                        return CreateCube();
            }
        }

        public static string KindName(PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Sphere:      return Loc.T("Сфера");
                case PrimitiveKind.Cylinder:    return Loc.T("Цилиндр");
                case PrimitiveKind.Cone:        return Loc.T("Конус");
                case PrimitiveKind.Tetrahedron: return Loc.T("Тетраэдр");
                case PrimitiveKind.Pyramid:     return Loc.T("Пирамида");
                case PrimitiveKind.Torus:       return Loc.T("Тор");
                case PrimitiveKind.Capsule:     return Loc.T("Капсула");
                default:                        return Loc.T("Куб");
            }
        }

        public static Mesh CreateCube(float size = 1f)
        {
            float h = size / 2f;
            var m = new Mesh();
            m.Vertices.Add(new Vec3(-h, -h, -h)); // 0
            m.Vertices.Add(new Vec3( h, -h, -h)); // 1
            m.Vertices.Add(new Vec3( h,  h, -h)); // 2
            m.Vertices.Add(new Vec3(-h,  h, -h)); // 3
            m.Vertices.Add(new Vec3(-h, -h,  h)); // 4
            m.Vertices.Add(new Vec3( h, -h,  h)); // 5
            m.Vertices.Add(new Vec3( h,  h,  h)); // 6
            m.Vertices.Add(new Vec3(-h,  h,  h)); // 7

            m.Faces.Add(new[]{0,2,1}); m.Faces.Add(new[]{0,3,2}); // front
            m.Faces.Add(new[]{5,7,4}); m.Faces.Add(new[]{5,6,7}); // back
            m.Faces.Add(new[]{4,3,0}); m.Faces.Add(new[]{4,7,3}); // left
            m.Faces.Add(new[]{1,6,5}); m.Faces.Add(new[]{1,2,6}); // right
            m.Faces.Add(new[]{3,6,2}); m.Faces.Add(new[]{3,7,6}); // top
            m.Faces.Add(new[]{4,1,5}); m.Faces.Add(new[]{4,0,1}); // bottom
            return m;
        }

        // The density is a compromise: a 12-segment silhouette was plainly faceted, and
        // on huge faces both the highlight and the reflection smeared. The faces are
        // quads rather than pairs of triangles — half as many of them, and the paint
        // atlas (one cell per face) does not shrink as the mesh gets denser.
        public static Mesh CreateSphere(float radius = 0.5f, int stacks = 14, int slices = 22)
        {
            var m = new Mesh();
            for (int i = 0; i <= stacks; i++)
            {
                float phi = (float)Math.PI * i / stacks - (float)Math.PI / 2f;
                for (int j = 0; j <= slices; j++)
                {
                    float theta = 2f * (float)Math.PI * j / slices;
                    m.Vertices.Add(new Vec3(
                        radius * (float)Math.Cos(phi) * (float)Math.Cos(theta),
                        radius * (float)Math.Sin(phi),
                        radius * (float)Math.Cos(phi) * (float)Math.Sin(theta)));
                }
            }
            for (int i = 0; i < stacks; i++)
                for (int j = 0; j < slices; j++)
                {
                    int a = i * (slices + 1) + j;
                    int b = a + 1, c = a + slices + 1, d = c + 1;
                    m.Faces.Add(new[]{a, c, d, b});   // the winding keeps the outward normal
                }
            return m;
        }

        public static Mesh CreateCylinder(float radius = 0.5f, float height = 1f, int seg = 24)
        {
            var m = new Mesh();
            float h = height / 2f;
            m.Vertices.Add(new Vec3(0,  h, 0)); // 0 top center
            m.Vertices.Add(new Vec3(0, -h, 0)); // 1 bottom center
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * (float)Math.PI * i / seg;
                float x = radius * (float)Math.Cos(a), z = radius * (float)Math.Sin(a);
                m.Vertices.Add(new Vec3(x,  h, z));
                m.Vertices.Add(new Vec3(x, -h, z));
            }
            for (int i = 0; i < seg; i++)
            {
                int ni = (i + 1) % seg;
                int t0 = 2 + i*2, t1 = 2 + ni*2;
                int b0 = t0 + 1,   b1 = t1 + 1;
                // Winding chosen so cross(v1-v0, v2-v0) points OUTWARD
                m.Faces.Add(new[]{0, t1, t0});   // top cap  → normal +Y
                m.Faces.Add(new[]{1, b0, b1});   // bottom cap → normal −Y
                m.Faces.Add(new[]{t0, b1, b0}); // side quad A → normal outward
                m.Faces.Add(new[]{t0, t1, b1}); // side quad B → normal outward
            }
            return m;
        }

        public static Mesh CreateCone(float radius = 0.5f, float height = 1f, int seg = 28)
        {
            var m = new Mesh();
            float h = height / 2f;
            m.Vertices.Add(new Vec3(0,  h, 0)); // 0 apex
            m.Vertices.Add(new Vec3(0, -h, 0)); // 1 base center
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * (float)Math.PI * i / seg;
                m.Vertices.Add(new Vec3(radius * (float)Math.Cos(a), -h, radius * (float)Math.Sin(a)));
            }
            for (int i = 0; i < seg; i++)
            {
                int ni = (i + 1) % seg;
                int b0 = 2 + i, b1 = 2 + ni;
                // cross(v1-v0, v2-v0) points outward
                m.Faces.Add(new[]{0, b1, b0});  // side  → normal outward+up
                m.Faces.Add(new[]{1, b0, b1});  // base  → normal −Y
            }
            return m;
        }

        // Square pyramid: base on the bottom, apex on top. Winding matches the cone
        // (cross(v1-v0, v2-v0) points outward) so picking and shading work the same.
        public static Mesh CreatePyramid(float size = 1f, float height = 1f)
        {
            float s = size / 2f, h = height / 2f;
            var m = new Mesh();
            m.Vertices.Add(new Vec3( 0,  h,  0)); // 0 apex
            m.Vertices.Add(new Vec3(-s, -h, -s)); // 1
            m.Vertices.Add(new Vec3( s, -h, -s)); // 2
            m.Vertices.Add(new Vec3( s, -h,  s)); // 3
            m.Vertices.Add(new Vec3(-s, -h,  s)); // 4
            m.Faces.Add(new[] { 1, 2, 3, 4 });    // base → normal −Y
            m.Faces.Add(new[] { 0, 2, 1 });       // sides → outward
            m.Faces.Add(new[] { 0, 3, 2 });
            m.Faces.Add(new[] { 0, 4, 3 });
            m.Faces.Add(new[] { 0, 1, 4 });
            return m;
        }

        // Torus around the Y axis: ring radius R, tube radius r. Triangles are wound
        // so cross(v1-v0, v2-v0) points outward (matches the picker's backface cull).
        public static Mesh CreateTorus(float R = 0.35f, float r = 0.15f, int segU = 24, int segV = 14)
        {
            var m = new Mesh();
            for (int i = 0; i < segU; i++)
            {
                float u = 2f * (float)Math.PI * i / segU;
                float cu = (float)Math.Cos(u), su = (float)Math.Sin(u);
                for (int j = 0; j < segV; j++)
                {
                    float v = 2f * (float)Math.PI * j / segV;
                    float cv = (float)Math.Cos(v), sv = (float)Math.Sin(v);
                    m.Vertices.Add(new Vec3((R + r * cv) * cu, r * sv, (R + r * cv) * su));
                }
            }
            for (int i = 0; i < segU; i++)
            {
                int ni = (i + 1) % segU;
                for (int j = 0; j < segV; j++)
                {
                    int nj = (j + 1) % segV;
                    int a = i  * segV + j;
                    int b = ni * segV + j;
                    int c = ni * segV + nj;
                    int d = i  * segV + nj;
                    m.Faces.Add(new[] { a, d, c, b });   // the same winding, but as a single face
                }
            }
            return m;
        }

        public static Mesh CreateTetrahedron(float size = 0.8f)
        {
            float a = size;
            var m = new Mesh();
            m.Vertices.Add(new Vec3(0, a, 0));
            m.Vertices.Add(new Vec3( (float)Math.Sqrt(8.0/9.0)*a,  -a/3f, 0));
            m.Vertices.Add(new Vec3(-(float)Math.Sqrt(2.0/9.0)*a,  -a/3f,  (float)Math.Sqrt(2.0/3.0)*a));
            m.Vertices.Add(new Vec3(-(float)Math.Sqrt(2.0/9.0)*a,  -a/3f, -(float)Math.Sqrt(2.0/3.0)*a));
            m.Faces.Add(new[]{0, 2, 1});
            m.Faces.Add(new[]{0, 3, 2});
            m.Faces.Add(new[]{0, 1, 3});
            m.Faces.Add(new[]{1, 2, 3});
            return m;
        }

        // Capsule: a cylindrical side between two hemispheres (as in Blender/Unity).
        // Built from latitude rings from the top pole to the bottom one; the hemisphere
        // equators are spaced apart by the cylinder height, so joining ring to ring
        // between them gives a straight vertical side. The winding is the same as for
        // the other primitives: cross(v1-v0, v2-v0) points outward.
        public static Mesh CreateCapsule(float radius = 0.32f, float cylHeight = 0.5f,
                                         int seg = 24, int rings = 6)
        {
            var m = new Mesh();
            float hc = cylHeight / 2f;

            int topPole = m.Vertices.Count;
            m.Vertices.Add(new Vec3(0, hc + radius, 0));

            // Interior latitude rings (R > 0), top → bottom, seg vertices each.
            var ringStart = new System.Collections.Generic.List<int>();

            // Top hemisphere: phi 90°→0° (k=1..rings), centre at +hc.
            for (int k = 1; k <= rings; k++)
            {
                float phi = (float)(Math.PI / 2.0) * (1f - (float)k / rings);
                AddCapsuleRing(m, ringStart, seg,
                    hc + radius * (float)Math.Sin(phi), radius * (float)Math.Cos(phi));
            }
            // Bottom hemisphere: phi 0°→−90° (k=0..rings-1), centre at −hc.
            for (int k = 0; k < rings; k++)
            {
                float phi = -(float)(Math.PI / 2.0) * ((float)k / rings);
                AddCapsuleRing(m, ringStart, seg,
                    -hc + radius * (float)Math.Sin(phi), radius * (float)Math.Cos(phi));
            }

            int bottomPole = m.Vertices.Count;
            m.Vertices.Add(new Vec3(0, -hc - radius, 0));

            // Top cap fan (center, next, cur) → outward, as the cylinder's top cap.
            int first = ringStart[0];
            for (int i = 0; i < seg; i++)
            {
                int ni = (i + 1) % seg;
                m.Faces.Add(new[] { topPole, first + ni, first + i });
            }
            // Ring-to-ring bands (one of them is the cylinder wall).
            for (int r = 0; r + 1 < ringStart.Count; r++)
            {
                int up = ringStart[r], lo = ringStart[r + 1];
                for (int i = 0; i < seg; i++)
                {
                    int ni = (i + 1) % seg;
                    int t0 = up + i, t1 = up + ni, b0 = lo + i, b1 = lo + ni;
                    m.Faces.Add(new[] { t0, b1, b0 });
                    m.Faces.Add(new[] { t0, t1, b1 });
                }
            }
            // Bottom cap fan (center, cur, next) → outward, as the cylinder's base.
            int last = ringStart[ringStart.Count - 1];
            for (int i = 0; i < seg; i++)
            {
                int ni = (i + 1) % seg;
                m.Faces.Add(new[] { bottomPole, last + i, last + ni });
            }
            return m;
        }

        private static void AddCapsuleRing(Mesh m, System.Collections.Generic.List<int> ringStart,
                                           int seg, float y, float r)
        {
            ringStart.Add(m.Vertices.Count);
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * (float)Math.PI * i / seg;
                m.Vertices.Add(new Vec3(r * (float)Math.Cos(a), y, r * (float)Math.Sin(a)));
            }
        }
    }
}
