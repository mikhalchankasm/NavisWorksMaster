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
        private readonly ModelColorSchemeLedger<ModelItem, ModelColorSchemeOriginalState> _originalStates =
            new ModelColorSchemeLedger<ModelItem, ModelColorSchemeOriginalState>();
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
            ModelColorSchemeApplyMode mode;
            if (!ModelColorSchemeApplyModes.TryParse(request.Mode, out mode))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "mode must be replace or accumulate.");
            var response = new ModelColorSchemeResponse
            {
                Operation = operation,
                Scope = scope,
                Apply = apply,
                Mode = ModelColorSchemeApplyModes.ToWire(mode),
                HadActiveScheme = HasActiveSchemeFor(document),
                CanReset = HasActiveSchemeFor(document),
                ActiveSchemeItemCount = HasActiveSchemeFor(document) ? _originalStates.Count : 0,
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
            var phases = new PhaseTimings();
            var collectStarted = Stopwatch.GetTimestamp();
            var collected = CollectItems(
                document,
                scope,
                maxItems,
                includeContainers,
                operationTimer,
                workBudgetSeconds);
            phases.Add("collect", Stopwatch.GetTimestamp() - collectStarted);
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
                var analysisSucceeded = false;
                try
                {
                    Analyze(
                        collected.Items,
                        request,
                        maxPropertiesPerItem,
                        candidateLimit,
                        workBudgetSeconds,
                        verbosity,
                        operationTimer,
                        phases,
                        response);
                    response.Message = response.AnalysisTruncated
                        ? "Model color analysis stopped at the host-side work budget. Narrow the scope or use analysis filters."
                        : response.ItemsTruncated
                        ? "Model color analysis completed on a truncated item scope. Increase maxItems for full coverage."
                        : "Model color analysis completed.";
                    analysisSucceeded = true;
                    return response;
                }
                finally
                {
                    LogPhaseTimings(
                        "analyze",
                        response.AnalyzedItemCount,
                        analysisSucceeded ? "ok" : "error",
                        phases,
                        operationTimer);
                }
            }

            var applySucceeded = false;
            try
            {
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
                        pathHolder: out var itemPathHolder,
                        maxPropertiesPerItem: maxPropertiesPerItem,
                        categoryFilters: categoryFilters,
                        propertyFilters: propertyFilters,
                        includeAncestors: true,
                        collectProperties: collectProperties,
                        propertyCache: propertyCache,
                        sourceFileCache: sourceFileCache,
                        phases: phases);
                    propertyFactsTruncated |= itemFacts.PropertiesTruncated;
                    response.ClassifiedItemCount++;
                    var matchStarted = Stopwatch.GetTimestamp();
                    var matched = false;
                    for (var ruleIndex = 0; ruleIndex < preparedRules.Count; ruleIndex++)
                    {
                        if (!ModelColorSchemeRuleMatcher.MatchesPrepared(preparedRules[ruleIndex].Rule, itemFacts))
                            continue;

                        ruleBuckets[ruleIndex].Items.Add(item);
                        if (string.IsNullOrWhiteSpace(ruleBuckets[ruleIndex].SampleItemName))
                        {
                            ruleBuckets[ruleIndex].SampleItemName = itemFacts.Name;
                            ruleBuckets[ruleIndex].SampleItemPathHolder = itemPathHolder;
                            ruleBuckets[ruleIndex].SampleSourceFile = itemFacts.SourceFile;
                        }
                        response.MatchedItemCount++;
                        matched = true;
                        break;
                    }

                    phases.Add("match", Stopwatch.GetTimestamp() - matchStarted);
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

                var respondStarted = Stopwatch.GetTimestamp();
                response.RuleResults = ruleBuckets
                    .Select((bucket, index) => new ModelColorSchemeRuleResult
                    {
                        RuleIndex = index + 1,
                        Name = bucket.Rule.Name,
                        ColorHex = bucket.Rule.ColorHex,
                        Transparency = bucket.Rule.Rule.Transparency,
                        MatchedItemCount = bucket.Items.Count,
                        SampleItemName = bucket.SampleItemName ?? string.Empty,
                        SampleItemPath = bucket.SampleItemPathHolder == null
                            ? string.Empty
                            : bucket.SampleItemPathHolder.GetPath() ?? string.Empty,
                        SampleSourceFile = bucket.SampleSourceFile ?? string.Empty,
                    })
                    .ToList();
                phases.Add("respond", Stopwatch.GetTimestamp() - respondStarted);

                if (!apply)
                {
                    if (response.HadActiveScheme && mode == ModelColorSchemeApplyMode.Replace)
                    {
                        response.Warnings.Add(
                            "apply=true would first restore the " + _originalStates.Count +
                            " items of the active model color scheme; pass mode=accumulate to add to it instead.");
                    }
                    response.Message = "Dry-run only. Review rule coverage, then pass apply=true to apply the model color scheme.";
                    applySucceeded = true;
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

                var applyStarted = Stopwatch.GetTimestamp();
                Apply(
                    document,
                    ruleBuckets,
                    request.ClearSelectionAfterApply.GetValueOrDefault(true),
                    mode,
                    response);
                phases.Add("apply", Stopwatch.GetTimestamp() - applyStarted);
                applySucceeded = true;
                return response;
            }
            finally
            {
                LogPhaseTimings(
                    "apply",
                    response.ClassifiedItemCount,
                    applySucceeded ? "ok" : "error",
                    phases,
                    operationTimer);
            }
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
            response.ColoredItemCount = _originalStates.Count;
            response.RestoredItemCount = _originalStates.Count;
            ClearSession();
            response.CanReset = false;
            response.ActiveSchemeItemCount = 0;
            response.Message = "Overrides changed by the active MCP model color scheme were restored.";
            return response;
        }

        private static void LogPhaseTimings(
            string operation,
            int itemCount,
            string outcome,
            PhaseTimings phases,
            Stopwatch operationTimer)
        {
            Logger.Info(
                "model_color_scheme phases op=" + operation +
                " items=" + itemCount +
                " outcome=" + outcome +
                " " + phases.Format() +
                " elapsed_ms=" + operationTimer.ElapsedMilliseconds,
                "AgentHost");
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

    }
}
