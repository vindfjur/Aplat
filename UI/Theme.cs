using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Aplat.UI
{
    // Single source of truth for the app's look: colours, radii and the UI font.
    // The whole interface (MainForm.Layout, custom controls, dialogs) reads from
    // here, so a redesign is a one-file change.
    //
    // Colours are tokens of the current ThemePalette (Light or Graphite). They are
    // properties, not snapshots: custom-painted controls pick up a theme switch on
    // their next paint, and ThemeManager re-colours the standard controls.
    //
    // Font: prefers Apple's SF Pro when it is installed (e.g. running the build in a
    // macOS-hosted Windows VM with the system fonts shared in), and quietly falls back
    // to Segoe UI / the default UI font everywhere else. No hard dependency on a font
    // that may be missing.
    public static class Theme
    {
        private static ThemeMode    _mode    = ThemeMode.Light;
        private static ThemePalette _current = ThemePalette.Light;
        private static bool         _watchingSystem;

        public static ThemePalette Current { get { return _current; } }
        public static ThemeMode    Mode    { get { return _mode; } }
        public static bool         IsDark  { get { return _current.IsDark; } }

        // Raised after the palette actually changed (not when only the mode did).
        public static event System.EventHandler<ThemeChangedEventArgs> Changed;

        public static void SetMode(ThemeMode mode)
        {
            _mode = mode;
            WatchSystem(mode == ThemeMode.System);
            Activate(Resolve(mode));
        }

        public static ThemePalette Resolve(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Graphite: return ThemePalette.Graphite;
                case ThemeMode.System:   return SystemPrefersDark() ? ThemePalette.Graphite : ThemePalette.Light;
                default:                 return ThemePalette.Light;
            }
        }

        private static void Activate(ThemePalette next)
        {
            if (ReferenceEquals(next, _current)) return;
            var previous = _current;
            _current = next;
            var h = Changed;
            if (h != null) h(null, new ThemeChangedEventArgs(previous, next));
        }

        // Windows: HKCU\...\Themes\Personalize\AppsUseLightTheme = 0 means dark apps.
        // Anywhere else (or on any registry error) the answer is "light".
        private static bool SystemPrefersDark()
        {
            if (System.Environment.OSVersion.Platform != System.PlatformID.Win32NT) return false;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        private static void WatchSystem(bool on)
        {
            if (on == _watchingSystem) return;
            _watchingSystem = on;
            if (on) Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            else    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private static void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (_mode == ThemeMode.System && e.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Activate(Resolve(ThemeMode.System));
        }

        // ── Tokens (see ThemePalette for the meaning of each) ─────
        public static Color Bg             { get { return _current.Bg; } }
        public static Color Surface        { get { return _current.Surface; } }
        public static Color Card           { get { return _current.Card; } }
        public static Color Input          { get { return _current.Input; } }
        public static Color Border         { get { return _current.Border; } }
        public static Color BorderSoft     { get { return _current.BorderSoft; } }
        public static Color Text           { get { return _current.Text; } }
        public static Color TextSoft       { get { return _current.TextSoft; } }
        public static Color TextMuted      { get { return _current.TextMuted; } }
        public static Color TextDisabled   { get { return _current.TextDisabled; } }
        public static Color Accent         { get { return _current.Accent; } }
        public static Color AccentHover    { get { return _current.AccentHover; } }
        public static Color AccentLit      { get { return _current.AccentLit; } }
        public static Color AccentSoft     { get { return _current.AccentSoft; } }
        public static Color AccentText     { get { return _current.AccentText; } }
        public static Color BtnFace        { get { return _current.BtnFace; } }
        public static Color BtnHover       { get { return _current.BtnHover; } }
        public static Color BtnDown        { get { return _current.BtnDown; } }
        public static Color SliderTrack    { get { return _current.SliderTrack; } }
        public static Color SliderKnob     { get { return _current.SliderKnob; } }
        public static Color Shadow         { get { return _current.Shadow; } }
        public static Color SceneBackground { get { return _current.SceneBackground; } }

        // Selection frames on the canvas (marquee): the accent line over a white
        // under-stroke, so the frame reads on any paint colour.
        public static Color Selection      { get { return _current.Accent; } }
        public static Color SelectionUnder { get { return _current.SelectionUnder; } }

        // Corner radii: all zero — the design is strictly sharp (square corners).
        // Kept as named constants so the whole UI stays a one-file change if a future
        // iteration wants a little rounding back.
        public const int RadiusTile   = 0;
        public const int RadiusButton = 0;
        public const int RadiusCard   = 0;

        // ── Font resolution (resolved once, lazily) ───────────────
        private static string _family;

        public static string FontFamily
        {
            get
            {
                if (_family == null) _family = ResolveFamily();
                return _family;
            }
        }

        private static string ResolveFamily()
        {
            // First installed wins; ordered most-preferred → fallback.
            string[] wanted = { "SF Pro Display", "SF Pro Text", "SF Pro", "Segoe UI", "Helvetica Neue" };
            using (var installed = new InstalledFontCollection())
            {
                var have = installed.Families;
                foreach (var name in wanted)
                    foreach (var fam in have)
                        if (string.Equals(fam.Name, name, System.StringComparison.OrdinalIgnoreCase))
                            return fam.Name;
            }
            return "Segoe UI"; // safe default; GDI substitutes if even this is absent
        }

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(FontFamily, size, style);
        }


        // ── Shared drawing helpers ────────────────────────────────

        // Legible text colour on an arbitrary fill (a brush or material colour):
        // decided by perceived luminance (Rec. 601), not HSL brightness — pure red
        // has HSL brightness 0.5 but reads as a dark colour.
        public static Color ContrastText(Color fill)
        {
            double lum = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255.0;
            return lum < 0.6 ? Color.White : Color.FromArgb(15, 23, 42);
        }

        // A slightly lighter / darker shade of a colour (delta in 0..255 steps),
        // used to build subtle vertical gradients on otherwise-flat surfaces.
        public static Color Shade(Color c, int delta)
        {
            int r = c.R + delta, g = c.G + delta, b = c.B + delta;
            return Color.FromArgb(c.A,
                r < 0 ? 0 : r > 255 ? 255 : r,
                g < 0 ? 0 : g > 255 ? 255 : g,
                b < 0 ? 0 : b > 255 ? 255 : b);
        }

        // Vertical (top→bottom) gradient brush over a rectangle. Caller disposes.
        public static LinearGradientBrush VGradient(Rectangle r, Color top, Color bottom)
        {
            // Guard against a zero-height rect (LinearGradientBrush throws on it) and
            // inflate by 1px so the brush's tiling never leaves a stray line at the
            // top edge (a well-known GDI+ LinearGradientBrush artifact).
            if (r.Height < 1) r.Height = 1;
            var gr = new Rectangle(r.X, r.Y - 1, System.Math.Max(1, r.Width), r.Height + 2);
            return new LinearGradientBrush(gr, top, bottom, LinearGradientMode.Vertical);
        }

        // A rounded-rectangle path; collapses to a plain rectangle when radius <= 0.
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0) { path.AddRectangle(r); return path; }

            int d = radius * 2;
            if (d > r.Width)  d = r.Width;
            if (d > r.Height) d = r.Height;

            path.AddArc(r.X,             r.Y,              d, d, 180, 90);
            path.AddArc(r.Right - d,     r.Y,              d, d, 270, 90);
            path.AddArc(r.Right - d,     r.Bottom - d,     d, d,   0, 90);
            path.AddArc(r.X,             r.Bottom - d,     d, d,  90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

namespace Aplat.UI
{
    public sealed class ThemeChangedEventArgs : System.EventArgs
    {
        public ThemeChangedEventArgs(ThemePalette previous, ThemePalette current)
        {
            Previous = previous;
            Current  = current;
        }

        public ThemePalette Previous { get; private set; }
        public ThemePalette Current  { get; private set; }
    }
}
