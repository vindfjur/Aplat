using System;
using System.Drawing;
using Aplat.Models;

namespace Aplat.Rendering
{
    public enum ViewMode { Perspective, Top, Front, Side }

    public class Camera
    {
        // Zoom-out / fly distance is capped so the camera can't drift infinitely far
        // from the scene.
        public const float MinDistance = 0.3f;
        public const float MaxDistance = 80f;

        public float AngleX    { get; set; }  // pitch degrees
        public float AngleY    { get; set; }  // yaw degrees
        public float Distance  { get; set; }
        public Vec3  Target    { get; set; }
        public ViewMode Mode   { get; set; }
        public float OrthoScale { get; set; }

        public Camera()
        {
            AngleX    = 25f;
            AngleY    = -40f;
            Distance  = 5f;
            Target    = Vec3.Zero;
            Mode      = ViewMode.Perspective;
            OrthoScale = 3f;
        }

        // Frame an object: centre on it and pull the camera back just enough to fit
        // its bounding sphere (like pressing F in UE5). Works for perspective and
        // orthographic views; distance is clamped to the zoom-out limit.
        public void Frame(Vec3 center, float radius)
        {
            if (!(radius >= 0.05f)) radius = 0.05f;          // also catches NaN
            if (float.IsInfinity(radius)) radius = MaxDistance;
            if (float.IsNaN(center.X + center.Y + center.Z) || float.IsInfinity(center.X + center.Y + center.Z)) center = Vec3.Zero;
            Target = center;
            // The projection's focal length is min(w,h)*0.72, so an object of half-
            // extent r fits comfortably (~35% of the view) at distance ≈ r * 2.2.
            Distance   = Clamp(radius * 2.2f,  MinDistance, MaxDistance);
            OrthoScale = Clamp(radius * 1.35f, MinDistance, MaxDistance);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public Vec3 ToCameraSpace(Vec3 world)
        {
            var rel = world - Target;
            float ax = AngleX * (float)Math.PI / 180f;
            float ay = AngleY * (float)Math.PI / 180f;

            if (Mode == ViewMode.Perspective)
            {
                float cosY = (float)Math.Cos(ay), sinY = (float)Math.Sin(ay);
                float x1 = rel.X * cosY + rel.Z * sinY;
                float z1 = -rel.X * sinY + rel.Z * cosY;
                float y1 = rel.Y;

                float cosX = (float)Math.Cos(ax), sinX = (float)Math.Sin(ax);
                float y2 = y1 * cosX - z1 * sinX;
                float z2 = y1 * sinX + z1 * cosX;
                return new Vec3(x1, y2, z2 - Distance);
            }
            else if (Mode == ViewMode.Top)
                return new Vec3(rel.X, rel.Z, rel.Y);
            else if (Mode == ViewMode.Front)
                return new Vec3(rel.X, rel.Y, rel.Z);
            else // Side
                return new Vec3(-rel.Z, rel.Y, rel.X);
        }

        public PointF ProjectCamera(Vec3 cam, int w, int h)
        {
            float cx = w / 2f, cy = h / 2f;
            if (Mode == ViewMode.Perspective)
            {
                if (cam.Z >= -0.01f) return new PointF(-99999f, -99999f);
                float fov = Math.Min(w, h) * 0.72f;
                return new PointF(cam.X * fov / -cam.Z + cx, -cam.Y * fov / -cam.Z + cy);
            }
            else
            {
                float scale = Math.Min(w, h) / (OrthoScale * 2f);
                return new PointF(cam.X * scale + cx, -cam.Y * scale + cy);
            }
        }

        public PointF Project(Vec3 world, int w, int h)
        {
            return ProjectCamera(ToCameraSpace(world), w, h);
        }

        public float GetDepth(Vec3 world)
        {
            return ToCameraSpace(world).Z;
        }

        // Camera position in world space (for 3-D backface culling)
        public Vec3 GetPosition()
        {
            float ax = AngleX * (float)Math.PI / 180f;
            float ay = AngleY * (float)Math.PI / 180f;
            return Target + new Vec3(
                -(float)Math.Sin(ay) * (float)Math.Cos(ax) * Distance,
                (float)Math.Sin(ax) * Distance,
                (float)Math.Cos(ay) * (float)Math.Cos(ax) * Distance);
        }

        // Approximate "face-to-camera" unit direction for orthographic views
        public Vec3 GetOrthoViewDir()
        {
            switch (Mode)
            {
                case ViewMode.Top:   return new Vec3( 0,  1,  0);
                case ViewMode.Front: return new Vec3( 0,  0,  1);
                case ViewMode.Side:  return new Vec3( 1,  0,  0);
                default:             return new Vec3( 0,  0,  1);
            }
        }

        public Vec3 RightDir()
        {
            switch (Mode)
            {
                case ViewMode.Top:   return new Vec3(1, 0, 0);
                case ViewMode.Front: return new Vec3(1, 0, 0);
                case ViewMode.Side:  return new Vec3(0, 0, -1);
                default:
                    float ay = AngleY * (float)Math.PI / 180f;
                    return new Vec3((float)Math.Cos(ay), 0f, (float)Math.Sin(ay));
            }
        }

        public Vec3 UpDir()
        {
            if (Mode == ViewMode.Top) return new Vec3(0, 0, 1);
            return new Vec3(0, 1, 0);
        }

        public Vec3 ForwardDir()
        {
            if (Mode == ViewMode.Perspective)
            {
                float ay = AngleY * (float)Math.PI / 180f;
                return new Vec3((float)Math.Sin(ay), 0f, -(float)Math.Cos(ay));
            }
            return UpDir();
        }

        public float MoveStep()
        {
            float baseDist = Mode == ViewMode.Perspective ? Distance : OrthoScale;
            return baseDist * 0.06f;
        }

        // World-space direction that points "right" on screen — used for nudging
        // selected objects with the arrow keys so they move where the arrow points.
        public Vec3 ScreenRight()
        {
            switch (Mode)
            {
                case ViewMode.Top:   return new Vec3(1, 0, 0);
                case ViewMode.Front: return new Vec3(1, 0, 0);
                case ViewMode.Side:  return new Vec3(0, 0, -1);
                default:
                    float ay = AngleY * (float)Math.PI / 180f;
                    return new Vec3((float)Math.Cos(ay), 0f, (float)Math.Sin(ay));
            }
        }

        // World-space direction that points "up" on screen (the dual of ScreenRight).
        public Vec3 ScreenUp()
        {
            switch (Mode)
            {
                case ViewMode.Top:   return new Vec3(0, 0, 1);
                case ViewMode.Front: return new Vec3(0, 1, 0);
                case ViewMode.Side:  return new Vec3(0, 1, 0);
                default:
                    float ax = AngleX * (float)Math.PI / 180f;
                    float ay = AngleY * (float)Math.PI / 180f;
                    float sinX = (float)Math.Sin(ax), cosX = (float)Math.Cos(ax);
                    float sinY = (float)Math.Sin(ay), cosY = (float)Math.Cos(ay);
                    return new Vec3(sinX * sinY, cosX, -sinX * cosY);
            }
        }
    }
}
