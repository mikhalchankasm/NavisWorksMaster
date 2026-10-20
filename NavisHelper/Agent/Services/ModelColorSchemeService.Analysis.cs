using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class ModelColorSchemeService
    {
        private void Analyze(
            List<ModelItem> items,
            ModelColorSchemeRequest request,
            int maxPropertiesPerItem,
            int candidateLimit,
            int workBudgetSeconds,
            string verbosity,
            Stopwatch operationTimer,
            PhaseTimings phases,
            ModelColorSchemeResponse response)
        {
            var categoryFilters = ModelColorSchemeRuleMatcher.NormalizeValues(request.AnalysisCategoryFilters);
            var propertyFilters = ModelColorSchemeRuleMatcher.NormalizeValues(request.AnalysisPropertyFilters);
            var candidates = new Dictionary<string, ModelColorSchemeCandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
            var distinctCountByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var candidatesCapped = false;
            var propertyFactsTruncated = false;
            var propertyCache =
                new Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts>();
            var sourceFileCache = new Dictionary<ModelItem, string>();

            foreach (var item in items)
            {
                if (operationTimer.Elapsed.TotalSeconds >= workBudgetSeconds)
                {
                    response.AnalysisTruncated = true;
                    response.Warnings.Add(
                        "Analysis stopped at the host-side work budget before the MCP timeout. Narrow the scope or use analysis filters.");
                    break;
                }

                var facts = BuildItemFacts(
                    item,
                    pathHolder: out var pathHolder,
                    maxPropertiesPerItem: maxPropertiesPerItem,
                    categoryFilters: categoryFilters,
                    propertyFilters: propertyFilters,
                    includeAncestors: true,
                    collectProperties: true,
                    propertyCache: propertyCache,
                    sourceFileCache: sourceFileCache,
                    phases: phases);
                response.AnalyzedItemCount++;
                propertyFactsTruncated |= facts.PropertiesTruncated;
                var candidatesStarted = Stopwatch.GetTimestamp();
                var itemCandidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddCandidate(candidates, distinctCountByKind, itemCandidateKeys, "source_file", string.Empty, string.Empty, facts.SourceFile, facts, pathHolder, ref candidatesCapped);
                AddCandidate(candidates, distinctCountByKind, itemCandidateKeys, "display_name", string.Empty, string.Empty, facts.Name, facts, pathHolder, ref candidatesCapped);
                foreach (var property in facts.Properties)
                {
                    AddCandidate(
                        candidates,
                        distinctCountByKind,
                        itemCandidateKeys,
                        "property_value",
                        property.Category,
                        property.Property,
                        property.Value,
                        facts,
                        pathHolder,
                        ref candidatesCapped);
                }
                phases.Add("candidates", Stopwatch.GetTimestamp() - candidatesStarted);
            }
            var selectStarted = Stopwatch.GetTimestamp();
            if (candidatesCapped)
            {
                response.Warnings.Add(
                    "Analysis candidate accumulation reached " +
                    MaximumAccumulatedCandidateCountPerKind +
                    " distinct values in at least one candidate kind; narrow analysisCategoryFilters/analysisPropertyFilters for a complete property pattern inventory.");
            }
            if (propertyFactsTruncated)
            {
                response.Warnings.Add(
                    "Property facts were truncated for one or more analyzed items. Narrow analysis filters or increase maxPropertiesPerItem.");
            }

            var ordered = candidates.Values
                .OrderByDescending(candidate => candidate.Count)
                .ThenBy(candidate => candidate.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            response.CandidateCount = ordered.Count;
            response.Candidates = SelectCandidatePreview(ordered, candidateLimit)
                .Select(candidate => candidate.ToResponse(verbosity))
                .ToList();
            response.ReturnedCandidateCount = response.Candidates.Count;
            phases.Add("select", Stopwatch.GetTimestamp() - selectStarted);
        }

        private static void AddCandidate(
            Dictionary<string, ModelColorSchemeCandidateAccumulator> candidates,
            Dictionary<string, int> distinctCountByKind,
            HashSet<string> itemCandidateKeys,
            string kind,
            string category,
            string property,
            string value,
            ModelColorSchemeItemFacts facts,
            ModelColorSchemeItemPathHolder pathHolder,
            ref bool candidatesCapped)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalizedValue = value.Trim();
            var key = kind + "\n" + (category ?? string.Empty) + "\n" +
                      (property ?? string.Empty) + "\n" + normalizedValue;
            if (!itemCandidateKeys.Add(key))
                return;

            ModelColorSchemeCandidateAccumulator candidate;
            if (!candidates.TryGetValue(key, out candidate))
            {
                int kindCount;
                distinctCountByKind.TryGetValue(kind, out kindCount);
                if (kindCount >= MaximumAccumulatedCandidateCountPerKind)
                {
                    candidatesCapped = true;
                    return;
                }

                candidate = new ModelColorSchemeCandidateAccumulator
                {
                    Kind = kind,
                    Category = category ?? string.Empty,
                    Property = property ?? string.Empty,
                    Value = normalizedValue,
                    SampleItemName = facts.Name ?? string.Empty,
                    SampleItemPathHolder = pathHolder,
                    SampleSourceFile = facts.SourceFile ?? string.Empty,
                };
                candidates[key] = candidate;
                distinctCountByKind[kind] = kindCount + 1;
            }
            candidate.Count++;
        }

        private static List<ModelColorSchemeCandidateAccumulator> SelectCandidatePreview(
            List<ModelColorSchemeCandidateAccumulator> ordered,
            int limit)
        {
            var result = new List<ModelColorSchemeCandidateAccumulator>();
            var selected = new HashSet<ModelColorSchemeCandidateAccumulator>();
            foreach (var kind in new[] { "source_file", "display_name", "property_value" })
            {
                var candidate = ordered.FirstOrDefault(value =>
                    string.Equals(value.Kind, kind, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && selected.Add(candidate))
                    result.Add(candidate);
                if (result.Count >= limit)
                    return result;
            }

            foreach (var candidate in ordered)
            {
                if (selected.Add(candidate))
                    result.Add(candidate);
                if (result.Count >= limit)
                    break;
            }
            return result;
        }

    }
}
