using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ModelColorSchemeService
    {
        private const int DefaultMaxItems = 100000;
        private const int MaximumMaxItems = 2000000;
        private const int DefaultCandidateLimit = 100;
        private const int MaximumCandidateLimit = 5000;
        private const int DefaultMaxPropertiesPerItem = 50;
        private const int MaximumMaxPropertiesPerItem = 1000;
        private const int LargeApplyThreshold = 25000;
        private const int MaximumRuleCount = 200;
        private const int MaximumAccumulatedCandidateCountPerKind = 50000;
        private const int DefaultWorkBudgetSeconds = 40;
        private const int MaximumWorkBudgetSeconds = 45;

        private ModelColorSchemeDocumentIdentity _sessionIdentity;
        private List<ModelColorSchemeOriginalState> _originalStates;
        private ModelItemCollection _selectionBeforeApply;
        private bool _selectionClearedByScheme;
        private bool _hasActiveScheme;

        public ModelColorSchemeResponse Execute(Document document, ModelColorSchemeRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ModelColorSchemeRequest();
            DiscardStaleSession(document);

            var operation = NormalizeOperation(request.Operation);
            var scope = NormalizeScope(request.Scope);
            var apply = request.Apply == true;
            var response = new ModelColorSchemeResponse
            {
                Operation = operation,
                Scope = scope,
                Apply = apply,
                HadActiveScheme = HasActiveSchemeFor(document),
                CanReset = HasActiveSchemeFor(document),
            };

            if (operation == "reset")
                return Reset(document, response);

            if (operation == "apply" && scope == "selection")
                EnsureSelectionAvailableForScope(document);

            var maxItems = Clamp(request.MaxItems, DefaultMaxItems, 1, MaximumMaxItems);
            var candidateLimit = Clamp(request.CandidateLimit, DefaultCandidateLimit, 1, MaximumCandidateLimit);
            var maxPropertiesPerItem = Clamp(
                request.MaxPropertiesPerItem,
                DefaultMaxPropertiesPerItem,
                1,
                MaximumMaxPropertiesPerItem);
            var workBudgetSeconds = Clamp(
                request.WorkBudgetSeconds,
                DefaultWorkBudgetSeconds,
                5,
                MaximumWorkBudgetSeconds);
            var verbosity = NormalizeVerbosity(request.Verbosity);
            var includeContainers = request.IncludeContainers.GetValueOrDefault(false);
            if (operation == "apply" && includeContainers)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "includeContainers=true is analysis-only because container overrides can propagate to matched descendants.");
            }
            var operationTimer = Stopwatch.StartNew();
            var collected = CollectItems(
                document,
                scope,
                maxItems,
                includeContainers,
                operationTimer,
                workBudgetSeconds);
            response.TraversedItemCount = collected.TraversedItemCount;
            response.EligibleItemCount = collected.Items.Count;
            response.ItemsTruncated = collected.Truncated;
            if (response.ItemsTruncated)
            {
                response.Warnings.Add(
                    collected.WorkBudgetReached
                        ? "Item traversal stopped at the host-side work budget before the requested scope was complete."
                        : "Item traversal reached maxItems before the requested scope was complete.");
            }

            if (operation == "analyze")
            {
                Analyze(
                    collected.Items,
                    request,
                    maxPropertiesPerItem,
                    candidateLimit,
                    workBudgetSeconds,
                    verbosity,
                    operationTimer,
                    response);
                response.Message = response.AnalysisTruncated
                    ? "Model color analysis stopped at the host-side work budget. Narrow the scope or use analysis filters."
                    : response.ItemsTruncated
                    ? "Model color analysis completed on a truncated item scope. Increase maxItems for full coverage."
                    : "Model color analysis completed.";
                return response;
            }

            var preparedRules = PrepareRules(request.Rules);
            var ruleBuckets = preparedRules
                .Select(rule => new ModelColorSchemeRuleBucket { Rule = rule })
                .ToList();
            var propertyRules = preparedRules
                .Where(rule => HasPropertyMatchers(rule.Rule))
                .ToList();
            var collectProperties = propertyRules.Count > 0;
            var categoryFilters = propertyRules.Any(rule => rule.Rule.CategoryContains.Count == 0)
                ? new List<string>()
                : propertyRules
                    .SelectMany(rule => rule.Rule.CategoryContains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            var propertyFilters = propertyRules.Any(rule => rule.Rule.PropertyContains.Count == 0)
                ? new List<string>()
                : propertyRules
                    .SelectMany(rule => rule.Rule.PropertyContains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            var propertyFactsTruncated = false;
            var propertyCache =
                new Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts>();
            var sourceFileCache = new Dictionary<ModelItem, string>();

            for (var itemIndex = 0; itemIndex < collected.Items.Count; itemIndex++)
            {
                if (operationTimer.Elapsed.TotalSeconds >= workBudgetSeconds)
                {
                    response.ClassificationTruncated = true;
                    response.Warnings.Add(
                        "Classification stopped at the host-side work budget before the MCP timeout.");
                    break;
                }

                var item = collected.Items[itemIndex];
                var itemFacts = BuildItemFacts(
                    item,
                    maxPropertiesPerItem,
                    categoryFilters,
                    propertyFilters,
                    includeAncestors: true,
                    collectProperties: collectProperties,
                    propertyCache: propertyCache,
                    sourceFileCache: sourceFileCache);
                propertyFactsTruncated |= itemFacts.PropertiesTruncated;
                response.ClassifiedItemCount++;
                var matched = false;
                for (var ruleIndex = 0; ruleIndex < preparedRules.Count; ruleIndex++)
                {
                    if (!ModelColorSchemeRuleMatcher.MatchesPrepared(preparedRules[ruleIndex].Rule, itemFacts))
                        continue;

                    ruleBuckets[ruleIndex].Items.Add(item);
                    if (string.IsNullOrWhiteSpace(ruleBuckets[ruleIndex].SampleItemName))
                    {
                        ruleBuckets[ruleIndex].SampleItemName = itemFacts.Name;
                        ruleBuckets[ruleIndex].SampleItemPath = itemFacts.Path;
                        ruleBuckets[ruleIndex].SampleSourceFile = itemFacts.SourceFile;
                    }
                    response.MatchedItemCount++;
                    matched = true;
                    break;
                }

                if (!matched)
                    response.UnclassifiedItemCount++;
            }
            if (propertyFactsTruncated)
            {
                response.Warnings.Add(
                    "Property facts were truncated for one or more items. Narrow property matchers or increase maxPropertiesPerItem.");
            }
            if (response.ClassificationTruncated)
            {
                response.UnprocessedItemCount =
                    Math.Max(0, response.EligibleItemCount - response.ClassifiedItemCount);
            }

            response.RuleResults = ruleBuckets
                .Select((bucket, index) => new ModelColorSchemeRuleResult
                {
                    RuleIndex = index + 1,
                    Name = bucket.Rule.Name,
                    ColorHex = bucket.Rule.ColorHex,
                    Transparency = bucket.Rule.Rule.Transparency,
                    MatchedItemCount = bucket.Items.Count,
                    SampleItemName = bucket.SampleItemName ?? string.Empty,
                    SampleItemPath = bucket.SampleItemPath ?? string.Empty,
                    SampleSourceFile = bucket.SampleSourceFile ?? string.Empty,
                })
                .ToList();

            if (!apply)
            {
                response.Message = "Dry-run only. Review rule coverage, then pass apply=true to apply the model color scheme.";
                return response;
            }

            if (response.ItemsTruncated)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "The model color scope was truncated. Increase maxItems before apply=true; partial scheme application is not allowed.",
                    logAsWarning: true);
            }
            if (response.ClassificationTruncated)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "Classification stopped at the host-side work budget. Narrow the scope before apply=true.");
            }
            if (propertyFactsTruncated)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "Property facts were truncated. Narrow property matchers or increase maxPropertiesPerItem before apply=true.");
            }
            if (response.MatchedItemCount > LargeApplyThreshold && request.ConfirmLargeApply != true)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "The scheme matches more than " + LargeApplyThreshold +
                    " items. Review the dry-run and pass confirmLargeApply=true to apply.");
            }

            Apply(
                document,
                ruleBuckets,
                request.ClearSelectionAfterApply.GetValueOrDefault(true),
                response);
            return response;
        }

        public void DiscardForDocumentChange()
        {
            ClearSession();
        }

        public void HandleDocumentFileNameChanged(Document document)
        {
            if (!_hasActiveScheme)
            {
                ClearSession();
                return;
            }

            if (_sessionIdentity != null && _sessionIdentity.HasSameModelContent(document))
            {
                _sessionIdentity = ModelColorSchemeDocumentIdentity.Capture(document);
                return;
            }

            ClearSession();
        }

        private ModelColorSchemeResponse Reset(Document document, ModelColorSchemeResponse response)
        {
            if (!response.Apply)
            {
                response.Message = response.HadActiveScheme
                    ? "Dry-run only. Pass apply=true to restore overrides changed by the active model color scheme."
                    : "There is no active MCP model color scheme in this document.";
                return response;
            }

            if (!response.HadActiveScheme)
            {
                response.Message = "There is no active MCP model color scheme in this document.";
                return response;
            }

            RestoreOriginalStates(document, response.Warnings);
            RestoreSelectionIfSafe(document, response);
            RequestRedraw(document);
            if (response.Warnings.Count > 0)
            {
                response.CanReset = true;
                response.Message =
                    "The model color scheme reset was incomplete. Review warnings and retry while the document remains open.";
                return response;
            }

            response.Reset = true;
            response.ColoredItemCount = _originalStates == null ? 0 : _originalStates.Count;
            ClearSession();
            response.CanReset = false;
            response.Message = "Overrides changed by the active MCP model color scheme were restored.";
            return response;
        }

        private void Analyze(
            List<ModelItem> items,
            ModelColorSchemeRequest request,
            int maxPropertiesPerItem,
            int candidateLimit,
            int workBudgetSeconds,
            string verbosity,
            Stopwatch operationTimer,
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
                    maxPropertiesPerItem,
                    categoryFilters,
                    propertyFilters,
                    includeAncestors: true,
                    collectProperties: true,
                    propertyCache: propertyCache,
                    sourceFileCache: sourceFileCache);
                response.AnalyzedItemCount++;
                propertyFactsTruncated |= facts.PropertiesTruncated;
                var itemCandidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddCandidate(candidates, distinctCountByKind, itemCandidateKeys, "source_file", string.Empty, string.Empty, facts.SourceFile, facts, ref candidatesCapped);
                AddCandidate(candidates, distinctCountByKind, itemCandidateKeys, "display_name", string.Empty, string.Empty, facts.Name, facts, ref candidatesCapped);
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
                        ref candidatesCapped);
                }
            }
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
        }

        private void Apply(
            Document document,
            List<ModelColorSchemeRuleBucket> ruleBuckets,
            bool clearSelectionAfterApply,
            ModelColorSchemeResponse response)
        {
            if (HasActiveSchemeFor(document))
            {
                RestoreOriginalStates(document, response.Warnings);
                RestoreSelectionIfSafe(document, response);
                RequestRedraw(document);
                if (response.Warnings.Count > 0)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "The previous model color scheme could not be fully restored; the new scheme was not applied.");
                }
                ClearSession();
            }

            if (ruleBuckets.All(bucket => bucket.Items.Count == 0))
            {
                response.Applied = true;
                response.CanReset = false;
                response.Message = "The scheme matched no items; no material overrides were changed.";
                return;
            }

            var statesByItem = new Dictionary<ModelItem, ModelColorSchemeOriginalState>();
            foreach (var bucket in ruleBuckets)
            {
                foreach (var item in bucket.Items)
                {
                    if (!statesByItem.ContainsKey(item))
                        statesByItem[item] = CaptureOriginalState(item);
                }
            }

            _sessionIdentity = ModelColorSchemeDocumentIdentity.Capture(document);
            _originalStates = new List<ModelColorSchemeOriginalState>();
            _selectionBeforeApply = SnapshotSelection(document);
            _selectionClearedByScheme = false;
            _hasActiveScheme = true;
            var verificationSamples =
                new List<Tuple<ModelItem, Autodesk.Navisworks.Api.Color>>();

            try
            {
                foreach (var bucket in ruleBuckets)
                {
                    if (bucket.Items.Count == 0)
                        continue;

                    foreach (var item in bucket.Items)
                        _originalStates.Add(statesByItem[item]);
                    document.Models.OverridePermanentColor(bucket.Items, bucket.Rule.Color);
                    if (bucket.Rule.Rule.Transparency.HasValue)
                    {
                        document.Models.OverridePermanentTransparency(
                            bucket.Items,
                            bucket.Rule.Rule.Transparency.Value);
                    }
                    foreach (var item in bucket.Items)
                    {
                        if (verificationSamples.Count >= 100)
                            break;
                        verificationSamples.Add(
                            Tuple.Create(item, bucket.Rule.Color));
                    }
                    response.ColoredItemCount += bucket.Items.Count;
                }

                if (clearSelectionAfterApply &&
                    document.CurrentSelection != null &&
                    document.CurrentSelection.SelectedItems != null &&
                    document.CurrentSelection.SelectedItems.Count > 0)
                {
                    response.SelectionClearedItemCount =
                        document.CurrentSelection.SelectedItems.Count;
                    document.CurrentSelection.Clear();
                    _selectionClearedByScheme = true;
                }
                VerifyAppliedColors(verificationSamples, response);
            }
            catch (Exception ex)
            {
                RestoreOriginalStates(document, response.Warnings);
                RestoreSelectionIfSafe(document, response);
                RequestRedraw(document);
                if (response.Warnings.Count == 0)
                    ClearSession();
                else
                    Logger.Error(string.Join(" | ", response.Warnings), "ModelColorScheme");
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Model color scheme apply failed: " + ex.Message +
                    (response.Warnings.Count == 0
                        ? " Applied changes were restored."
                        : " Automatic restore was incomplete; reset remains available."));
            }

            response.Applied = true;
            response.CanReset = true;
            RequestRedraw(document);
            response.Message =
                "Model color scheme applied. Reset is available only in this Navisworks host session and document.";
        }

        private static List<ModelColorSchemePreparedRule> PrepareRules(List<ModelColorSchemeRule> rules)
        {
            var source = rules ?? new List<ModelColorSchemeRule>();
            if (source.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "At least one color rule is required.");
            if (source.Count > MaximumRuleCount)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "Rule count exceeds the maximum of " + MaximumRuleCount + ".");
            }

            var prepared = new List<ModelColorSchemePreparedRule>();
            for (var index = 0; index < source.Count; index++)
            {
                var rule = ModelColorSchemeRuleMatcher.NormalizeRule(source[index]);
                if (!ModelColorSchemeRuleMatcher.HasMatchers(rule))
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) + " must contain at least one matcher.");
                }
                if (string.IsNullOrWhiteSpace(rule.ColorHex))
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) + " requires colorHex.");
                }
                if (rule.Transparency.HasValue &&
                    (float.IsNaN(rule.Transparency.Value) ||
                     float.IsInfinity(rule.Transparency.Value) ||
                     rule.Transparency.Value < 0 ||
                     rule.Transparency.Value > 1))
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) + " transparency must be between 0 and 1.");
                }

                System.Drawing.Color parsed;
                try
                {
                    parsed = ColorParser.ParseColor(rule.ColorHex);
                }
                catch (Exception ex)
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) + " colorHex is invalid: " + ex.Message);
                }
                if (parsed.A != 255)
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) +
                        " colorHex alpha is not supported; use the separate transparency field.");
                }

                prepared.Add(new ModelColorSchemePreparedRule
                {
                    Rule = rule,
                    Name = string.IsNullOrWhiteSpace(rule.Name)
                        ? "Rule " + (index + 1)
                        : rule.Name.Trim(),
                    ColorHex = "#" + parsed.R.ToString("X2") + parsed.G.ToString("X2") + parsed.B.ToString("X2"),
                    Color = Autodesk.Navisworks.Api.Color.FromByteRGB(parsed.R, parsed.G, parsed.B),
                });
            }

            return prepared;
        }

        private static ModelColorSchemeCollectionResult CollectItems(
            Document document,
            string scope,
            int maxItems,
            bool includeContainers,
            Stopwatch operationTimer,
            int workBudgetSeconds)
        {
            var result = new ModelColorSchemeCollectionResult();
            var stack = new Stack<ModelItem>();
            if (scope == "selection")
            {
                var selected = document.CurrentSelection == null
                    ? Enumerable.Empty<ModelItem>()
                    : document.CurrentSelection.SelectedItems;
                foreach (var item in selected.Reverse())
                    if (item != null)
                        stack.Push(item);
            }
            else
            {
                var roots = document.Models == null
                    ? new List<ModelItem>()
                    : document.Models
                        .Where(model => model != null && model.RootItem != null)
                        .Select(model => model.RootItem)
                        .ToList();
                for (var index = roots.Count - 1; index >= 0; index--)
                    stack.Push(roots[index]);
            }

            var visited = new HashSet<ModelItem>();
            while (stack.Count > 0 &&
                   result.TraversedItemCount < maxItems &&
                   operationTimer.Elapsed.TotalSeconds < workBudgetSeconds)
            {
                var item = stack.Pop();
                if (item == null || !visited.Add(item))
                    continue;

                result.TraversedItemCount++;
                var children = SafeChildren(item);
                var hasChildren = children.Count > 0;
                var hasGeometry = SafeBool(() => item.HasGeometry);
                if (hasGeometry && (includeContainers || !hasChildren))
                    result.Items.Add(item);

                for (var index = children.Count - 1; index >= 0; index--)
                    stack.Push(children[index]);
            }

            result.WorkBudgetReached =
                stack.Count > 0 &&
                operationTimer.Elapsed.TotalSeconds >= workBudgetSeconds;
            result.Truncated = stack.Count > 0;
            return result;
        }

        private static ModelColorSchemeItemFacts BuildItemFacts(
            ModelItem item,
            int maxPropertiesPerItem,
            List<string> categoryFilters,
            List<string> propertyFilters,
            bool includeAncestors = false,
            bool collectProperties = true,
            Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts> propertyCache = null,
            Dictionary<ModelItem, string> sourceFileCache = null)
        {
            var facts = new ModelColorSchemeItemFacts
            {
                Name = SafeString(() => item.DisplayName),
                Path = BuildItemPath(item),
                SourceFile = GetSourceFileFromProperties(item, sourceFileCache),
            };
            if (string.IsNullOrWhiteSpace(facts.SourceFile))
                facts.SourceFile = GetSourceFile(item);
            if (item == null || !collectProperties)
                return facts;

            var propertyCount = 0;
            try
            {
                IEnumerable<ModelItem> propertyItems;
                if (includeAncestors)
                    propertyItems = item.AncestorsAndSelf;
                else
                    propertyItems = new[] { item };
                foreach (var propertyItem in propertyItems)
                {
                    var cached = ReadCachedProperties(
                        propertyItem,
                        maxPropertiesPerItem,
                        categoryFilters,
                        propertyFilters,
                        propertyCache);
                    foreach (var propertyFact in cached.Properties)
                    {
                        if (propertyCount >= maxPropertiesPerItem)
                        {
                            facts.PropertiesTruncated = true;
                            return facts;
                        }
                        facts.Properties.Add(propertyFact);
                        propertyCount++;
                    }
                    if (cached.Truncated)
                    {
                        facts.PropertiesTruncated = true;
                        return facts;
                    }
                }
            }
            catch
            {
            }

            return facts;
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
                    SampleItemPath = facts.Path ?? string.Empty,
                    SampleSourceFile = facts.SourceFile ?? string.Empty,
                };
                candidates[key] = candidate;
                distinctCountByKind[kind] = kindCount + 1;
            }
            candidate.Count++;
        }

        private static ModelColorSchemeCachedPropertyFacts ReadCachedProperties(
            ModelItem item,
            int maxProperties,
            List<string> categoryFilters,
            List<string> propertyFilters,
            Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts> cache)
        {
            ModelColorSchemeCachedPropertyFacts cached;
            if (item != null && cache != null && cache.TryGetValue(item, out cached))
                return cached;

            cached = new ModelColorSchemeCachedPropertyFacts();
            try
            {
                if (item != null && item.PropertyCategories != null)
                {
                    foreach (var category in item.PropertyCategories)
                    {
                        if (category == null || category.Properties == null)
                            continue;

                        var categoryName = SafeString(() => category.DisplayName);
                        var categoryInternalName = SafeString(() => category.Name);
                        if (!MatchesOptionalFilters(
                            categoryFilters,
                            categoryName,
                            categoryInternalName))
                        {
                            continue;
                        }

                        foreach (var property in category.Properties)
                        {
                            if (property == null)
                                continue;

                            var propertyName = SafeString(() => property.DisplayName);
                            var propertyInternalName = SafeString(() => property.Name);
                            if (!MatchesOptionalFilters(
                                propertyFilters,
                                propertyName,
                                propertyInternalName))
                            {
                                continue;
                            }

                            var value = GetPropertyValue(property);
                            if (string.IsNullOrWhiteSpace(value))
                                continue;
                            if (cached.Properties.Count >= maxProperties)
                            {
                                cached.Truncated = true;
                                break;
                            }

                            cached.Properties.Add(new ModelColorSchemePropertyFact
                            {
                                Category = categoryName,
                                Property = propertyName,
                                Value = value,
                            });
                        }
                        if (cached.Truncated)
                            break;
                    }
                }
            }
            catch
            {
            }

            if (item != null && cache != null)
                cache[item] = cached;
            return cached;
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

        private static ModelColorSchemeOriginalState CaptureOriginalState(ModelItem item)
        {
            if (item == null || !SafeBool(() => item.HasGeometry))
                throw new InvalidOperationException("A color-scheme item has no readable geometry.");

            var color = item.Geometry.PermanentColor;
            var originalColor = item.Geometry.OriginalColor;
            var transparency = item.Geometry.PermanentTransparency;
            var originalTransparency = item.Geometry.OriginalTransparency;
            return new ModelColorSchemeOriginalState
            {
                Item = item,
                Color = color,
                Transparency = transparency,
                HadMaterialOverride =
                    !ColorsEqual(color, originalColor) ||
                    Math.Abs(transparency - originalTransparency) > 0.000001,
            };
        }

        private void RestoreOriginalStates(Document document, List<string> warnings)
        {
            var states = _originalStates ?? new List<ModelColorSchemeOriginalState>();
            try
            {
                document.Models.ResetPermanentMaterials(
                    ToModelItemCollection(states.Select(state => state.Item)));
                foreach (var colorGroup in states
                    .Where(state => state.HadMaterialOverride)
                    .GroupBy(state => ColorKey(state.Color)))
                {
                    var items = ToModelItemCollection(colorGroup.Select(state => state.Item));
                    document.Models.OverridePermanentColor(items, colorGroup.First().Color);
                }
                foreach (var transparencyGroup in states
                    .Where(state => state.HadMaterialOverride)
                    .GroupBy(state => state.Transparency))
                {
                        document.Models.OverridePermanentTransparency(
                        ToModelItemCollection(transparencyGroup.Select(state => state.Item)),
                        transparencyGroup.Key);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not restore model color scheme materials: " + ex.Message);
            }
        }

        private static bool ColorsEqual(
            Autodesk.Navisworks.Api.Color left,
            Autodesk.Navisworks.Api.Color right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.R == right.R && left.G == right.G && left.B == right.B;
        }

        private static string ColorKey(Autodesk.Navisworks.Api.Color color)
        {
            return color == null ? string.Empty : color.R + ":" + color.G + ":" + color.B;
        }

        private static ModelItemCollection ToModelItemCollection(IEnumerable<ModelItem> items)
        {
            var result = new ModelItemCollection();
            foreach (var item in items ?? Enumerable.Empty<ModelItem>())
                if (item != null)
                    result.Add(item);
            return result;
        }

        private static void RequestRedraw(Document document)
        {
            try
            {
                if (document != null && document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch
            {
            }
        }

        private bool HasActiveSchemeFor(Document document)
        {
            return _hasActiveScheme &&
                   _sessionIdentity != null &&
                   _sessionIdentity.Matches(document);
        }

        private void DiscardStaleSession(Document document)
        {
            if (_hasActiveScheme && !HasActiveSchemeFor(document))
                ClearSession();
        }

        private void ClearSession()
        {
            _sessionIdentity = null;
            _originalStates = null;
            _selectionBeforeApply = null;
            _selectionClearedByScheme = false;
            _hasActiveScheme = false;
        }

        private static ModelItemCollection SnapshotSelection(Document document)
        {
            var result = new ModelItemCollection();
            try
            {
                if (document != null &&
                    document.CurrentSelection != null &&
                    document.CurrentSelection.SelectedItems != null)
                {
                    result.CopyFrom(document.CurrentSelection.SelectedItems);
                }
            }
            catch
            {
            }
            return result;
        }

        private void EnsureSelectionAvailableForScope(Document document)
        {
            if (!_selectionClearedByScheme ||
                _selectionBeforeApply == null ||
                _selectionBeforeApply.Count == 0)
            {
                return;
            }

            try
            {
                if (document.CurrentSelection != null &&
                    (document.CurrentSelection.SelectedItems == null ||
                     document.CurrentSelection.SelectedItems.Count == 0))
                {
                    document.CurrentSelection.CopyFrom(_selectionBeforeApply);
                    _selectionClearedByScheme = false;
                }
            }
            catch
            {
            }
        }

        private void RestoreSelectionIfSafe(
            Document document,
            ModelColorSchemeResponse response)
        {
            if (!_selectionClearedByScheme ||
                _selectionBeforeApply == null ||
                _selectionBeforeApply.Count == 0)
            {
                return;
            }

            try
            {
                if (document.CurrentSelection != null &&
                    (document.CurrentSelection.SelectedItems == null ||
                     document.CurrentSelection.SelectedItems.Count == 0))
                {
                    document.CurrentSelection.CopyFrom(_selectionBeforeApply);
                    response.SelectionRestored = true;
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add(
                    "Could not restore the selection cleared by the model color scheme: " +
                    ex.Message);
            }
        }

        private static void VerifyAppliedColors(
            List<Tuple<ModelItem, Autodesk.Navisworks.Api.Color>> samples,
            ModelColorSchemeResponse response)
        {
            foreach (var sample in samples ??
                new List<Tuple<ModelItem, Autodesk.Navisworks.Api.Color>>())
            {
                try
                {
                    if (sample.Item1 == null || !sample.Item1.HasGeometry)
                        continue;
                    response.ColorVerificationSampleCount++;
                    if (ColorsEqual(
                        sample.Item1.Geometry.PermanentColor,
                        sample.Item2))
                    {
                        response.PermanentColorMatchCount++;
                    }
                    if (ColorsEqual(
                        sample.Item1.Geometry.ActiveColor,
                        sample.Item2))
                    {
                        response.ActiveColorMatchCount++;
                    }
                }
                catch
                {
                }
            }

            if (response.ColorVerificationSampleCount > 0 &&
                response.PermanentColorMatchCount <
                response.ColorVerificationSampleCount)
            {
                response.Warnings.Add(
                    "Navisworks did not retain the requested permanent color on every verification sample.");
            }
            else if (response.ColorVerificationSampleCount > 0 &&
                     response.ActiveColorMatchCount <
                     response.ColorVerificationSampleCount)
            {
                response.Warnings.Add(
                    "Permanent colors were stored, but another Navisworks display layer still masks some active colors.");
            }
        }

        private static string NormalizeOperation(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "analyze"
                : value.Trim().ToLowerInvariant();
            if (normalized == "analyze" || normalized == "apply" || normalized == "reset")
                return normalized;
            throw new AgentCommandException(
                ErrorCodes.SchemaViolation,
                "operation must be analyze, apply, or reset.");
        }

        private static string NormalizeScope(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "model"
                : value.Trim().ToLowerInvariant();
            if (normalized == "model" || normalized == "selection")
                return normalized;
            throw new AgentCommandException(
                ErrorCodes.SchemaViolation,
                "scope must be model or selection.");
        }

        private static string NormalizeVerbosity(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "compact"
                : value.Trim().ToLowerInvariant();
            if (normalized == "compact" || normalized == "full")
                return normalized;
            throw new AgentCommandException(
                ErrorCodes.SchemaViolation,
                "verbosity must be compact or full.");
        }

        private static int Clamp(int? value, int defaultValue, int minimum, int maximum)
        {
            var result = value.GetValueOrDefault(defaultValue);
            if (result < minimum)
                return minimum;
            if (result > maximum)
                return maximum;
            return result;
        }

        private static bool MatchesOptionalFilters(
            List<string> filters,
            string displayName,
            string internalName)
        {
            if (filters == null || filters.Count == 0)
                return true;
            return filters.Any(filter =>
                (!string.IsNullOrWhiteSpace(displayName) &&
                 displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(internalName) &&
                 internalName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool HasPropertyMatchers(ModelColorSchemeRule rule)
        {
            return rule != null &&
                   (rule.CategoryContains.Count > 0 ||
                    rule.PropertyContains.Count > 0 ||
                    rule.PropertyValueContains.Count > 0);
        }

        private static List<ModelItem> SafeChildren(ModelItem item)
        {
            try
            {
                return item == null || item.Children == null
                    ? new List<ModelItem>()
                    : item.Children.Where(child => child != null).ToList();
            }
            catch
            {
                return new List<ModelItem>();
            }
        }

        private static string GetSourceFile(ModelItem item)
        {
            try
            {
                var model = item == null ? null : item.Model;
                if (model == null)
                    return string.Empty;
                return !string.IsNullOrWhiteSpace(model.SourceFileName)
                    ? model.SourceFileName
                    : model.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSourceFileFromProperties(
            ModelItem item,
            Dictionary<ModelItem, string> cache)
        {
            try
            {
                foreach (var current in item.AncestorsAndSelf)
                {
                    string cached;
                    if (cache != null && cache.TryGetValue(current, out cached))
                    {
                        if (!string.IsNullOrWhiteSpace(cached))
                            return cached;
                        continue;
                    }

                    var found = string.Empty;
                    foreach (var category in current.PropertyCategories)
                    {
                        if (category == null || category.Properties == null)
                            continue;
                        foreach (var property in category.Properties)
                        {
                            var name = SafeString(() => property.DisplayName);
                            var internalName = SafeString(() => property.Name);
                            if (!IsSourceFileProperty(name, internalName))
                                continue;
                            found = GetPropertyValue(property);
                            if (!string.IsNullOrWhiteSpace(found))
                                break;
                        }
                        if (!string.IsNullOrWhiteSpace(found))
                            break;
                    }
                    if (cache != null)
                        cache[current] = found;
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private static bool IsSourceFileProperty(string displayName, string internalName)
        {
            var values = new[] { displayName ?? string.Empty, internalName ?? string.Empty };
            return values.Any(value =>
                value.IndexOf("Файл источника", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Source File", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SourceFile", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildItemPath(ModelItem item)
        {
            try
            {
                return string.Join(
                    "/",
                    item.AncestorsAndSelf
                        .Reverse()
                        .Select(current => SafeString(() => current.DisplayName))
                        .Where(name => !string.IsNullOrWhiteSpace(name)));
            }
            catch
            {
                return SafeString(() => item.DisplayName);
            }
        }

        private static string GetPropertyValue(DataProperty property)
        {
            try
            {
                if (property == null || property.Value == null)
                    return string.Empty;
                return property.Value.ToDisplayString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeString(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

        private sealed class ModelColorSchemePreparedRule
        {
            public ModelColorSchemeRule Rule;
            public string Name;
            public string ColorHex;
            public Autodesk.Navisworks.Api.Color Color;
        }

        private sealed class ModelColorSchemeRuleBucket
        {
            public ModelColorSchemePreparedRule Rule;
            public ModelItemCollection Items = new ModelItemCollection();
            public string SampleItemName;
            public string SampleItemPath;
            public string SampleSourceFile;
        }

        private sealed class ModelColorSchemeCollectionResult
        {
            public int TraversedItemCount;
            public bool Truncated;
            public bool WorkBudgetReached;
            public List<ModelItem> Items = new List<ModelItem>();
        }

        private sealed class ModelColorSchemeOriginalState
        {
            public ModelItem Item;
            public Autodesk.Navisworks.Api.Color Color;
            public double Transparency;
            public bool HadMaterialOverride;
        }

        private sealed class ModelColorSchemeCachedPropertyFacts
        {
            public bool Truncated;
            public List<ModelColorSchemePropertyFact> Properties =
                new List<ModelColorSchemePropertyFact>();
        }

        private sealed class ModelColorSchemeCandidateAccumulator
        {
            public string Kind;
            public string Category;
            public string Property;
            public string Value;
            public int Count;
            public string SampleItemName;
            public string SampleItemPath;
            public string SampleSourceFile;

            public ModelColorSchemeCandidate ToResponse(string verbosity)
            {
                var compact = string.Equals(
                    verbosity,
                    "compact",
                    StringComparison.OrdinalIgnoreCase);
                return new ModelColorSchemeCandidate
                {
                    Kind = Kind,
                    Category = compact ? TruncateText(Category, 200) : Category,
                    Property = compact ? TruncateText(Property, 200) : Property,
                    Value = compact ? TruncateText(Value, 500) : Value,
                    Count = Count,
                    SampleItemName = compact ? TruncateText(SampleItemName, 200) : SampleItemName,
                    SampleItemPath = compact ? string.Empty : SampleItemPath,
                    SampleSourceFile = compact ? TruncateText(SampleSourceFile, 300) : SampleSourceFile,
                };
            }

            private static string TruncateText(string value, int maximumLength)
            {
                value = value ?? string.Empty;
                return value.Length <= maximumLength
                    ? value
                    : value.Substring(0, maximumLength) + "…";
            }
        }
    }

    internal sealed class ModelColorSchemeDocumentIdentity
    {
        private readonly Document _document;
        private readonly string _fileName;
        private readonly HashSet<string> _modelIdentities;

        private ModelColorSchemeDocumentIdentity(
            Document document,
            string fileName,
            HashSet<string> modelIdentities)
        {
            _document = document;
            _fileName = fileName;
            _modelIdentities = modelIdentities;
        }

        public static ModelColorSchemeDocumentIdentity Capture(Document document)
        {
            return document == null
                ? null
                : new ModelColorSchemeDocumentIdentity(
                    document,
                    NormalizePath(SafeRead(() => document.FileName)),
                    ReadModelIdentities(document));
        }

        public bool Matches(Document document)
        {
            return HasSameModelContent(document) &&
                   string.Equals(
                       _fileName,
                       NormalizePath(SafeRead(() => document.FileName)),
                       StringComparison.OrdinalIgnoreCase);
        }

        public bool HasSameModelContent(Document document)
        {
            var currentIdentities = ReadModelIdentities(document);
            return document != null &&
                   ReferenceEquals(_document, document) &&
                   _modelIdentities != null &&
                   _modelIdentities.Count > 0 &&
                   currentIdentities != null &&
                   _modelIdentities.IsSubsetOf(currentIdentities);
        }

        private static HashSet<string> ReadModelIdentities(Document document)
        {
            if (document == null || document.Models == null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var model in document.Models)
                {
                    if (model == null)
                        continue;
                    identities.Add(
                        SafeRead(() => model.Guid.ToString("D")) + "|" +
                        NormalizePath(SafeRead(() => model.SourceFileName)) + "|" +
                        NormalizePath(SafeRead(() => model.FileName)));
                }
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return identities;
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try { return Path.GetFullPath(value.Trim()); }
            catch { return value.Trim(); }
        }

        private static string SafeRead(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
