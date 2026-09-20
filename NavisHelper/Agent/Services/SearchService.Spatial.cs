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
                foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
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
            // sourceFileContains is not on it either. It makes each skipped item
            // cheaper, but the cap counts scanned items and the counter increments
            // before every filter, so it does not let a call reach further into the
            // model. Raising maxScannedItems is the only lever that extends coverage,
            // and the 10 second budget is the next wall behind it.
            if (response.TraversalTruncated)
            {
                response.Warnings.Add(
                    "Traversal stopped at " + response.ScannedItemCount.ToString(CultureInfo.InvariantCulture) +
                    " scanned items, so this answer is partial and items outside it were never examined. " +
                    "Raise maxScannedItems (maximum " + SpatialSearchOptionsHelper.MaxMaxScannedItems.ToString(CultureInfo.InvariantCulture) +
                    "); a " + MaxSpatialSearchMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " ms budget stops the traversal after that. Narrowing the zone does NOT help: the zone filters " +
                    "results, not the scan, so every item is scanned either way.");
            }
            if (response.ResultsTruncated)
                response.Warnings.Add("Result limit reached; narrow the zone or increase maxResults up to the documented maximum.");

            Logger.Info(
                "find_items_by_bbox mode=" + matchMode + " scanned=" + response.ScannedItemCount + " matched=" + response.MatchedItemCount + " returned=" + response.ReturnedItemCount + " traversal_truncated=" + response.TraversalTruncated + " results_truncated=" + response.ResultsTruncated + " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");
            return response;
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
