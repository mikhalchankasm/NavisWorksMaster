using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashNativeIgnoreRuleService
    {
        public static void Apply(Document document, ClashTest test, ClashNativeIgnoreRules options)
        {
            if (options == null || options.SameFile != true)
                return;

            LcClClashRules.LoadBaseRuleSet(test.IgnoreRules, document.State);
            var candidates = test.IgnoreRules.Cast<Rule>().ToList();
            var sameFile = candidates.FirstOrDefault(rule => IsSameFileRule(rule.DisplayName));
            if (sameFile == null)
            {
                var names = string.Join(", ", candidates.Select(rule => rule.DisplayName).Where(name => !string.IsNullOrWhiteSpace(name)));
                throw new AgentCommandException(ErrorCodes.CommandFailed,
                    "The native Clash Detective same-file ignore rule is unavailable. Loaded rules: " + names);
            }

            foreach (var rule in candidates)
                rule.IsEnabled = ReferenceEquals(rule, sameFile) || Rule.ValueEquals(rule, sameFile);
        }

        internal static bool IsSameFileRule(string displayName)
        {
            var value = (displayName ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length == 0)
                return false;
            var same = value.Contains("same") || value.Contains("одн") || value.Contains("одинаков");
            var file = value.Contains("file") || value.Contains("model") || value.Contains("файл") || value.Contains("модел");
            return same && file;
        }

        public static bool IsApplied(ClashTest test)
        {
            return test != null && test.IgnoreRules.Cast<Rule>().Any(rule => rule.IsEnabled && IsSameFileRule(rule.DisplayName));
        }
    }
}
