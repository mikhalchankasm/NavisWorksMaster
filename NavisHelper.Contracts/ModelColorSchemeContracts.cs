using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ModelColorSchemeRequest
    {
        public string Operation { get; set; }
        public string Scope { get; set; }
        public bool? Apply { get; set; }
        public int? MaxItems { get; set; }
        public int? CandidateLimit { get; set; }
        public int? MaxPropertiesPerItem { get; set; }
        public bool? IncludeContainers { get; set; }
        public bool? ConfirmLargeApply { get; set; }
        public bool? ClearSelectionAfterApply { get; set; }
        public int? WorkBudgetSeconds { get; set; }
        public string Verbosity { get; set; }
        public List<string> AnalysisCategoryFilters { get; set; } = new List<string>();
        public List<string> AnalysisPropertyFilters { get; set; } = new List<string>();
        public List<ModelColorSchemeRule> Rules { get; set; } = new List<ModelColorSchemeRule>();
    }

    public sealed class ModelColorSchemeRule
    {
        public string Name { get; set; }
        public string ColorHex { get; set; }
        public float? Transparency { get; set; }
        public bool MatchAll { get; set; }
        public List<string> NameContains { get; set; } = new List<string>();
        public List<string> PathContains { get; set; } = new List<string>();
        public List<string> SourceFileContains { get; set; } = new List<string>();
        public List<string> CategoryContains { get; set; } = new List<string>();
        public List<string> PropertyContains { get; set; } = new List<string>();
        public List<string> PropertyValueContains { get; set; } = new List<string>();
    }

    public sealed class ModelColorSchemeResponse
    {
        public string Operation { get; set; }
        public string Scope { get; set; }
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public bool Reset { get; set; }
        public bool HadActiveScheme { get; set; }
        public bool CanReset { get; set; }
        public int TraversedItemCount { get; set; }
        public int EligibleItemCount { get; set; }
        public int MatchedItemCount { get; set; }
        public int ColoredItemCount { get; set; }
        public int UnclassifiedItemCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool AnalysisTruncated { get; set; }
        public bool ClassificationTruncated { get; set; }
        public int AnalyzedItemCount { get; set; }
        public int ClassifiedItemCount { get; set; }
        public int UnprocessedItemCount { get; set; }
        public int ColorVerificationSampleCount { get; set; }
        public int PermanentColorMatchCount { get; set; }
        public int ActiveColorMatchCount { get; set; }
        public int SelectionClearedItemCount { get; set; }
        public bool SelectionRestored { get; set; }
        public int CandidateCount { get; set; }
        public int ReturnedCandidateCount { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<ModelColorSchemeCandidate> Candidates { get; set; } = new List<ModelColorSchemeCandidate>();
        public List<ModelColorSchemeRuleResult> RuleResults { get; set; } = new List<ModelColorSchemeRuleResult>();
    }

    public sealed class ModelColorSchemeCandidate
    {
        public string Kind { get; set; }
        public string Category { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }
        public int Count { get; set; }
        public string SampleItemName { get; set; }
        public string SampleItemPath { get; set; }
        public string SampleSourceFile { get; set; }
    }

    public sealed class ModelColorSchemeRuleResult
    {
        public int RuleIndex { get; set; }
        public string Name { get; set; }
        public string ColorHex { get; set; }
        public float? Transparency { get; set; }
        public int MatchedItemCount { get; set; }
        public string SampleItemName { get; set; }
        public string SampleItemPath { get; set; }
        public string SampleSourceFile { get; set; }
    }

    public sealed class ModelColorSchemeItemFacts
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public bool PropertiesTruncated { get; set; }
        public List<ModelColorSchemePropertyFact> Properties { get; set; } = new List<ModelColorSchemePropertyFact>();
    }

    public sealed class ModelColorSchemePropertyFact
    {
        public string Category { get; set; }
        public string Property { get; set; }
        public string Value { get; set; }
    }

    public static class ModelColorSchemeRuleMatcher
    {
        public static bool HasMatchers(ModelColorSchemeRule rule)
        {
            return rule != null &&
                   (rule.MatchAll ||
                    HasValues(rule.NameContains) ||
                    HasValues(rule.PathContains) ||
                    HasValues(rule.SourceFileContains) ||
                    HasValues(rule.CategoryContains) ||
                    HasValues(rule.PropertyContains) ||
                    HasValues(rule.PropertyValueContains));
        }

        public static bool Matches(ModelColorSchemeRule rule, ModelColorSchemeItemFacts item)
        {
            if (!HasMatchers(rule) || item == null)
                return false;

            return MatchesPrepared(rule, item);
        }

        public static bool MatchesPrepared(ModelColorSchemeRule rule, ModelColorSchemeItemFacts item)
        {
            if (rule == null || item == null)
                return false;

            if (rule.MatchAll)
                return true;

            if (!MatchesContains(item.Name, rule.NameContains) ||
                !MatchesContains(item.Path, rule.PathContains) ||
                !MatchesContains(item.SourceFile, rule.SourceFileContains))
            {
                return false;
            }

            var hasPropertyMatcher =
                HasValues(rule.CategoryContains) ||
                HasValues(rule.PropertyContains) ||
                HasValues(rule.PropertyValueContains);
            if (!hasPropertyMatcher)
                return true;

            return (item.Properties ?? new List<ModelColorSchemePropertyFact>())
                .Any(property =>
                    property != null &&
                    MatchesContains(property.Category, rule.CategoryContains) &&
                    MatchesContains(property.Property, rule.PropertyContains) &&
                    MatchesContains(property.Value, rule.PropertyValueContains));
        }

        public static List<string> NormalizeValues(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ModelColorSchemeRule NormalizeRule(ModelColorSchemeRule rule)
        {
            rule = rule ?? new ModelColorSchemeRule();
            return new ModelColorSchemeRule
            {
                Name = rule.Name,
                ColorHex = rule.ColorHex,
                Transparency = rule.Transparency,
                MatchAll = rule.MatchAll,
                NameContains = NormalizeValues(rule.NameContains),
                PathContains = NormalizeValues(rule.PathContains),
                SourceFileContains = NormalizeValues(rule.SourceFileContains),
                CategoryContains = NormalizeValues(rule.CategoryContains),
                PropertyContains = NormalizeValues(rule.PropertyContains),
                PropertyValueContains = NormalizeValues(rule.PropertyValueContains),
            };
        }

        private static bool MatchesContains(string value, IEnumerable<string> filters)
        {
            if (!HasValues(filters))
                return true;

            var text = value ?? string.Empty;
            return filters.Any(filter =>
                !string.IsNullOrWhiteSpace(filter) &&
                text.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasValues(IEnumerable<string> values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}
