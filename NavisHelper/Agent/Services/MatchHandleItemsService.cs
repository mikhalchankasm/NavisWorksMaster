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

            // An item's source file is the nearest file node at or above it: under an
            // .rvm appended to an .nwd that is the .rvm, not the document root. Each
            // ancestor is looked up once per call, keyed by its printed path; that key
            // decides only a reported string -- which item is on the page is decided
            // by the handle's own list order, so no row can be dropped or merged.
            var sourceFilesByPath = includeSourceFiles
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : null;
            var index = 0;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                if (index >= page.Offset)
                {
                    response.Items.Add(BuildItem(index, item, includePaths, includeSourceFiles, sourceFilesByPath));
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
            IDictionary<string, string> sourceFilesByPath)
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
                info.SourceFile = GetSourceFileCached(sourceFilesByPath, chain, chainPaths);

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
            IDictionary<string, string> sourceFilesByPath,
            IList<ModelItem> chain,
            IReadOnlyList<string> chainPaths)
        {
            // Climb from the item until a node has the property or a cached answer;
            // every node passed on the way has no property of its own, so it shares
            // the answer found above it.
            var passed = new List<string>();
            string sourceFile = null;
            for (var depth = chain.Count - 1; depth >= 0 && sourceFile == null; depth--)
            {
                if (sourceFilesByPath.TryGetValue(chainPaths[depth], out sourceFile))
                    break;

                passed.Add(chainPaths[depth]);
                var property = NativePropertyLookup.FindSourceFileProperty(chain[depth]);
                if (property != null)
                    sourceFile = GetPropertyDisplayValue(property);
            }

            sourceFile = sourceFile ?? string.Empty;
            foreach (var path in passed)
                sourceFilesByPath[path] = sourceFile;

            return sourceFile;
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
