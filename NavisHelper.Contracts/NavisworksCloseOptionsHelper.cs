using System;

namespace NavisHelper.Agent.Contracts
{
    public static class NavisworksCloseOptionsHelper
    {
        public static string NormalizeMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized))
                return NavisworksCloseModes.Prompt;
            if (normalized == NavisworksCloseModes.Prompt ||
                normalized == NavisworksCloseModes.Save ||
                normalized == NavisworksCloseModes.Discard)
                return normalized;

            throw new ArgumentException("mode must be prompt, save, or discard.");
        }

        public static void ValidateAuthorization(bool apply, bool confirmClose)
        {
            if (apply && !confirmClose)
            {
                throw new ArgumentException(
                    "Closing Navisworks requires confirmClose=true together with apply=true.");
            }
        }
    }
}
