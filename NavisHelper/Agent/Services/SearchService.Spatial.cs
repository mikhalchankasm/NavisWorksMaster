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
            var seenItems = new HashSet<ModelItem>();

            if (document.Models != null)
            {
                // Per model rather than over document.Models.RootItemDescendantsAndSelf, so
                // that a model ruled out below is never enumerated. The flat enumeration
                // could only be filtered item by item, which is the reason this tool had no
                // lever except raising maxScannedItems: every filter ran after the counter.
                foreach (ModelItem modelRoot in EnumerateCandidateModels(document, request, sourceFileContains, response))
                {
                    // The walk is per subtree now, not per item. `isolate_by_box` always
                    // skipped an item whose box missed the zone, and Autodesk defines
                    // BoundingBox() as the box of the item *and its children*, so such a
                    // subtree is provably empty in every match mode. The walker asks, after
                    // this loop body has run for an item, whether to skip that item's
                    // children -- so a plain `continue` below means "keep descending", and
                    // only the prune sets the flag.
                    var skipThisSubtree = false;
                    foreach (ModelItem item in SpatialSubtreeWalk.Walk(modelRoot, ChildItems, node => skipThisSubtree))
                    {
                        skipThisSubtree = false;

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

                        // The box is read before the container check, and for containers
                        // too, because it is now the prune test: a parent box that misses
                        // the zone rules out every descendant, so the walk need not
                        // enumerate them. The cost of reading container boxes that used
                        // to be skipped is the price of the prune, and it is measured on
                        // the rig -- a zone that spans the site prunes nothing and pays
                        // it everywhere.
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

                        // The same mode-independent test that rules out whole models,
                        // read on the item's own box: it encloses the item's children,
                        // so non-overlap rules out every mode for the whole subtree. An
                        // unreadable (null) box fails open here -- the item does not
                        // match below, but its children are still walked.
                        if (!SpatialModelPruning.ModelExtentsCanHoldAMatch(
                                ToSpatialPoint(box, takeMin: true),
                                ToSpatialPoint(box, takeMin: false),
                                request.Min,
                                request.Max))
                        {
                            response.OutsideItemCount++;
                            skipThisSubtree = true;
                            continue;
                        }

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

                        if (box == null || !MatchesSpatialBox(box, request.Min, request.Max, matchMode))
                            continue;

                        response.MatchedItemCount++;
                        if (matches.Count >= maxResults)
                        {
                            response.ResultsTruncated = true;
                            continue;
                        }

                        // ModelItem identity avoids merging distinct leaves with the same path.
                        // Only returned items enter the set, bounding it by maxResults and
                        // avoiding retention of native item wrappers for later zone matches.
                        if (!seenItems.Add(item))
                            continue;

                        // The path is still what the result is presented and sorted by;
                        // it is simply no longer what identity is decided by, and it is
                        // now built only for the items that are actually returned. The
                        // source file is read here for the same reason, unless the filter
                        // already needed it above.
                        if (sourceFile == null)
                            sourceFile = TryGetSourceFile(item) ?? string.Empty;

                        matches.Add(new SpatialMatch(item, BuildItemPath(item), sourceFile, box));
                    }

                    if (response.TraversalTruncated)
                        break;
                }
            }

            matches.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
            response.ReturnedItemCount = matches.Count;
            if (matches.Count > 0)
            {
                response.MatchHandle = sessionStore.Add(matches.Select(match => match.Item).ToList());
                response.Preview = matches.Take(previewLimit).Select(BuildSpatialPreviewItem).ToList();
            }

            // "Narrow the zone" is a lever again, and that is new. It still cannot make
            // any single item cheaper -- the zone is read after the item is counted --
            // but the walk now stops at an item whose own box misses the zone, so a
            // tighter zone reaches fewer items at all.
            //
            // sourceFileContains prunes whatever the geometry: a zone that spans the
            // site prunes nothing inside a model and pays one extra box read per
            // container, while the file filter rules out whole appended models
            // unconditionally.
            if (response.TraversalTruncated)
            {
                response.Warnings.Add(
                    "Traversal stopped at " + response.ScannedItemCount.ToString(CultureInfo.InvariantCulture) +
                    " scanned items, so this answer is partial and items outside it were never examined. " +
                    "Raise maxScannedItems (maximum " + SpatialSearchOptionsHelper.MaxMaxScannedItems.ToString(CultureInfo.InvariantCulture) +
                    "); a " + MaxSpatialSearchMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " ms budget stops the traversal after that. To scan less rather than allow more, narrow the zone" +
                    (response.OutsideItemCount > 0
                        ? " (" + response.OutsideItemCount.ToString(CultureInfo.InvariantCulture) + " items outside the zone were not descended into on this call)"
                        : ": items whose boxes miss it are not descended into") +
                    ", or set sourceFileContains: it skips whole appended models before their items are counted" +
                    (response.PrunedModelCount > 0
                        ? " (" + response.PrunedModelCount.ToString(CultureInfo.InvariantCulture) + " models skipped on this call)"
                        : string.Empty) +
                    ".");
            }
            if (response.ResultsTruncated)
                response.Warnings.Add("Result limit reached; narrow the zone or increase maxResults up to the documented maximum.");

            Logger.Info(
                "find_items_by_bbox mode=" + matchMode + " scanned=" + response.ScannedItemCount + " matched=" + response.MatchedItemCount + " returned=" + response.ReturnedItemCount + " traversal_truncated=" + response.TraversalTruncated + " results_truncated=" + response.ResultsTruncated + " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");
            return response;
        }

        /// <summary>
        /// The root item of each model worth scanning, model by model, skipping whole
        /// models that cannot hold a match. A skipped model contributes nothing to
        /// `scannedItemCount`, which is the point: every other filter in this method
        /// runs after the counter has already counted the item. The subtree walk over
        /// each root is the caller's -- it owns the per-item prune decision.
        /// </summary>
        private static IEnumerable<ModelItem> EnumerateCandidateModels(
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

                yield return rootItem;
            }
        }

        // The walker's children delegate. A null Children collection reads as "no
        // children": the walker drops it without enumerating, the same way it never
        // asks for the children of a skipped item.
        private static IEnumerable<ModelItem> ChildItems(ModelItem item)
        {
            return item.Children;
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
