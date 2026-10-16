using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sparring
{
    /// <summary>
    /// The text of a self-test: a top line that says how it went, then what failed, what needs a
    /// look, what passed and what is only for information, each in the order checked and named by
    /// its area. Nothing checked never reads as success.
    /// </summary>
    internal static class SelfTestReport
    {
        /// <summary>The outcome of one check; <see cref="Info"/> notes something that is neither good nor bad.</summary>
        internal enum Result { Pass, Info, Warn, Fail }

        internal sealed class Check
        {
            public Check(string area, string what, Result result, string detail)
            {
                Area = area;
                What = what;
                Outcome = result;
                Detail = detail;
            }

            public string Area { get; }

            public string What { get; }

            public Result Outcome { get; }

            public string Detail { get; }
        }

        private static readonly Result[] Order = { Result.Fail, Result.Warn, Result.Pass, Result.Info };

        /// <param name="checks">The checks in the order they ran.</param>
        public static List<string> Lines(List<Check> checks)
        {
            var lines = new List<string> { "Self-test of Sparring: " + Summary(checks) };
            foreach (var result in Order)
                foreach (var c in checks.Where(c => c.Outcome == result))
                    lines.Add(Mark(result) + c.Area + ": " + c.What + (string.IsNullOrEmpty(c.Detail) ? "" : " - " + c.Detail));
            return lines;
        }

        private static string Summary(List<Check> checks)
        {
            if (checks.Count == 0) return "nothing was checked";
            int fail = checks.Count(c => c.Outcome == Result.Fail), warn = checks.Count(c => c.Outcome == Result.Warn);
            int pass = checks.Count(c => c.Outcome == Result.Pass), info = checks.Count(c => c.Outcome == Result.Info);
            string infoPart = info > 0 ? ", " + Count(info) + " for information" : "";
            if (fail == 0 && warn == 0) return "all " + Count(pass) + " passed" + infoPart;
            var parts = new List<string>();
            if (fail > 0) parts.Add(Count(fail) + " failed");
            if (warn > 0) parts.Add(Count(warn) + (warn == 1 ? " warning" : " warnings"));
            if (pass > 0) parts.Add(Count(pass) + " passed");
            return string.Join(", ", parts) + infoPart;
        }

        private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

        private static string Mark(Result result)
        {
            switch (result)
            {
                case Result.Fail: return "FAIL  ";
                case Result.Warn: return "WARN  ";
                case Result.Pass: return "ok    ";
                default: return "info  ";
            }
        }
    }
}
