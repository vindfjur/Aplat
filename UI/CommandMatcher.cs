using System;
using System.Collections.Generic;
using System.Text;

namespace Aplat.UI
{
    // One searchable entry. Everything is normalised once here, so a search over a few
    // hundred entries only compares short prepared strings.
    public sealed class MatchItem
    {
        public readonly string Title;
        public int Boost;   // added to the score: recently used commands up, unavailable ones down

        internal readonly string NTitle;
        internal readonly string[] TitleWords;
        internal readonly string[] AliasPhrases;   // other-language name, synonyms
        internal readonly string[] AliasWords;
        internal readonly string NExtra;           // group, hint, shortcut
        internal readonly string[] ExtraWords;

        // aliases: phrases separated by ';' or '|' (the same command under other names,
        // e.g. the title in the other language and synonyms); extra: free text of lower weight.
        public MatchItem(string title, string aliases = null, string extra = null)
        {
            Title = title ?? "";
            NTitle = CommandMatcher.Normalize(Title);
            TitleWords = CommandMatcher.Words(NTitle);

            var phrases = new List<string>();
            var words = new List<string>();
            if (!string.IsNullOrEmpty(aliases))
                foreach (string raw in aliases.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = CommandMatcher.Normalize(raw);
                    if (p.Length == 0) continue;
                    phrases.Add(p);
                    words.AddRange(CommandMatcher.Words(p));
                }
            AliasPhrases = phrases.ToArray();
            AliasWords = words.ToArray();

            NExtra = CommandMatcher.Normalize(extra);
            ExtraWords = CommandMatcher.Words(NExtra);
        }
    }

    public sealed class MatchResult
    {
        public readonly List<int> Hits = new List<int>();          // indexes into the item list, best first
        public readonly List<int> Suggestions = new List<int>();   // near-misses: "did you mean"
        public bool LayoutSwapped;   // the query was typed in the wrong keyboard layout
        public string Effective;     // the normalised query that produced the result

        public bool DidYouMean { get { return Suggestions.Count > 0; } }
    }

    // The pure matching logic of the command palette (no UI): normalisation (case, ё/е),
    // wrong-layout recovery, ranking and typo tolerance. Ranking, best first:
    //   whole title > title prefix > word start in the title > other names / word start >
    //   substring > group, hint and keys > typo (Damerau–Levenshtein, only as a suggestion).
    public static class CommandMatcher
    {
        // Scores of the tiers. A "good" match is a word start or better.
        const int Exact = 1000, Prefix = 300, AliasExact = 280, AliasPrefix = 240, WordStart = 200,
                  AliasWordStart = 170, Substring = 100, AliasSubstring = 80, ExtraWordStart = 30,
                  ExtraSubstring = 20, GoodFrom = AliasWordStart;

        const string Latin    = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
        const string Cyrillic = "йцукенгшщзхъфывапролджэячсмитьбюё";

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char raw in s)
            {
                char c = char.ToLowerInvariant(raw);
                if (c == 'ё') c = 'е';
                bool keep = char.IsLetterOrDigit(c) || c == '+' || c == '[' || c == ']';
                if (keep) { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.ToString();
        }

        public static string[] Words(string normalized)
        {
            return string.IsNullOrEmpty(normalized)
                ? new string[0]
                : normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Russian letters typed on the English layout ("rbcnm") and the other way round.
        public static string SwapLayout(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char raw in s)
            {
                char ch = char.ToLowerInvariant(raw);
                int i = Latin.IndexOf(ch);
                if (i >= 0) { sb.Append(Cyrillic[i]); continue; }
                i = Cyrillic.IndexOf(ch);
                sb.Append(i >= 0 ? Latin[i] : raw);
            }
            return sb.ToString();
        }

        // Damerau–Levenshtein distance (optimal string alignment): an insertion, a deletion, a
        // substitution or a swap of two neighbouring letters each cost 1.
        public static int Distance(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                }
            return d[n, m];
        }

        // Dice coefficient over letter triples: 1 for equal words, 0 for nothing in common.
        public static double Trigrams(string a, string b)
        {
            var ta = TriSet(a); var tb = TriSet(b);
            if (ta.Count == 0 || tb.Count == 0) return 0;
            int common = 0;
            foreach (string t in ta) if (tb.Contains(t)) common++;
            return 2.0 * common / (ta.Count + tb.Count);
        }

        static HashSet<string> TriSet(string s)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(s)) return set;
            string p = "  " + s + " ";
            for (int i = 0; i + 3 <= p.Length; i++) set.Add(p.Substring(i, 3));
            return set;
        }

        // How many letters may be wrong in a word of this length.
        public static int AllowedErrors(int length)
        {
            if (length <= 2) return 0;
            if (length <= 6) return 1;
            if (length <= 10) return 2;
            return 3;
        }

        sealed class Hit { public int Index, Score; public bool Good, Fuzzy; }

        public static MatchResult Search(string query, IList<MatchItem> items, int maxSuggestions = 4)
        {
            var result = new MatchResult();
            string q = Normalize(query);
            result.Effective = q;
            if (q.Length == 0 || items == null) return result;

            var strict = Collect(q, items, false);
            bool good = strict.Exists(h => h.Good);
            string swapped = Normalize(SwapLayout(query));
            if (swapped == q) swapped = null;

            if (!good && swapped != null)
            {
                var other = Collect(swapped, items, false);
                if (other.Exists(h => h.Good))
                {
                    foreach (var h in other) result.Hits.Add(h.Index);
                    result.LayoutSwapped = true;
                    result.Effective = swapped;
                    return result;
                }
            }

            foreach (var h in strict) result.Hits.Add(h.Index);
            if (good) return result;

            // Nothing matches well: offer the near-misses.
            var fuzzy = Collect(q, items, true);
            string used = q;
            bool viaSwap = false;
            if (fuzzy.Count == 0 && swapped != null)
            {
                fuzzy = Collect(swapped, items, true);
                used = swapped;
                viaSwap = fuzzy.Count > 0;
            }
            var taken = new HashSet<int>(result.Hits);
            foreach (var h in fuzzy)
            {
                if (taken.Contains(h.Index)) continue;
                result.Suggestions.Add(h.Index);
                if (result.Suggestions.Count >= maxSuggestions) break;
            }
            if (viaSwap && result.Suggestions.Count > 0) { result.LayoutSwapped = true; result.Effective = used; }
            return result;
        }

        // Every item the query matches, best first. With typos on, only items that need a
        // typo correction for at least one word are returned (the others are strict hits).
        static List<Hit> Collect(string q, IList<MatchItem> items, bool typos)
        {
            string[] words = Words(q);
            var hits = new List<Hit>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int total = 0;
                bool ok = true, good = true, fuzzy = false;
                foreach (string w in words)
                {
                    bool f;
                    int s = WordScore(w, it, typos, out f);
                    if (s < 0) { ok = false; break; }
                    total += s;
                    if (f) fuzzy = true;
                    if (f || s < GoodFrom) good = false;
                }
                if (!ok || (typos && !fuzzy)) continue;
                if (it.NTitle == q) total += Exact;
                else if (words.Length > 1 && it.NTitle.StartsWith(q, StringComparison.Ordinal)) total += 100;
                total += it.Boost;
                hits.Add(new Hit { Index = i, Score = total, Good = good, Fuzzy = fuzzy });
            }
            // Stable: equal scores keep the menu order.
            var order = new List<Hit>(hits);
            order.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            return order;
        }

        // The best tier one query word reaches in an item, or -1.
        static int WordScore(string w, MatchItem it, bool typos, out bool fuzzy)
        {
            fuzzy = false;
            int best = -1;
            if (it.NTitle == w) best = Exact;
            else if (it.NTitle.StartsWith(w, StringComparison.Ordinal)) best = Prefix;
            else if (StartsAny(it.TitleWords, w)) best = WordStart;
            else if (it.NTitle.IndexOf(w, StringComparison.Ordinal) >= 0) best = Substring;

            if (best < Prefix)
            {
                int a = -1;
                foreach (string p in it.AliasPhrases)
                {
                    if (p == w) { a = Math.Max(a, AliasExact); break; }
                    if (p.StartsWith(w, StringComparison.Ordinal)) a = Math.Max(a, AliasPrefix);
                }
                if (a < 0 && StartsAny(it.AliasWords, w)) a = AliasWordStart;
                if (a < 0)
                    foreach (string p in it.AliasPhrases)
                        if (p.IndexOf(w, StringComparison.Ordinal) >= 0) { a = AliasSubstring; break; }
                best = Math.Max(best, a);
            }
            if (best < 0)
            {
                if (StartsAny(it.ExtraWords, w)) best = ExtraWordStart;
                else if (it.NExtra.IndexOf(w, StringComparison.Ordinal) >= 0) best = ExtraSubstring;
            }
            if (best >= 0 || !typos || w.Length < 3) return best;

            // A typo: compare with the words of the title (and other names), also with only the
            // beginning of a longer word, so a half-typed word with a slip is still understood.
            int max = AllowedErrors(w.Length);
            double bestFit = -1;
            int bestD = int.MaxValue;
            ConsiderTypo(w, it.TitleWords, max, 0, ref bestD, ref bestFit);
            ConsiderTypo(w, it.AliasWords, max, 0, ref bestD, ref bestFit);
            if (bestD > max) return -1;
            fuzzy = true;
            return (int)(60 - 15 * bestD + 10 * bestFit);
        }

        static void ConsiderTypo(string w, string[] words, int max, int penalty, ref int bestD, ref double bestFit)
        {
            foreach (string t in words)
            {
                if (Math.Abs(t.Length - w.Length) > max && t.Length < w.Length) continue;
                int d = Distance(w, t);
                string head = t.Length > w.Length ? t.Substring(0, w.Length) : t;
                if (t.Length > w.Length) d = Math.Min(d, Distance(w, head));
                d += penalty;
                if (d > max) continue;
                double fit = Trigrams(w, head);
                if (d < bestD || (d == bestD && fit > bestFit)) { bestD = d; bestFit = fit; }
            }
        }

        static bool StartsAny(string[] words, string w)
        {
            foreach (string t in words) if (t.StartsWith(w, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
