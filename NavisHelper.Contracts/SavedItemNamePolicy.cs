using System.Text;
using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class SavedItemNamePolicy
    {
        public const int DefaultMaxLength = 120;

        public static string Normalize(string value, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (!char.IsControl(ch))
                    builder.Append(ch);
            }

            var name = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            if (name.Length <= DefaultMaxLength)
                return name;

            var safeLength = DefaultMaxLength;
            if (safeLength < name.Length &&
                char.IsHighSurrogate(name[safeLength - 1]) &&
                char.IsLowSurrogate(name[safeLength]))
            {
                safeLength--;
            }

            return name.Substring(0, safeLength).Trim();
        }

        public static string MakeUnique(
            string value,
            string fallback,
            Func<string, bool> nameExists,
            Func<string> overflowTokenFactory)
        {
            var baseName = Normalize(value, fallback);
            if (!nameExists(baseName))
                return baseName;

            for (var i = 2; i < 10000; i++)
            {
                var candidate = baseName + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                if (!nameExists(candidate))
                    return candidate;
            }

            return baseName + " " + overflowTokenFactory();
        }
    }
}
