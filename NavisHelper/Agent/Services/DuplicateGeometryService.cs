using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed class DuplicateGeometryService
    {
        private const int MaxScannedItems = 200000;
        private const int MaxHandleGroups = 25;

        public FindDuplicateGeometryResponse Find(Document document, FindDuplicateGeometryRequest request,
            MatchSessionStore sessionStore)
        {
            request = request ?? new FindDuplicateGeometryRequest();
            string mode;
            if (!DuplicateGeometryModes.TryNormalize(request.Mode, out mode))
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    "Mode must be all, within_root or across_roots.");
            var targets = ResolveTargets(document, request, sessionStore);
            var maxGroups = Math.Max(1, Math.Min(100, request.MaxGroups ?? 50));
            var previewPerGroup = Math.Max(1, Math.Min(20, request.PreviewPerGroup ?? 5));
            var response = new FindDuplicateGeometryResponse
            {
                GridSize = DuplicateGeometryRules.ClampGrid(request.GridSize),
                Mode = mode,
            };
            var items = new List<ModelItem>();
            var candidates = new List<DuplicateGeometryCandidate>();
            var seen = new HashSet<ModelItem>();
            var nodeKeys = new Dictionary<ModelItem, string>();
            foreach (var item in EnumerateScope(targets))
            {
                var deadline = HostRequestDeadline.Current;
                if (deadline != null && deadline.Expired)
                {
                    response.ScanTruncated = true;
                    response.Warnings.Add("Geometry scan stopped at the host request deadline; results cover scanned items only.");
                    break;
                }
                if (!item.HasGeometry || seen.Contains(item))
                    continue;
                if (items.Count >= MaxScannedItems)
                {
                    response.ScanTruncated = true;
                    response.Warnings.Add("Geometry scan limited to 200000 items; narrow the scope for complete results.");
                    break;
                }
                seen.Add(item);
                candidates.Add(ReadCandidate(item, items.Count, nodeKeys));
                items.Add(item);
            }
            response.ScannedItemCount = candidates.Count;
            var groups = DuplicateGeometryRules.Group(candidates, response.GridSize, mode, maxGroups + 1);
            response.GroupsTruncated = groups.Count > maxGroups;
            foreach (var group in groups.Take(maxGroups))
            {
                var groupItems = group.ItemIndexes.Select(index => items[index]).ToList();
                response.Groups.Add(new DuplicateGeometryGroupInfo
                {
                    ItemCount = groupItems.Count,
                    RootCount = group.RootCount,
                    SameParentAndName = group.SameParentAndName,
                    MatchHandle = response.Groups.Count < MaxHandleGroups ? sessionStore.Add(groupItems) : null,
                    PreviewPaths = groupItems.Take(previewPerGroup).Select(SearchService.PrintItemPath).ToList(),
                });
                response.DuplicateItemCount += groupItems.Count;
            }
            response.GroupCount = response.Groups.Count;
            if (response.GroupCount > MaxHandleGroups)
                response.Warnings.Add("Match handles were issued for the first 25 groups only; narrow the scope or the mode for handles to the rest.");
            return response;
        }

        private static List<ModelItem> ResolveTargets(Document document, FindDuplicateGeometryRequest request,
            MatchSessionStore sessionStore)
        {
            var error = ItemAppearanceRules.DescribeTargetError(request.MatchHandles, request.UseSelection);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var targets = new List<ModelItem>();
            if (request.UseSelection == true)
                targets.AddRange(document.CurrentSelection.SelectedItems);
            else
            {
                foreach (var handle in request.MatchHandles.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    IList<ModelItem> resolved;
                    if (!sessionStore.TryGet(handle, out resolved) || resolved == null)
                        throw new AgentCommandException(ErrorCodes.StaleMatchReference,
                            "matchHandle is stale or was not found: " + handle + ". Re-run find_items and retry.");
                    targets.AddRange(resolved);
                }
            }
            return targets;
        }

        private static IEnumerable<ModelItem> EnumerateScope(IEnumerable<ModelItem> targets)
        {
            // A target whose ancestor is also a target is already inside that ancestor's walk;
            // walking it again made nested selections cost quadratic node visits (#362 review).
            var distinct = new HashSet<ModelItem>(targets.Where(item => item != null));
            foreach (var target in distinct.Where(item => !HasAncestorIn(item, distinct)))
            {
                // Yield structural nodes too so the caller checks its deadline during sparse walks.
                yield return target;
                if (!target.HasGeometry)
                    foreach (var item in target.Descendants)
                        yield return item;
            }
        }

        private static bool HasAncestorIn(ModelItem item, HashSet<ModelItem> set)
        {
            for (var ancestor = item.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (set.Contains(ancestor))
                    return true;
            return false;
        }

        // Keyed by the node itself (ModelItem equality is native identity), not by its printed
        // path: two roots appended from the same file print the same path but are different
        // roots, and copies across them are exactly what across_roots must find (#362 review).
        private static string NodeKey(ModelItem node, Dictionary<ModelItem, string> keys)
        {
            if (node == null)
                return string.Empty;
            string key;
            if (!keys.TryGetValue(node, out key))
            {
                key = "n" + keys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                keys.Add(node, key);
            }
            return key;
        }

        private static DuplicateGeometryCandidate ReadCandidate(ModelItem item, int index, Dictionary<ModelItem, string> nodeKeys)
        {
            var geometry = item.Geometry;
            BoundingBox3D box;
            try { box = geometry.BoundingBox; }
            catch { box = null; }
            if (box == null || box.IsEmpty)
            {
                if (box != null)
                    box.Dispose();
                box = item.BoundingBox();
            }
            using (box)
            {
                var min = box.Min;
                var max = box.Max;
                var parent = item.Parent;
                var root = item;
                for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                    root = ancestor;
                return new DuplicateGeometryCandidate
                {
                    Index = index,
                    RootKey = NodeKey(root, nodeKeys),
                    ParentKey = NodeKey(parent, nodeKeys),
                    DisplayName = item.DisplayName,
                    MinX = min.X, MinY = min.Y, MinZ = min.Z,
                    MaxX = max.X, MaxY = max.Y, MaxZ = max.Z,
                    PrimitiveCount = (int)geometry.PrimitiveCount,
                };
            }
        }
    }
}
