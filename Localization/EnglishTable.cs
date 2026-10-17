using System.Collections.Generic;

namespace Aplat
{
    // Russian source text → English. The key is the exact text used in the code.
    internal static partial class EnglishTable
    {
        internal static Dictionary<string, string> Build()
        {
            var t = new Dictionary<string, string>();
            Fill(t);
            return t;
        }

        static partial void Fill(Dictionary<string, string> t);
    }
}
