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
        private void Apply(
            Document document,
            List<ModelColorSchemeRuleBucket> ruleBuckets,
            bool clearSelectionAfterApply,
            ModelColorSchemeApplyMode mode,
            ModelColorSchemeResponse response)
        {
            // #338: replace used to restore the active scheme silently, so batch applies undid each other.
            var accumulate = mode == ModelColorSchemeApplyMode.Accumulate && HasActiveSchemeFor(document);
            if (HasActiveSchemeFor(document) && !accumulate)
            {
                var restoredCount = _originalStates.Count;
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
                response.PreviousSchemeRestored = true;
                response.RestoredItemCount = restoredCount;
                response.Warnings.Add(
                    "The previous model color scheme (" + restoredCount +
                    " items) was restored before this apply; pass mode=accumulate to add to it instead.");
            }

            if (ruleBuckets.All(bucket => bucket.Items.Count == 0))
            {
                response.Applied = true;
                response.CanReset = accumulate;
                response.ActiveSchemeItemCount = accumulate ? _originalStates.Count : 0;
                response.Message = accumulate
                    ? "The scheme matched no items; the active model color scheme is unchanged."
                    : "The scheme matched no items; no material overrides were changed.";
                return;
            }

            // Read every new original state before the session changes, so a failed read
            // leaves the active scheme as it was. Items an earlier accumulated batch already
            // captured keep that pre-scheme state.
            var staged = new ModelColorSchemeLedger<ModelItem, ModelColorSchemeOriginalState>();
            foreach (var bucket in ruleBuckets)
            {
                foreach (var item in bucket.Items)
                {
                    if (!accumulate || !_originalStates.Contains(item))
                        staged.TryCapture(item, CaptureOriginalState);
                }
            }

            if (!accumulate)
            {
                ClearSession();
                _sessionIdentity = ModelColorSchemeDocumentIdentity.Capture(document);
                _selectionBeforeApply = SnapshotSelection(document);
            }
            _hasActiveScheme = true;
            foreach (var state in staged.States)
                _originalStates.TryCapture(state.Item, item => state);
            var verificationSamples =
                new List<Tuple<ModelItem, Autodesk.Navisworks.Api.Color>>();

            try
            {
                using (var transaction = document.BeginTransaction("NavisHelper model color scheme apply"))
                {
                    foreach (var bucket in ruleBuckets)
                    {
                        if (bucket.Items.Count == 0)
                            continue;

                        if (bucket.Rule.Color != null)
                        {
                            document.Models.OverridePermanentColor(bucket.Items, bucket.Rule.Color);
                            foreach (var item in bucket.Items)
                            {
                                if (verificationSamples.Count >= 100)
                                    break;
                                verificationSamples.Add(
                                    Tuple.Create(item, bucket.Rule.Color));
                            }
                        }
                        if (bucket.Rule.Rule.Transparency.HasValue)
                        {
                            document.Models.OverridePermanentTransparency(
                                bucket.Items,
                                bucket.Rule.Rule.Transparency.Value);
                        }
                        response.ColoredItemCount += bucket.Items.Count;
                    }
                    transaction.Commit();
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
                // A separate list: response.Warnings may already carry the replace notice.
                var restoreWarnings = new List<string>();
                RestoreOriginalStates(document, restoreWarnings);
                RestoreSelectionIfSafe(document, response);
                RequestRedraw(document);
                if (restoreWarnings.Count == 0)
                    ClearSession();
                else
                    Logger.Error(string.Join(" | ", restoreWarnings), "ModelColorScheme");
                throw new AgentCommandException(
                    ErrorCodes.CommandFailed,
                    "Model color scheme apply failed: " + ex.Message +
                    (restoreWarnings.Count == 0
                        ? (accumulate
                            ? " The whole active scheme, earlier accumulated batches included, was restored."
                            : " Applied changes were restored.")
                        : " Automatic restore was incomplete; reset remains available."));
            }

            response.Applied = true;
            response.CanReset = true;
            response.ActiveSchemeItemCount = _originalStates.Count;
            RequestRedraw(document);
            response.Message = accumulate
                ? "Model color scheme added to the active scheme (" + _originalStates.Count +
                  " items in total); reset restores all of them. Reset is available only in this Navisworks host session and document."
                : "Model color scheme applied. Reset is available only in this Navisworks host session and document.";
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
                var appearanceError = ModelColorSchemeRuleMatcher.DescribeAppearanceError(rule);
                if (appearanceError != null)
                {
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "Rule " + (index + 1) + " " + appearanceError);
                }

                var colorHex = string.Empty;
                Autodesk.Navisworks.Api.Color color = null;
                if (!string.IsNullOrWhiteSpace(rule.ColorHex))
                {
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
                    colorHex = "#" + parsed.R.ToString("X2") + parsed.G.ToString("X2") + parsed.B.ToString("X2");
                    color = Autodesk.Navisworks.Api.Color.FromByteRGB(parsed.R, parsed.G, parsed.B);
                }

                prepared.Add(new ModelColorSchemePreparedRule
                {
                    Rule = rule,
                    Name = string.IsNullOrWhiteSpace(rule.Name)
                        ? "Rule " + (index + 1)
                        : rule.Name.Trim(),
                    ColorHex = colorHex,
                    Color = color,
                });
            }

            return prepared;
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
            var states = _originalStates.States;
            try
            {
                using (var transaction = document.BeginTransaction("NavisHelper model color scheme restore"))
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
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not restore model color scheme materials: " + ex.Message);
            }
        }

        // Exact on purpose: both colors come from Navisworks, and a sub-byte permanent
        // override must still count as an override so reset restores it. Requested
        // versus read-back colors are compared by ModelColorSchemeColorVerifier instead.
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

        private static ModelColorSchemeRgb ToRgb(Autodesk.Navisworks.Api.Color color)
        {
            return new ModelColorSchemeRgb(color.R, color.G, color.B);
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
            catch (Exception ex)
            {
                Logger.Warn("Could not request redraw after model color scheme update: " + ex.Message, "ModelColorScheme");
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
            _originalStates.Clear();
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
            catch (Exception ex)
            {
                Logger.Warn("Could not snapshot selection before model color scheme update: " + ex.Message, "ModelColorScheme");
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
            catch (Exception ex)
            {
                Logger.Warn("Could not restore selection for model color scheme scope: " + ex.Message, "ModelColorScheme");
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
            var readings = new List<ModelColorSchemeColorSample>();
            foreach (var sample in samples ??
                new List<Tuple<ModelItem, Autodesk.Navisworks.Api.Color>>())
            {
                if (sample.Item1 == null ||
                    sample.Item2 == null ||
                    !SafeBool(() => sample.Item1.HasGeometry))
                {
                    continue;
                }
                ModelColorSchemeColorSample reading = null;
                var readStage = "RequestedColor";
                try
                {
                    reading = new ModelColorSchemeColorSample { Requested = ToRgb(sample.Item2) };
                    readStage = "Geometry";
                    var geometry = sample.Item1.Geometry;
                    readStage = "PermanentColor";
                    reading.Permanent = ToRgbOrNull(geometry.PermanentColor);
                    readStage = "ActiveColor";
                    reading.Active = ToRgbOrNull(geometry.ActiveColor);
                }
                catch (Exception ex)
                {
                    var error = (readStage + ": " + ex.GetType().Name + ": " + ex.Message)
                        .Replace('\r', ' ').Replace('\n', ' ');
                    if (error.Length > 200)
                        error = error.Substring(0, 200);
                    if (reading == null)
                    {
                        response.Warnings.Add("Could not verify a requested model color: " + error);
                        continue;
                    }
                    reading.ReadError = error;
                }
                readings.Add(reading);
            }
            ModelColorSchemeColorVerifier.Tally(readings, response);
            if (samples != null && samples.Count > 0 && response.ColorVerificationSampleCount == 0)
                response.Warnings.Add("No model color verification samples could be read.");
        }

        private static ModelColorSchemeRgb? ToRgbOrNull(Autodesk.Navisworks.Api.Color color)
        {
            return color == null ? (ModelColorSchemeRgb?)null : ToRgb(color);
        }

    }
}
