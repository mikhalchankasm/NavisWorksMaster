using System;

namespace NavisHelper.Agent.Contracts
{
    public static class RedlineTypeWhitelistHelper
    {
        public const string Line = "RedlineLine";
        public const string Ellipse = "RedlineEllipse";

        public static bool IsSupportedForSetRedlines(string type)
        {
            return string.Equals(type, Line, StringComparison.Ordinal) ||
                string.Equals(type, Ellipse, StringComparison.Ordinal);
        }
    }
}
