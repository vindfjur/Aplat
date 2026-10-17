using System.Drawing;

namespace Aplat.UI
{
    public enum ThemeMode
    {
        Light,
        Graphite,
        System      // follows Windows "app mode" (light / dark) live
    }

    // One complete set of colour tokens. Controls never hard-code colours: they ask
    // Theme for a token, and Theme answers from the current palette. The two
    // palettes share every token name, so a control looks right in both by design.
    public sealed class ThemePalette
    {
        public string Name;
        public bool   IsDark;

        // Surfaces, from the window background up to raised cards and input wells.
        public Color Bg, Surface, Card, Input;
        public Color Border, BorderSoft;

        // Text. TextDisabled is for switched-off controls: still readable (≥ 4:1 on
        // the button face in both themes), just clearly secondary. Never use the
        // system GrayText for it — on the dark theme that is grey-on-black.
        public Color Text, TextSoft, TextMuted, TextDisabled;

        // Accent (selection, active tool, primary action).
        public Color Accent, AccentHover, AccentLit, AccentSoft, AccentText;

        // Neutral button states.
        public Color BtnFace, BtnHover, BtnDown;

        // Controls with their own chrome.
        public Color SliderTrack, SliderKnob, Shadow;
        public Color SelectionUnder;

        // Default viewport colour for new scenes (a scene may override it).
        public Color SceneBackground;

        // Fluent Glass, "instrument" refresh: crisp slate surfaces, an electric
        // accent, sharp 1px geometry.
        public static readonly ThemePalette Light = new ThemePalette
        {
            Name            = Loc.T("Светлая"),
            IsDark          = false,
            Bg              = Color.FromArgb(248, 250, 252),   // slate-50
            Surface         = Color.FromArgb(241, 245, 249),   // slate-100
            Card            = Color.FromArgb(255, 255, 255),
            Input           = Color.FromArgb(251, 252, 254),
            Border          = Color.FromArgb(203, 213, 225),   // slate-300
            BorderSoft      = Color.FromArgb(226, 232, 240),   // slate-200
            Text            = Color.FromArgb(15, 23, 42),      // slate-900
            TextSoft        = Color.FromArgb(71, 85, 105),     // slate-600
            TextMuted       = Color.FromArgb(148, 163, 184),   // slate-400
            TextDisabled    = Color.FromArgb(110, 124, 145),
            Accent          = Color.FromArgb(37, 99, 235),     // blue-600
            AccentHover     = Color.FromArgb(29, 78, 216),     // blue-700
            AccentLit       = Color.FromArgb(59, 130, 246),    // blue-500
            AccentSoft      = Color.FromArgb(232, 240, 254),
            AccentText      = Color.FromArgb(255, 255, 255),
            BtnFace         = Color.FromArgb(246, 248, 251),
            BtnHover        = Color.FromArgb(236, 240, 245),
            BtnDown         = Color.FromArgb(225, 231, 239),
            SliderTrack     = Color.FromArgb(221, 227, 235),
            SliderKnob      = Color.FromArgb(255, 255, 255),
            Shadow          = Color.FromArgb(15, 23, 42),
            SelectionUnder  = Color.FromArgb(230, 255, 255, 255),
            SceneBackground = Color.FromArgb(178, 181, 186),
        };

        // Graphite: the dark mode. Neutral, very slightly cool greys (no blue cast),
        // a lifted accent that holds contrast on dark, cards darker than the viewport
        // so the HUD reads as a layer above the scene.
        public static readonly ThemePalette Graphite = new ThemePalette
        {
            Name            = Loc.T("Графит"),
            IsDark          = true,
            Bg              = Color.FromArgb(24, 24, 27),
            Surface         = Color.FromArgb(30, 30, 34),
            Card            = Color.FromArgb(37, 37, 41),
            Input           = Color.FromArgb(29, 29, 33),
            Border          = Color.FromArgb(64, 64, 71),
            BorderSoft      = Color.FromArgb(51, 51, 57),
            Text            = Color.FromArgb(236, 236, 240),
            TextSoft        = Color.FromArgb(170, 170, 180),
            TextMuted       = Color.FromArgb(113, 113, 123),
            TextDisabled    = Color.FromArgb(142, 142, 152),
            Accent          = Color.FromArgb(92, 134, 255),
            AccentHover     = Color.FromArgb(72, 116, 245),
            AccentLit       = Color.FromArgb(118, 154, 255),
            AccentSoft      = Color.FromArgb(42, 52, 82),
            AccentText      = Color.FromArgb(255, 255, 255),
            BtnFace         = Color.FromArgb(45, 45, 50),
            BtnHover        = Color.FromArgb(56, 56, 62),
            BtnDown         = Color.FromArgb(67, 67, 74),
            SliderTrack     = Color.FromArgb(61, 61, 68),
            SliderKnob      = Color.FromArgb(242, 242, 245),
            Shadow          = Color.FromArgb(0, 0, 0),
            SelectionUnder  = Color.FromArgb(230, 255, 255, 255),
            SceneBackground = Color.FromArgb(44, 44, 48),
        };
    }
}
