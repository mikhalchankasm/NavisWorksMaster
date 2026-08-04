using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.AI
{
    internal sealed class AIColorOperationContext
    {
        internal AIColorOperationContext(
            Document document,
            AIColorDocumentIdentity documentIdentity,
            ModelItemCollection selection,
            IReadOnlyList<string> objectNames,
            string key,
            int keyGeneration,
            string modelId,
            ColorSchemeType scheme,
            double temperature)
        {
            Document = document;
            DocumentIdentity = documentIdentity;
            Selection = selection;
            ObjectNames = objectNames;
            Key = key ?? string.Empty;
            KeyGeneration = keyGeneration;
            ModelId = modelId ?? string.Empty;
            Scheme = scheme;
            Temperature = temperature;
        }

        internal Document Document { get; }
        internal AIColorDocumentIdentity DocumentIdentity { get; }
        internal ModelItemCollection Selection { get; }
        internal IReadOnlyList<string> ObjectNames { get; }
        internal string Key { get; }
        internal int KeyGeneration { get; }
        internal string ModelId { get; }
        internal ColorSchemeType Scheme { get; }
        internal double Temperature { get; }
    }

    internal sealed class AIColorWorkflow
    {
        private readonly OpenRouterKeyStore _keyStore;
        private readonly IOpenRouterTransport _transport;
        private readonly OpenRouterCatalogCache _catalogCache;

        internal AIColorWorkflow()
            : this(
                OpenRouterKeyStore.Current,
                new AiWorkerTransport(),
                OpenRouterCatalogCache.Current)
        {
        }

        internal AIColorWorkflow(
            OpenRouterKeyStore keyStore,
            IOpenRouterTransport transport,
            OpenRouterCatalogCache catalogCache)
        {
            _keyStore = keyStore ??
                        throw new ArgumentNullException(nameof(keyStore));
            _transport = transport ??
                         throw new ArgumentNullException(nameof(transport));
            _catalogCache = catalogCache ??
                            throw new ArgumentNullException(nameof(catalogCache));
        }

        // This method must run on the Navisworks UI thread. It snapshots every
        // Navisworks-backed value before the first asynchronous operation.
        internal bool TryPrepareOnUiThread(
            bool requireOpenRouter,
            out AIColorOperationContext context,
            out AiColorOutcome failure)
        {
            context = null;
            failure = null;

            var document = NwApplication.ActiveDocument;
            Selection currentSelection =
                document == null ? null : document.CurrentSelection;
            var selectedItems = currentSelection?.GetSelectedItems();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.NoSelection);
                return false;
            }

            var colorableSelection =
                AIColorUtils.FilterColorableObjects(selectedItems);
            if (colorableSelection.Count == 0)
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.NoColorableObjects);
                return false;
            }

            var objectNames =
                AIColorUtils.GetObjectNamesFromSelection(colorableSelection);
            if (objectNames.Count == 0)
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.NoObjectNames);
                return false;
            }

            var uniqueObjectNames = objectNames
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (requireOpenRouter &&
                uniqueObjectNames.Length >
                OpenRouterColorRequestLimits.MaxUniqueObjectNames)
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.TooManyObjects);
                return false;
            }

            var config = AIConfig.Instance.CaptureSnapshot();
            var modelId = OpenRouterModelSelection.MigrationCandidate(
                config.ModelName);
            if (requireOpenRouter && string.IsNullOrWhiteSpace(modelId))
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.ModelNotSelected);
                return false;
            }

            var key = requireOpenRouter ? _keyStore.GetKey() : string.Empty;
            if (requireOpenRouter && string.IsNullOrWhiteSpace(key))
            {
                failure = AiColorOutcome.Failure(
                    AiColorOutcomeKind.MissingKey);
                return false;
            }

            context = new AIColorOperationContext(
                document,
                AIColorDocumentIdentity.Capture(document),
                CopySelection(colorableSelection),
                uniqueObjectNames,
                key,
                _keyStore.Generation,
                modelId,
                (ColorSchemeType)config.ColorScheme,
                config.Temperature);
            return true;
        }

        // This method is Navisworks-free and is safe on a worker thread.
        internal async Task<AiColorOutcome> ExecuteNetworkAsync(
            AIColorOperationContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);

            var catalog = _catalogCache.TryGet(
                context.KeyGeneration,
                DateTime.UtcNow);
            if (catalog == null)
            {
                catalog = await _transport.GetModelsAsync(
                        context.Key,
                        cancellationToken)
                    .ConfigureAwait(false);
                _catalogCache.Store(
                    context.KeyGeneration,
                    catalog,
                    DateTime.UtcNow);
            }

            var policy = OpenRouterModelPolicy.Evaluate(
                catalog,
                context.ModelId);
            if (!policy.MaySendChat)
                return policy.Failure;

            var requestPolicy = OpenRouterColorRequestPolicy.Evaluate(
                policy.Model,
                context.ObjectNames);
            if (!requestPolicy.MaySend)
                return AiColorOutcome.Failure(
                    requestPolicy.FailureOutcomeKind,
                    null,
                    new AiColorDiagnostics(
                        string.Empty,
                        context.ObjectNames.Count,
                        requestPolicy.OutputBudget,
                        policy.Model.MaxCompletionTokens,
                        requestPolicy.ReasoningPolicy));

            var outcome = await _transport.GetColorsAsync(
                    context.Key,
                    context.ObjectNames,
                    ColorSchemes.GetSchemeNameRu(context.Scheme),
                    policy.Model,
                    context.Temperature,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!outcome.IsSuccess)
                return outcome;

            return AiColorOutcome.Success(
                AiColorSource.OpenRouter,
                outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal),
                outcome.Diagnostics);
        }

        internal AiColorOutcome CreateLocalPaletteOutcome(
            AIColorOperationContext context)
        {
            if (context == null || context.ObjectNames.Count == 0)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);

            var colors = ColorSchemes.GenerateColorsForObjects(
                context.Scheme,
                new List<string>(context.ObjectNames));
            if (colors.Count == 0)
                return AiColorOutcome.Failure(
                    AiColorOutcomeKind.InvalidRequest);
            return AiColorOutcome.Success(
                AiColorSource.LocalPalette,
                colors);
        }

        // This method must run on the Navisworks UI thread after the document
        // identity guard has succeeded.
        internal int ApplyOnUiThread(
            AIColorOperationContext context,
            AiColorOutcome outcome)
        {
            if (context == null || outcome == null || !outcome.IsSuccess)
                return 0;
            return AIColorUtils.ApplyColorsToObjects(
                context.Document,
                context.Selection,
                outcome.Colors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));
        }

        private static ModelItemCollection CopySelection(
            ModelItemCollection source)
        {
            var copy = new ModelItemCollection();
            foreach (var item in source)
                copy.Add(item);
            return copy;
        }

    }
}
