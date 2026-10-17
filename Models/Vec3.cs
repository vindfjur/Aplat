using System;

namespace Aplat.Models
{
    [Serializable]
    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 One  = new Vec3(1, 1, 1);

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public float Get(int axis)
        {
            if (axis == 0) return X;
            if (axis == 1) return Y;
            return Z;
        }

        public Vec3 With(int axis, float value)
        {
            if (axis == 0) return new Vec3(value, Y, Z);
            if (axis == 1) return new Vec3(X, value, Z);
            return new Vec3(X, Y, value);
        }

        public Vec3 Normalized()
        {
            float l = Length;
            return l < 1e-6f ? Zero : new Vec3(X / l, Y / l, Z / l);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return a * s; }

        public static float Dot(Vec3 a, Vec3 b)   { return a.X*b.X + a.Y*b.Y + a.Z*b.Z; }
        public static Vec3  Cross(Vec3 a, Vec3 b)  { return new Vec3(a.Y*b.Z - a.Z*b.Y, a.Z*b.X - a.X*b.Z, a.X*b.Y - a.Y*b.X); }

        public override string ToString() { return string.Format("({0:F2}, {1:F2}, {2:F2})", X, Y, Z); }
    }
}
