using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        private const int MaxSpatialSearchMilliseconds = 10000;

        public FindItemsByBboxResponse FindItemsByBbox(Document document, FindItemsByBboxRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            request = request ?? new FindItemsByBboxRequest();
            try
            {
                SpatialSearchOptionsHelper.ValidateBounds(request.Min, request.Max);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }

            string matchMode;
            try
            {
                matchMode = SpatialSearchOptionsHelper.NormalizeMatchMode(request.MatchMode);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }

            var maxScannedItems = SpatialSearchOptionsHelper.ClampMaxScannedItems(request.MaxScannedItems);
            var maxResults = SpatialSearchOptionsHelper.ClampMaxResults(request.MaxResults);
            var previewLimit = SpatialSearchOptionsHelper.ClampPreviewLimit(request.PreviewLimit);
            var includeHidden = request.IncludeHidden.GetValueOrDefault(true);
            var includeContainers = request.IncludeContainers.GetValueOrDefault(false);
            var sourceFileContains = (request.SourceFileContains ?? string.Empty).Trim();
            var started = Stopwatch.StartNew();
            var matches = new List<SpatialMatch>();
            var response = new FindItemsByBboxResponse
            {
                CoordinateSpace = "document_global",
                Min = request.Min,
                Max = request.Max,
                MatchMode = matchMode,
            };
            // Keyed by ModelItem for the same reason as find_items: a display-name
            // path has no sibling index, so two distinct leaves with the same name
            // inside the zone collapsed into one and the count under-reported.
            var seenItems = new HashSet<ModelItem>();

            if (document.Models != null)
            {
                // Per model rather than over document.Models.RootItemDescendantsAndSelf, so
                // that a model ruled out below is never enumerated. The flat enumeration
                // could only be filtered item by item, which is the reason this tool had no
                // lever except raising maxScannedItems: every filter ran after the counter.
                foreach (ModelItem item in EnumerateCandidateItems(document, request, sourceFileContains, response))
                {
                    if (response.ScannedItemCount >= maxScannedItems || started.ElapsedMilliseconds >= MaxSpatialSearchMilliseconds)
                    {
                        response.TraversalTruncated = true;
                        break;
                    }

                    if (item == null)
                        continue;

                    response.ScannedItemCount++;
                    if (!includeHidden && item.IsHidden)
                        continue;

                    // Any(), not Count() > 0. ModelItemEnumerableCollection implements
                    // IEnumerable<ModelItem> and nothing else -- no ICollection<T>, no
                    // Count property, checked against the 2027 assembly -- so LINQ's
                    // Count() enumerates every child, allocating a wrapper each, to
                    // answer whether there is at least one. Any() stops at the first.
                    if (!includeContainers && item.Children != null && item.Children.Any())
                        continue;

                    // Deferred. TryGetSourceFile walks every property category on the
                    // item and, failing that, every ancestor doing the same -- the most
                    // expensive thing available per item. It used to run for every
                    // scanned item, including the ones about to fail the box test, and
                    // including when no source-file filter was asked for at all. It is
                    // now read only when it is the filter, or when the item is going
                    // into the result and the record needs it.
                    string sourceFile = null;
                    if (!string.IsNullOrEmpty(sourceFileContains))
                    {
                        sourceFile = TryGetSourceFile(item) ?? string.Empty;
                        if (sourceFile.IndexOf(sourceFileContains, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    BoundingBox3D box;
                    try
                    {
                        box = item.BoundingBox();
                    }
                    catch (Exception ex)
                    {
                        if (response.Warnings.Count < 10)
                            response.Warnings.Add("Skipped item with unreadable bounding box: " + ex.Message);
                        continue;
                    }

                    if (box == null || !MatchesSpatialBox(box, request.Min, request.Max, matchMode))
                        continue;

                    if (!seenItems.Add(item))
                        continue;

                    response.MatchedItemCount++;
                    if (matches.Count >= maxResults)
                    {
                        response.ResultsTruncated = true;
                        continue;
                    }

                    // The path is still what the result is presented and sorted by;
                    // it is simply no longer what identity is decided by, and it is
                    // now built only for the items that are actually returned. The
                    // source file is read here for the same reason, unless the filter
                    // already needed it above.
                    if (sourceFile == null)
                        sourceFile = TryGetSourceFile(item) ?? string.Empty;

                    matches.Add(new SpatialMatch(item, BuildItemPath(item), sourceFile, box));
                }
            }

            matches.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
            response.ReturnedItemCount = matches.Count;
            if (matches.Count > 0)
            {
                response.MatchHandle = sessionStore.Add(matches.Select(match => match.Item).ToList());
                response.Preview = matches.Take(previewLimit).Select(BuildSpatialPreviewItem).ToList();
            }

            // "Narrow the zone" is not on this list, and used to be. The zone is read
            // only by MatchesSpatialBox, after an item has been scanned and its box
            // computed, so it cannot reduce scannedItemCount: a caller that followed
            // that advice narrowed the zone, hit the identical truncation, and had no
            // way to tell that the answer was still partial for the same reason.
            //
            // sourceFileContains used to be off it too, for the same reason. It now prunes
            // at the model root as well as filtering per item, so it is the one lever that
            // reduces the scan rather than only extending the cap -- which is why the
            // warning names it, and names what it does, instead of listing every input.
            if (response.TraversalTruncated)
            {
                response.Warnings.Add(
                    "Traversal stopped at " + response.ScannedItemCount.ToString(CultureInfo.InvariantCulture) +
                    " scanned items, so this answer is partial and items outside it were never examined. " +
                    "Raise maxScannedItems (maximum " + SpatialSearchOptionsHelper.MaxMaxScannedItems.ToString(CultureInfo.InvariantCulture) +
                    "); a " + MaxSpatialSearchMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " ms budget stops the traversal after that. To scan less rather than allow more, " +
                    "set sourceFileContains: it skips whole appended models before their items are counted" +
                    (response.PrunedModelCount > 0
                        ? " (" + response.PrunedModelCount.ToString(CultureInfo.InvariantCulture) + " skipped on this call)"
                        : string.Empty) +
                    ". Narrowing the zone does NOT help inside a model: the zone is read after each item " +
                    "is counted, so every item in a scanned model is scanned either way.");
            }
            if (response.ResultsTruncated)
                response.Warnings.Add("Result limit reached; narrow the zone or increase maxResults up to the documented maximum.");

            Logger.Info(
                "find_items_by_bbox mode=" + matchMode + " scanned=" + response.ScannedItemCount + " matched=" + response.MatchedItemCount + " returned=" + response.ReturnedItemCount + " traversal_truncated=" + response.TraversalTruncated + " results_truncated=" + response.ResultsTruncated + " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");
            return response;
        }

        /// <summary>
        /// The items worth scanning, model by model, skipping whole models that cannot hold
        /// a match. A skipped model contributes nothing to `scannedItemCount`, which is the
        /// point: every other filter in this method runs after the counter has already
        /// counted the item.
        /// </summary>
        private static IEnumerable<ModelItem> EnumerateCandidateItems(
            Document document,
            FindItemsByBboxRequest request,
            string sourceFileContains,
            FindItemsByBboxResponse response)
        {
            foreach (Model model in document.Models)
            {
                if (model == null)
                    continue;

                var rootItem = model.RootItem;
                if (rootItem == null)
                    continue;

                if (!SpatialModelPruning.ModelFileCanSatisfyFilter(TryGetModelSourceFile(model, response), sourceFileContains))
                {
                    response.PrunedModelCount++;
                    continue;
                }

                // Read once: the reader walks the model and can warn, so asking twice would
                // pay for it twice and could report the same failure twice.
                var modelBox = TryGetModelBox(model, response);
                if (!SpatialModelPruning.ModelExtentsCanHoldAMatch(
                        ToSpatialPoint(modelBox, takeMin: true),
                        ToSpatialPoint(modelBox, takeMin: false),
                        request.Min,
                        request.Max))
                {
                    response.PrunedModelCount++;
                    continue;
                }

                foreach (ModelItem item in rootItem.DescendantsAndSelf)
                    yield return item;
            }
        }

        // Both readers fail open. A model whose extents or file cannot be read is scanned,
        // because "we could not tell" must not be recorded as "nothing here" -- the whole
        // value of a prune is that it is provably empty, and an unreadable model is not.
        private static BoundingBox3D TryGetModelBox(Model model, FindItemsByBboxResponse response)
        {
            try
            {
                return model.RootItem == null ? null : model.RootItem.BoundingBox();
            }
            catch (Exception ex)
            {
                if (response.Warnings.Count < 10)
                    response.Warnings.Add("Could not read a model's extents, so it was scanned rather than skipped: " + ex.Message);
                return null;
            }
        }

        private static string TryGetModelSourceFile(Model model, FindItemsByBboxResponse response)
        {
            try
            {
                return model.SourceFileName;
            }
            catch (Exception ex)
            {
                // Said out loud, not swallowed. The contract promises that a model whose file
                // name cannot be read is scanned rather than skipped *and* that the caller is
                // told, because the alternative is a silently slower call with no explanation
                // for why a source-file filter pruned nothing.
                if (response.Warnings.Count < 10)
                    response.Warnings.Add("Could not read a model's source file, so it was scanned rather than skipped: " + ex.Message);
                return null;
            }
        }

        private static SpatialPoint ToSpatialPoint(BoundingBox3D box, bool takeMin)
        {
            if (box == null)
                return null;

            var point = takeMin ? box.Min : box.Max;
            return new SpatialPoint { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static bool MatchesSpatialBox(BoundingBox3D item, SpatialPoint min, SpatialPoint max, string matchMode)
        {
            if (string.Equals(matchMode, SpatialSearchOptionsHelper.Contains, StringComparison.OrdinalIgnoreCase))
            {
                return item.Min.X >= min.X && item.Max.X <= max.X &&
                       item.Min.Y >= min.Y && item.Max.Y <= max.Y &&
                       item.Min.Z >= min.Z && item.Max.Z <= max.Z;
            }

            if (string.Equals(matchMode, SpatialSearchOptionsHelper.Center, StringComparison.OrdinalIgnoreCase))
            {
                var center = item.Center;
                return center.X >= min.X && center.X <= max.X &&
                       center.Y >= min.Y && center.Y <= max.Y &&
                       center.Z >= min.Z && center.Z <= max.Z;
            }

            return item.Min.X <= max.X && item.Max.X >= min.X &&
                   item.Min.Y <= max.Y && item.Max.Y >= min.Y &&
                   item.Min.Z <= max.Z && item.Max.Z >= min.Z;
        }

        private static SpatialSearchItem BuildSpatialPreviewItem(SpatialMatch match)
        {
            return new SpatialSearchItem
            {
                DisplayName = match.Item.DisplayName,
                Path = match.Path,
                SourceFile = match.SourceFile,
                Min = new SpatialPoint { X = match.Box.Min.X, Y = match.Box.Min.Y, Z = match.Box.Min.Z },
                Max = new SpatialPoint { X = match.Box.Max.X, Y = match.Box.Max.Y, Z = match.Box.Max.Z },
            };
        }

        private sealed class SpatialMatch
        {
            public SpatialMatch(ModelItem item, string path, string sourceFile, BoundingBox3D box)
            {
                Item = item;
                Path = path;
                SourceFile = sourceFile;
                Box = box;
            }

            public ModelItem Item { get; private set; }
            public string Path { get; private set; }
            public string SourceFile { get; private set; }
            public BoundingBox3D Box { get; private set; }
        }
    }
}
