using System;
using System.Collections.Generic;
using System.Text;
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
            // .rvm appended to an .nwd that is the .rvm, not the document root. The
            // answer is cached per ancestor by item identity (ModelItem.Equals compares
            // the native object), because two appended files can share a display name
            // and so a printed path. The cached ancestors are released with the page.
            var sourceFilesByItem = includeSourceFiles
                ? new Dictionary<ModelItem, string>()
                : null;
            var pageBytes = 0L;
            var index = 0;
            try
            {
                foreach (var item in items)
                {
                    if (item == null)
                        continue;

                    if (index >= page.Offset)
                    {
                        var info = BuildItem(index, item, includePaths, sourceFilesByItem);
                        pageBytes += EstimateBytes(info);
                        if (response.Items.Count > 0 && pageBytes > MatchHandleItemsPaging.MaxPageBytes)
                        {
                            // Past the pipe frame the transport would trim rows after
                            // nextOffset is set; stop here so the counts stay true.
                            var cut = MatchHandleItemsPaging.Shorten(page, response.Items.Count);
                            response.ReturnedItemCount = cut.ReturnedItemCount;
                            response.NextOffset = cut.NextOffset;
                            response.HasMore = cut.HasMore;
                            response.SizeLimited = true;
                            break;
                        }

                        response.Items.Add(info);
                        if (response.Items.Count == page.ReturnedItemCount)
                            break;
                    }

                    index++;
                }
            }
            finally
            {
                if (sourceFilesByItem != null)
                {
                    foreach (var ancestor in sourceFilesByItem.Keys)
                        DisposeWrapper(ancestor);
                }
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
            IDictionary<ModelItem, string> sourceFilesByItem)
        {
            var info = new MatchHandleItemInfo
            {
                Index = index,
                DisplayName = GetItemDisplayName(item),
                ClassDisplayName = item.ClassDisplayName ?? string.Empty,
                Path = string.Empty,
                SourceFile = string.Empty,
            };

            if (!includePaths && sourceFilesByItem == null)
                return info;

            var chain = BuildItemChain(item);
            var cached = new List<ModelItem>();
            try
            {
                if (includePaths)
                    info.Path = ItemChainPaths.Build(BuildChainNames(chain))[chain.Count - 1];
                if (sourceFilesByItem != null)
                    info.SourceFile = GetSourceFileCached(sourceFilesByItem, chain, cached);
            }
            finally
            {
                // The ancestors are fresh Parent wrappers this call owns (ARCHITECTURE.md,
                // TECH-W13 disposal rules). The top one is left alone because it may be the
                // cached model root, the last one is the handle's own item, and the ones the
                // source-file cache keeps as keys are released with the cache.
                for (var depth = 1; depth < chain.Count - 1; depth++)
                {
                    if (!cached.Exists(kept => ReferenceEquals(kept, chain[depth])))
                        DisposeWrapper(chain[depth]);
                }
            }

            return info;
        }

        private static long EstimateBytes(MatchHandleItemInfo info)
        {
            // UTF-8 text of the four strings plus the field names and numbers around them.
            return 128L
                + Encoding.UTF8.GetByteCount(info.DisplayName ?? string.Empty)
                + Encoding.UTF8.GetByteCount(info.ClassDisplayName ?? string.Empty)
                + Encoding.UTF8.GetByteCount(info.Path ?? string.Empty)
                + Encoding.UTF8.GetByteCount(info.SourceFile ?? string.Empty);
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
            IDictionary<ModelItem, string> sourceFilesByItem,
            IList<ModelItem> chain,
            ICollection<ModelItem> cached)
        {
            // Climb from the item until a node has the property or a cached answer.
            // Every ancestor passed on the way has no property of its own, so it shares
            // the answer found above it. Neither the handle's own item nor the top of
            // the chain becomes a key: the cache's keys are disposed with it.
            var passed = new List<ModelItem>();
            string sourceFile = null;
            for (var depth = chain.Count - 1; depth >= 0 && sourceFile == null; depth--)
            {
                var node = chain[depth];
                var isAncestor = depth > 0 && depth < chain.Count - 1;
                if (isAncestor && sourceFilesByItem.TryGetValue(node, out sourceFile))
                    break;

                if (isAncestor)
                    passed.Add(node);
                var property = NativePropertyLookup.FindSourceFileProperty(node);
                if (property != null)
                    sourceFile = GetPropertyDisplayValue(property);
            }

            sourceFile = sourceFile ?? string.Empty;
            foreach (var node in passed)
            {
                sourceFilesByItem[node] = sourceFile;
                cached.Add(node);
            }

            return sourceFile;
        }

        private static void DisposeWrapper(ModelItem item)
        {
            // An interop call that can throw on a torn-down document; cleanup must
            // never become the failure the caller sees.
            if (item == null)
                return;
            try { item.Dispose(); } catch (Exception) { }
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
