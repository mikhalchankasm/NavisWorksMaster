using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// Pages through the items a match handle holds, without touching the selection.
    ///
    /// The handle's item list already exists in <see cref="MatchSessionStore"/>, so
    /// this reads it rather than searching again: no traversal, no native query, and
    /// no document state changes. Paging arithmetic lives in
    /// <see cref="MatchHandleItemsPaging"/>.
    /// </summary>
    internal sealed class MatchHandleItemsService
    {
        private readonly MatchSessionStore _sessionStore;

        public MatchHandleItemsService(MatchSessionStore sessionStore)
        {
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            _sessionStore = sessionStore;
        }

        public MatchHandleItemsResponse GetItems(MatchHandleItemsRequest request)
        {
            request = request ?? new MatchHandleItemsRequest();
            var handle = (request.MatchHandle ?? string.Empty).Trim();
            if (handle.Length == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "matchHandle is required.");

            IList<ModelItem> items;
            string reason;
            if (!_sessionStore.TryGet(handle, out items, out reason) || items == null)
            {
                if (string.IsNullOrEmpty(reason))
                    reason = "This match handle contains no items.";
                throw new AgentCommandException(
                    ErrorCodes.StaleMatchReference,
                    "matchHandle is stale or was not found. " + reason + " Re-run find_items/find_items_by_bbox and retry.");
            }

            var includePaths = request.IncludePaths.GetValueOrDefault(true);
            var includeSourceFiles = request.IncludeSourceFiles.GetValueOrDefault(false);
            var page = MatchHandleItemsPaging.Plan(request.Offset, request.Limit, CountItems(items));
            var response = new MatchHandleItemsResponse
            {
                MatchHandle = handle,
                TotalItemCount = page.TotalItemCount,
                Offset = page.Offset,
                Limit = page.Limit,
                ReturnedItemCount = page.ReturnedItemCount,
                NextOffset = page.NextOffset,
                HasMore = page.HasMore,
            };

            if (page.ReturnedItemCount == 0)
                return response;

            // One source-file read per root path rather than per item: the property
            // lives on the model node, so a page of one appended file would otherwise
            // climb to the same root for every item. The key is the printed root path,
            // as in selected_items_tree, and it decides only a reported string -- which
            // item is on the page is decided by the handle's own list order, so two
            // roots sharing a display name cannot drop or merge a row here.
            var sourceFilesByRootPath = includeSourceFiles
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : null;
            var index = 0;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                if (index >= page.Offset)
                {
                    response.Items.Add(BuildItem(index, item, includePaths, includeSourceFiles, sourceFilesByRootPath));
                    if (response.Items.Count == page.ReturnedItemCount)
                        break;
                }

                index++;
            }

            return response;
        }

        private static int CountItems(IList<ModelItem> items)
        {
            var count = 0;
            foreach (var item in items)
            {
                if (item != null)
                    count++;
            }

            return count;
        }

        private static MatchHandleItemInfo BuildItem(
            int index,
            ModelItem item,
            bool includePaths,
            bool includeSourceFiles,
            IDictionary<string, string> sourceFilesByRootPath)
        {
            var info = new MatchHandleItemInfo
            {
                Index = index,
                DisplayName = GetItemDisplayName(item),
                ClassDisplayName = item.ClassDisplayName ?? string.Empty,
                Path = string.Empty,
                SourceFile = string.Empty,
            };

            if (!includePaths && !includeSourceFiles)
                return info;

            var chain = BuildItemChain(item);
            var chainPaths = ItemChainPaths.Build(BuildChainNames(chain));
            if (includePaths)
                info.Path = chainPaths[chain.Count - 1];
            if (includeSourceFiles)
                info.SourceFile = GetSourceFileCached(sourceFilesByRootPath, chainPaths[0], chain[0]);

            return info;
        }

        private static List<ModelItem> BuildItemChain(ModelItem item)
        {
            var chain = new List<ModelItem>();
            var current = item == null ? null : item.Parent;

            while (current != null)
            {
                chain.Add(current);
                current = current.Parent;
            }

            chain.Reverse();
            if (item != null)
                chain.Add(item);

            return chain;
        }

        private static List<string> BuildChainNames(IList<ModelItem> chainItems)
        {
            var names = new List<string>(chainItems.Count);
            foreach (var chainItem in chainItems)
                names.Add(GetItemDisplayName(chainItem));

            return names;
        }

        private static string GetItemDisplayName(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.ClassDisplayName ?? string.Empty
                : item.DisplayName;
        }

        private static string GetSourceFileCached(
            IDictionary<string, string> sourceFilesByRootPath,
            string rootPath,
            ModelItem rootItem)
        {
            string sourceFile;
            if (sourceFilesByRootPath.TryGetValue(rootPath, out sourceFile))
                return sourceFile;

            sourceFile = TryGetSourceFile(rootItem);
            sourceFilesByRootPath[rootPath] = sourceFile;
            return sourceFile;
        }

        private static string TryGetSourceFile(ModelItem item)
        {
            var current = item;
            while (current != null)
            {
                var sourceFileProperty = NativePropertyLookup.FindSourceFileProperty(current);
                if (sourceFileProperty != null)
                    return GetPropertyDisplayValue(sourceFileProperty);

                current = current.Parent;
            }

            return string.Empty;
        }

        private static string GetPropertyDisplayValue(DataProperty property)
        {
            if (property == null)
                return string.Empty;

            var value = property.Value;
            if (value == null)
                return string.Empty;

            try
            {
                return value.ToDisplayString();
            }
            catch
            {
                return value.ToString();
            }
        }
    }
}  
