using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Determines whether property IDs obtained from a live Navisworks item can be persisted as strings.
    /// </summary>
    public static class SearchSetRuntimeBindingHelper
    {
        public static bool IsRoundTrippableInternalName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf('\uFFFD') < 0;
        }
    }
}
