using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Aplat
{
    public enum Lang { Russian, English }

    // Interface language. The source text of every message is Russian and doubles as its key:
    // Loc.T("<Russian text>") returns that text as it is, or its English counterpart from
    // Localization/English.cs. Text that is not in the table falls back to Russian and is
    // remembered in Missed, so tests can prove that nothing was forgotten.
    // The language is chosen once at start-up (language.txt in %AppData%\Aplat, else the Windows
    // display language) and applies after a restart, because windows are built with their text.
    public static class Loc
    {
        private static Dictionary<string, string> _en;
        private static readonly HashSet<string> _missed = new HashSet<string>();
        private static readonly Regex Cyrillic = new Regex("[А-Яа-яЁё]");

        public static Lang Current { get; private set; }
        public static bool IsEnglish { get { return Current == Lang.English; } }

        private static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Aplat", "language.txt");
            }
        }

        // Called once, before any window exists.
        public static void Init()
        {
            Set(Load());
        }

        public static Lang Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string raw = File.ReadAllText(FilePath).Trim().ToLowerInvariant();
                    if (raw == "en" || raw == "english") return Lang.English;
                    if (raw == "ru" || raw == "russian") return Lang.Russian;
                }
            }
            catch { }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? Lang.Russian : Lang.English;
        }

        public static void Save(Lang lang)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, lang == Lang.English ? "en" : "ru");
            }
            catch { }
        }

        public static void Set(Lang lang)
        {
            Current = lang;
            if (lang == Lang.English && _en == null) _en = EnglishTable.Build();
        }

        // The text in the current language.
        public static string T(string ru)
        {
            if (Current != Lang.English || string.IsNullOrEmpty(ru)) return ru;
            string en;
            if (_en.TryGetValue(ru, out en)) return en;
            if (Cyrillic.IsMatch(ru)) lock (_missed) _missed.Add(ru);
            return ru;
        }

        // Text with {0}-style placeholders.
        public static string F(string ru, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(ru), args);
        }

        // Text objects are named "<Russian prefix> <words>" / "Text: <words>"; the name is data saved in the
        // scene, so either prefix is recognised whichever language is on.
        public static string TextPrefix { get { return T("Текст: "); } }
        public static string TextFromName(string name)
        {
            if (name == null) return "";
            foreach (string p in new[] { "Текст: ", "Text: " })
                if (name.StartsWith(p, StringComparison.Ordinal)) return name.Substring(p.Length);
            return "";
        }

        // Russian texts that were asked for in English but have no translation (for tests).
        public static string[] Missed { get { lock (_missed) { var a = new string[_missed.Count]; _missed.CopyTo(a); return a; } } }
        public static bool HasCyrillic(string s) { return !string.IsNullOrEmpty(s) && Cyrillic.IsMatch(s); }
        public static Dictionary<string, string> EnglishEntries { get { if (_en == null) _en = EnglishTable.Build(); return _en; } }
    }
}
