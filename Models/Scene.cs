using System;
using System.Collections.Generic;
using System.Drawing;
using System.Xml.Serialization;

namespace Aplat.Models
{
    [Serializable]
    public class Scene
    {
        public List<SceneObject> Objects { get; set; }
        public string Name { get; set; }
        public float Brightness { get; set; }

        // Undo history persisted in the .aplat (last N action snapshots, oldest first) so
        // edits can be undone after reopening the file. Each entry is itself a serialized
        // Scene XML; it is stripped out while creating snapshots to avoid recursion.
        public List<string> History { get; set; }

        // ── Canvas / grid (persisted in .aplat) ─────────────────────
        public bool  ShowGrid       { get; set; }
        public bool  FixedGrid      { get; set; }
        public float GridExtent     { get; set; }   // half-size of the floor (canvas size)
        public float GridSpacing    { get; set; }   // distance between lines (grid format)
        public int   BackgroundArgb { get; set; }
        public int   GridColorArgb  { get; set; }

        // ── View toggles (persisted in .aplat) ─────────────────────
        public bool ShowWireframe { get; set; }
        public bool ShowVertices  { get; set; }
        public bool OutlineSelection { get; set; }

        // ── Sun / lighting (persisted in .aplat) ───────────────────
        // Never null: old files without a <Light> element get the defaults from the
        // LightSettings constructor.
        public LightSettings Light { get; set; }

        // ── Auto-rotation (persisted in .aplat) ────────────────────
        public bool  AutoRotate        { get; set; }
        public float AutoRotateSpeed   { get; set; }   // degrees per second
        public bool  AutoRotateObjects { get; set; }   // true → the object spins, otherwise the camera

        // ── Camera state (persisted in .aplat) ──────────────────────
        // HasCameraState stays false for old files so they keep the default view.
        public bool  HasCameraState  { get; set; }
        public int   CameraMode      { get; set; }   // (int)Rendering.ViewMode
        public float CameraAngleX    { get; set; }
        public float CameraAngleY    { get; set; }
        public float CameraDistance  { get; set; }
        public float CameraOrthoScale { get; set; }
        public Vec3  CameraTarget    { get; set; }

        [XmlIgnore]
        public Color BackgroundColor
        {
            get { return Color.FromArgb(BackgroundArgb); }
            set { BackgroundArgb = value.ToArgb(); }
        }

        [XmlIgnore]
        public Color GridColor
        {
            get { return Color.FromArgb(GridColorArgb); }
            set { GridColorArgb = value.ToArgb(); }
        }

        [XmlIgnore] public string FilePath   { get; set; }
        [XmlIgnore] public bool   IsModified { get; set; }

        public Scene()
        {
            Objects    = new List<SceneObject>();
            History    = new List<string>();
            Name       = "Untitled";
            Brightness = 2f;   // the lighting control is gone from the UI — keep the maximum by default
            IsModified = false;

            ShowGrid       = true;
            FixedGrid      = false;
            GridExtent     = 5f;
            GridSpacing    = 0.5f;
            BackgroundArgb = Color.FromArgb(50, 50, 53).ToArgb();
            GridColorArgb  = Color.FromArgb(55, 100, 100, 100).ToArgb();

            OutlineSelection  = true;   // previous default behaviour
            Light             = new LightSettings();
            AutoRotate        = false;
            AutoRotateSpeed   = 24f;
            AutoRotateObjects = false;
        }

        // Encode every object's paint layer to base64 right before the scene is
        // serialized to a .aplat (or to an undo snapshot).
        // `snapshot`: an undo snapshot, which leaves out what a journal can rebuild (Material.SnapshotMode).
        public void PrepareForSerialize(bool snapshot = false)
        {
            foreach (var o in Objects) PrepareForSerialize(o, snapshot);
        }

        private static void PrepareForSerialize(SceneObject o, bool snapshot)
        {
            if (o.Material != null) { o.Material.SyncPaintForSave(snapshot); o.Material.EmbedTexture(); }
            foreach (var ch in o.Children) PrepareForSerialize(ch, snapshot);
        }
    }

    // A real light source (the "sun"): a directional light with an azimuth and an
    // elevation above the horizon. The renderer uses it for diffuse shading,
    // highlights (Blinn/Phong through the OpenGL fixed-function pipeline) and
    // shadows (a shadow map).
    //
    // Lives in the same file as Scene because the project is built from an
    // explicit <Compile> list in the .csproj; the related type sits next to its owner.
    [Serializable]
    public class LightSettings
    {
        public bool  Enabled   { get; set; }
        public float Azimuth   { get; set; }   // 0..360°, rotation around the horizon
        public float Elevation { get; set; }   // -10..90°, height above the horizon
        public float Intensity { get; set; }   // 0..2, strength of the direct light
        public float Ambient   { get; set; }   // 0..1, diffuse "sky" light
        public int   ColorArgb        { get; set; }   // sun colour
        public int   AmbientColorArgb { get; set; }   // fill-light colour
        public bool  Specular       { get; set; }     // surface highlights
        public bool  Shadows        { get; set; }     // shadows (shadow map); false — no shadows
        public float ShadowStrength { get; set; }     // 0..1, shadow density
        public bool  ShowSun        { get; set; }     // helper marker for the light source

        public LightSettings()
        {
            Enabled          = true;
            Azimuth          = 135f;
            Elevation        = 48f;
            Intensity        = 1f;
            Ambient          = 0.34f;
            ColorArgb        = Color.FromArgb(255, 255, 246, 226).ToArgb();
            AmbientColorArgb = Color.FromArgb(255, 150, 170, 205).ToArgb();
            Specular         = true;
            Shadows          = true;
            ShadowStrength   = 0.42f;
            ShowSun          = false;
        }

        [XmlIgnore]
        public Color Color
        {
            get { return Color.FromArgb(ColorArgb); }
            set { ColorArgb = value.ToArgb(); }
        }

        [XmlIgnore]
        public Color AmbientColor
        {
            get { return Color.FromArgb(AmbientColorArgb); }
            set { AmbientColorArgb = value.ToArgb(); }
        }

        // Unit vector TOWARDS the sun (the light travels the opposite way).
        [XmlIgnore]
        public Vec3 Direction
        {
            get
            {
                double az = Azimuth   * Math.PI / 180.0;
                double el = Elevation * Math.PI / 180.0;
                double ce = Math.Cos(el);
                var d = new Vec3((float)(ce * Math.Sin(az)),
                                 (float)Math.Sin(el),
                                 (float)(ce * Math.Cos(az)));
                return d.Normalized();
            }
        }

        // With the sun below the horizon, direct light and shadow switch off by themselves.
        [XmlIgnore]
        public bool AboveHorizon { get { return Elevation > 1.5f; } }

        // "Time of day", 0..24 h: the sun follows an arc from east to west and
        // reddens near the horizon — one slider instead of three settings.
        public void ApplyTimeOfDay(float hours)
        {
            if (hours < 0f) hours = 0f;
            if (hours > 24f) hours = 24f;
            // 6:00 — sunrise (azimuth 90°), 12:00 — zenith (180°), 18:00 — sunset (270°).
            float t = (hours - 6f) / 12f;              // 0 at sunrise, 1 at sunset
            Azimuth   = 90f + t * 180f;
            Elevation = (float)(Math.Sin(Math.PI * t) * 72f) - 4f;
            if (Elevation < -10f) Elevation = -10f;

            // Warm light near the horizon, neutral at the zenith.
            float warm = 1f - Math.Max(0f, Math.Min(1f, Elevation / 45f));
            int r = 255;
            int g = (int)(246 - 70 * warm);
            int b = (int)(226 - 150 * warm);
            Color   = System.Drawing.Color.FromArgb(255, r, Clamp8(g), Clamp8(b));
            Ambient = 0.34f - 0.16f * warm;
            if (Ambient < 0.1f) Ambient = 0.1f;
        }

        private static int Clamp8(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        public LightSettings Clone()
        {
            return new LightSettings
            {
                Enabled = Enabled, Azimuth = Azimuth, Elevation = Elevation,
                Intensity = Intensity, Ambient = Ambient,
                ColorArgb = ColorArgb, AmbientColorArgb = AmbientColorArgb,
                Specular = Specular, Shadows = Shadows,
                ShadowStrength = ShadowStrength, ShowSun = ShowSun
            };
        }
    }
}
