using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Aplat.UI;

// Unit tests of the command palette's matching logic (UI/CommandMatcher.cs): normalisation,
// wrong keyboard layout, ranking, typo tolerance and the "did you mean" decision. Pure logic,
// no window is opened.
class MatchTests
{
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    static MatchItem I(string title, string aliases = null, string extra = null) { return new MatchItem(title, aliases, extra); }

    static readonly List<MatchItem> Items = new List<MatchItem>
    {
        I("Кисть", "Brush", "Инструменты 2D B"),                                     // 0
        I("Карандаш", "Pencil", "Инструменты 2D"),                                   // 1
        I("Ластик", "Eraser; стереть", "Инструменты 2D E"),                           // 2
        I("Ластик объектами", "Object eraser", "Инструменты 2D"),                    // 3
        I("Прямоугольник", "Rectangle", "Инструменты 2D"),                           // 4
        I("Показывать FPS", "Show FPS; частота кадров; fps", "Вид"),                 // 5
        I("Тени", "Shadows; shadow", "Вид"),                                         // 6
        I("О программе", "About", "Справка"),                                        // 7
        I("Сохранить", "Save", "Файл Ctrl+S"),                                       // 8
        I("Сохранить как", "Save as", "Файл Ctrl+Shift+S"),                          // 9
        I("Объём", "Volume", "Вид"),                                                 // 10
        I("Ёмкость", "Capacity", "Вид"),                                             // 11
        I("Автовращение", "Auto-rotate; spin", "Вид › Автовращение F9"),             // 12
        I("Показать всю сцену", "Show whole scene; fit", "Вид Home"),                // 13
        I("Светлая тема", "Light theme", "Вид › Тема"),                              // 14
        I("Дублировать", "Duplicate", "Правка Ctrl+D"),                              // 15
        I("Brush size", "Размер кисти", "Инструменты"),                              // 16
    };

    static MatchResult S(string q) { return CommandMatcher.Search(q, Items); }
    static int First(MatchResult r) { return r.Hits.Count > 0 ? r.Hits[0] : -1; }
    static string Names(IEnumerable<int> idx) { return string.Join(" | ", idx.Select(i => Items[i].Title)); }

    static void Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        // ── normalisation ─────────────────────────────────────────
        Check(CommandMatcher.Normalize("  ЁЛКА,  Ёж ") == "елка еж", "Normalize: case, ё→е, punctuation and spaces");
        Check(CommandMatcher.Normalize("Ctrl+Shift+S") == "ctrl+shift+s", "Normalize keeps '+' of shortcuts");
        Check(CommandMatcher.Normalize(null) == "" && CommandMatcher.Normalize("—…") == "", "Normalize: null / only punctuation");

        // ── distance ──────────────────────────────────────────────
        Check(CommandMatcher.Distance("kitten", "sitting") == 3, "Distance kitten/sitting = 3");
        Check(CommandMatcher.Distance("кисть", "кисьт") == 1, "Distance: a swap of two letters costs 1");
        Check(CommandMatcher.Distance("кисть", "кист") == 1 && CommandMatcher.Distance("кисть", "киссть") == 1, "Distance: missing / extra letter costs 1");
        Check(CommandMatcher.Distance("", "abc") == 3 && CommandMatcher.Distance("abc", "abc") == 0, "Distance: empty and equal");
        Check(CommandMatcher.Trigrams("кисть", "кисть") > 0.99 && CommandMatcher.Trigrams("кисть", "zzzzz") < 0.1, "Trigrams: equal ≈ 1, unrelated ≈ 0");

        // ── keyboard layout ───────────────────────────────────────
        Check(CommandMatcher.SwapLayout("rbcnm") == "кисть", "SwapLayout: rbcnm → кисть");
        Check(CommandMatcher.SwapLayout("кисть") == "rbcnm", "SwapLayout: кисть → rbcnm");
        Check(CommandMatcher.SwapLayout("GHBDTN") == "привет", "SwapLayout: upper case");
        Check(CommandMatcher.SwapLayout("ыфму") == "save", "SwapLayout: ыфму → save");

        // ── ranking: exact > prefix > word start > substring > other fields ──
        var r = S("ластик");
        Check(First(r) == 2 && r.Hits.Contains(3) && r.Hits.IndexOf(2) < r.Hits.IndexOf(3), "Ranking: exact title before a longer title that starts with it: " + Names(r.Hits));
        r = S("сохр");
        Check(r.Hits.Take(2).SequenceEqual(new[] { 8, 9 }), "Ranking: prefix matches in menu order: " + Names(r.Hits));
        r = S("объект");
        Check(First(r) == 3, "Ranking: word-start in the title ('Ластик объектами'): " + Names(r.Hits));
        r = S("кад");
        Check(r.Hits.Contains(5), "A word of an alias is found: " + Names(r.Hits));
        r = S("ctrl+s");
        Check(r.Hits.Contains(8) && r.Hits.Contains(9), "Shortcut text is searchable: " + Names(r.Hits));
        r = S("ctrl s");
        Check(r.Hits.Contains(8), "Shortcut typed with a space: " + Names(r.Hits));
        r = S("вид");
        Check(r.Hits.Count >= 5 && !r.Hits.Contains(0), "Group name finds its commands: " + Names(r.Hits));
        r = S("показ");
        Check(r.Hits.Take(2).All(i => i == 5 || i == 13), "Word start in the title ranks first: " + Names(r.Hits));
        r = S("кисть");
        Check(First(r) == 0, "Exact title ranks first: " + Names(r.Hits));

        // ── case, ё/е, other names ────────────────────────────────
        Check(First(S("КИСТЬ")) == 0, "Case-insensitive");
        Check(First(S("объем")) == 10 && First(S("ЕМКОСТЬ")) == 11, "ё/е equivalence both ways");
        Check(First(S("fps")) == 5, "Alias: 'fps' → Показывать FPS");
        Check(First(S("частота кадров")) == 5, "Alias: 'частота кадров' → Показывать FPS");
        Check(First(S("shadow")) == 6 && First(S("тени")) == 6, "Alias: shadow / тени → Тени");
        Check(First(S("about")) == 7 && First(S("о программе")) == 7, "Alias: about / о программе");
        Check(First(S("pencil")) == 1 && S("brush").Hits.Contains(0), "English name finds the Russian command");
        Check(First(S("ластик объект")) == 3, "Several words: all must match");

        // ── wrong keyboard layout ─────────────────────────────────
        r = S("rbcnm");
        Check(First(r) == 0 && r.LayoutSwapped && r.Effective == "кисть", "Wrong layout: rbcnm → Кисть, flagged");
        r = S("ыфму");
        Check(First(r) == 8 && r.LayoutSwapped, "Wrong layout: ыфму → Save, flagged");
        r = S("nty");
        Check(First(r) == 6 && r.LayoutSwapped, "Wrong layout: nty → Тени");
        r = S("кисть");
        Check(!r.LayoutSwapped, "A correct query is never swapped");
        r = S("fps");
        Check(!r.LayoutSwapped && First(r) == 5, "'fps' stays as typed although its swap exists");

        // ── typo tolerance and the "did you mean" decision ────────
        r = S("кисьт");
        Check(r.DidYouMean && r.Suggestions[0] == 0 && r.Hits.Count == 0, "Swapped letters: кисьт → suggests Кисть: " + Names(r.Suggestions));
        r = S("лстик");
        Check(r.DidYouMean && r.Suggestions[0] == 2, "Missing letter: лстик → Ластик: " + Names(r.Suggestions));
        r = S("ллластик");
        Check(r.DidYouMean && r.Suggestions.Contains(2), "Extra letters: ллластик → Ластик: " + Names(r.Suggestions));
        r = S("прямоугльник");
        Check(r.DidYouMean && r.Suggestions[0] == 4, "Long word, one missing letter → Прямоугольник");
        r = S("праямоугольнек");
        Check(r.DidYouMean && r.Suggestions[0] == 4, "Long word, two slips → Прямоугольник: " + Names(r.Suggestions));
        r = S("shdows");
        Check(r.DidYouMean && r.Suggestions[0] == 6, "English typo: shdows → Тени (Shadows)");
        r = S("pensil");
        Check(r.DidYouMean && r.Suggestions[0] == 1, "English typo: pensil → Карандаш (Pencil)");
        r = S("сохрнить");
        Check(r.Hits.Count == 0 && r.DidYouMean && r.Suggestions[0] == 8, "Typo in a word that has a prefix: сохрнить → Сохранить: " + Names(r.Suggestions));
        r = S("кисьт 2d");
        Check(r.DidYouMean && r.Suggestions[0] == 0, "Typo in one of several words: " + Names(r.Suggestions));
        r = S("zzzzqx");
        Check(r.Hits.Count == 0 && !r.DidYouMean, "Garbage: no hits and no suggestion");
        r = S("zq");
        Check(r.Hits.Count == 0 && !r.DidYouMean, "Two letters are not corrected");
        r = S("кисть");
        Check(!r.DidYouMean, "A good match never asks 'did you mean'");
        r = CommandMatcher.Search("", Items);
        Check(r.Hits.Count == 0 && !r.DidYouMean, "Empty query: nothing to rank (the caller shows the full list)");
        r = CommandMatcher.Search("кисьт", Items, 1);
        Check(r.Suggestions.Count <= 1, "The number of suggestions is capped");

        // ── boosts: recent first, unavailable last ────────────────
        var boosted = new List<MatchItem> { I("Сохранить"), I("Сохранить как"), I("Сохранить копию") };
        boosted[2].Boost = 20;
        var br = CommandMatcher.Search("сохр", boosted);
        Check(br.Hits[0] == 2, "A boost lifts a recently used command among equals");
        boosted[2].Boost = -8;
        br = CommandMatcher.Search("сохр", boosted);
        Check(br.Hits.Last() == 2, "A negative boost (unavailable) sinks it among equals");

        // ── speed: a few hundred commands stay instant ────────────
        var big = new List<MatchItem>();
        var rnd = new Random(7);
        string alphabet = "абвгдежзиклмнопрстуфхцчшэюя";
        for (int i = 0; i < 600; i++)
        {
            var t = new List<string>();
            for (int w = 0; w < 1 + i % 3; w++)
            {
                var sb = new System.Text.StringBuilder();
                for (int c = 0; c < 4 + rnd.Next(7); c++) sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                t.Add(sb.ToString());
            }
            big.Add(I(string.Join(" ", t), "alias " + i + "; synonym word", "Группа › Подгруппа Ctrl+" + i));
        }
        string[] queries = { "кисьт", "rbcnm", "лластик", "zzzzqx", "сохр", "вид", "абвгдежз", "праямоугольнек" };
        var sw = Stopwatch.StartNew();
        foreach (var q in queries) for (int k = 0; k < 5; k++) CommandMatcher.Search(q, big);
        sw.Stop();
        double per = sw.Elapsed.TotalMilliseconds / (queries.Length * 5);
        Check(per < 25, string.Format("600 commands: {0:0.0} ms per query (worst case, typos included)", per));

        Console.WriteLine(checks + " checks, " + fails + " failed");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
