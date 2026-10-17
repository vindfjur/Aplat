using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Aplat
{
    // Asks GitHub whether a newer release exists. It only reads one small JSON answer over HTTPS and never
    // downloads or runs anything: when a newer version is out the user is sent to the release page.
    internal static class UpdateCheck
    {
        internal const string Api = "https://api.github.com/repos/vindfjur/Aplat/releases/latest";
        internal const string ReleasePage = "https://github.com/vindfjur/Aplat/releases/latest";
        private const int MaxBytes = 256 * 1024;

        internal sealed class Result
        {
            internal bool Ok;
            internal Version Latest;
            internal string Tag;
            internal bool Newer;
            internal bool NoReleases;   // GitHub answered, but nothing is published yet (HTTP 404)
        }

        internal static Version Current
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0); }
        }

        // The tag of the release, "v1.2.3" or "1.2.3", from the API answer. Anything else is "no answer".
        internal static Version ParseTag(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d{1,5}(?:\\.\\d{1,5}){1,3})\"");
            Version v;
            return m.Success && Version.TryParse(m.Groups[1].Value, out v) ? v : null;
        }

        internal static Result Evaluate(string json, Version current)
        {
            var latest = ParseTag(json);
            if (latest == null) return new Result();
            return new Result { Ok = true, Latest = latest, Tag = latest.ToString(), Newer = latest > current };
        }

        // Blocking: call it from a worker thread. Never throws; failure is Ok == false.
        internal static Result Fetch()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 (older Windows has it off by default)
                var req = (HttpWebRequest)WebRequest.Create(Api);
                req.UserAgent = "Aplat/" + Current;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000; req.ReadWriteTimeout = 8000;
                req.AllowAutoRedirect = false;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    var buf = new byte[MaxBytes]; int n = 0, r;
                    while (n < MaxBytes && (r = s.Read(buf, n, MaxBytes - n)) > 0) n += r;
                    return Evaluate(Encoding.UTF8.GetString(buf, 0, n), Current);
                }
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                return new Result { NoReleases = resp != null && resp.StatusCode == HttpStatusCode.NotFound };
            }
            catch { return new Result(); }
        }
    }
}
