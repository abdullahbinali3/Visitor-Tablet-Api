using System.Text.RegularExpressions;

namespace VisitorTabletAPITemplate.Utilities
{
    public static partial class GeneratedRegexes
    {
        [GeneratedRegex(@"\s+")]
        public static partial Regex ContainsWhitespace();

        [GeneratedRegex(@"\D+")]
        public static partial Regex ContainsNonDigits();

        [GeneratedRegex(@"[^\u0020-\u007F]")]
        public static partial Regex NonPrintableAscii();
    }
}
