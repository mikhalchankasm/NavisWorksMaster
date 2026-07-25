using System;
using System.Collections.Generic;
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
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    if (!includeContainers && item.Children != null && item.Children.Count() > 0)
                        continue;

                    var sourceFile = TryGetSourceFile(item) ?? string.Empty;
                    if (!string.IsNullOrEmpty(sourceFileContains) &&
                        sourceFile.IndexOf(sourceFileContains, StringComparison.OrdinalIgnoreCase) < 0)
                    {
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

                    var path = BuildItemPath(item);
                    if (!seenPaths.Add(path))
                        continue;

                    response.MatchedItemCount++;
                    if (matches.Count >= maxResults)
                    {
                        response.ResultsTruncated = true;
                        continue;
                    }

                    matches.Add(new SpatialMatch(item, path, sourceFile, box));
                }
            }

            matches.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path));
            response.ReturnedItemCount = matches.Count;
            if (matches.Count > 0)
            {
                response.MatchHandle = sessionStore.Add(matches.Select(match => match.Item).ToList());
                response.Preview = matches.Take(previewLimit).Select(BuildSpatialPreviewItem).ToList();
            }

            if (response.TraversalTruncated)
                response.Warnings.Add("Traversal stopped at the safety limit; narrow the zone, sourceFileContains, or maxScannedItems.");
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
