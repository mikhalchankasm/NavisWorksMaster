using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        private ModelItem ResolveListChildrenParent(Document document, ListItemChildrenRequest request, MatchSessionStore sessionStore)
        {
            var parentMatchHandle = (request.ParentMatchHandle ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(parentMatchHandle))
                return ResolveListChildrenParentByHandle(parentMatchHandle, sessionStore);

            return ResolveListChildrenParentWithoutHandle(document, request);
        }

        private static ModelItem ResolveListChildrenParentByHandle(string parentMatchHandle, MatchSessionStore sessionStore)
        {
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            IList<ModelItem> items;
            if (!sessionStore.TryGet(parentMatchHandle, out items) || items == null || items.Count == 0)
                throw new AgentCommandException(ErrorCodes.StaleMatchReference, "parentMatchHandle is stale or was not found. Re-run find_items/list_item_children and retry.");

            // Group by item identity, not by the display-name path: a handle can
            // legitimately hold two distinct siblings that share a name, and that
            // is an ambiguous parent rather than one parent.
            var distinct = items
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (distinct.Count != 1)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "parentMatchHandle must resolve to exactly one parent item; it resolved to " + distinct.Count.ToString(CultureInfo.InvariantCulture) + ".");

            return distinct[0];
        }

        private ModelItem ResolveListChildrenParentWithoutHandle(Document document, ListItemChildrenRequest request)
        {
            var parentPath = (request.ParentPath ?? string.Empty).Trim();
            var parentName = (request.ParentName ?? string.Empty).Trim();
            var sourceFile = (request.SourceFile ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(parentPath) && string.IsNullOrWhiteSpace(parentName) && string.IsNullOrWhiteSpace(sourceFile))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "parentPath, parentName, or sourceFile is required.");

            var comparison = NormalizeComparison(request.Comparison);
            if (!string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "comparison must be equals, contains, or wildcard.");
            }

            List<ModelItem> matches;
            if (!string.IsNullOrWhiteSpace(parentPath))
            {
                if (!string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "list_item_children parentPath uses fast exact path traversal only. Use comparison=equals, or find the parent with find_items and pass parentMatchHandle.");

                matches = ResolveListChildrenParentsByPath(document, parentPath);
            }
            else
            {
                matches = ResolveListChildrenParentsFromRootIndex(document, parentName, sourceFile, comparison);
            }

            if (matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Parent item was not found by fast path. For nested/ambiguous nodes, first call find_items and pass parentMatchHandle.");

            if (matches.Count > 1)
            {
                var preview = string.Join("; ", matches.Take(10).Select(BuildItemPath).ToArray());
                throw new AgentCommandException(ErrorCodes.CommandFailed, "More than one parent item matched. Use a more specific parentPath or pass parentMatchHandle from find_items. Matches: " + preview);
            }

            return matches[0];
        }

        private static List<ModelItem> ResolveListChildrenParentsByPath(Document document, string parentPath)
        {
            var result = new List<ModelItem>();
            if (document == null || document.Models == null)
                return result;

            var printed = (parentPath ?? string.Empty).Trim();
            if (printed.Length == 0)
                return result;

            // A path containing the printed separator is resolved against the tree
            // one node at a time, because a DisplayName may itself contain " / " and
            // splitting up front would guess the boundaries wrong. A path without it
            // is a caller-authored slash path and keeps the old segment behaviour.
            var segments = printed.IndexOf(FindItemsPathSegments.Separator, StringComparison.Ordinal) >= 0
                ? null
                : FindItemsPathSegments.SplitSlashPath(printed).ToList();
            if (segments != null && segments.Count == 0)
                return result;

            var seen = new HashSet<ModelItem>();
            foreach (Model model in document.Models)
            {
                if (model == null || model.RootItem == null)
                    continue;

                AddResolvedPathCandidate(result, seen, ResolveFrom(model.RootItem, printed, segments));
                foreach (ModelItem child in model.RootItem.Children)
                    AddResolvedPathCandidate(result, seen, ResolveFrom(child, printed, segments));
            }

            return result;
        }

        private List<ModelItem> ResolveListChildrenParentsFromRootIndex(Document document, string parentName, string sourceFile, string comparison)
        {
            var result = new List<ModelItem>();
            if (document == null)
                return result;

            var index = GetRootSearchIndex(document);
            var seen = new HashSet<ModelItem>();
            foreach (var candidate in index.Candidates)
            {
                if (candidate == null || candidate.Item == null)
                    continue;

                var matched = false;
                if (!string.IsNullOrWhiteSpace(parentName))
                    matched = RootCandidateMatches(candidate, parentName, comparison);
                if (!matched && !string.IsNullOrWhiteSpace(sourceFile))
                    matched = RootCandidateMatches(candidate, sourceFile, comparison);
                if (matched)
                    AddResolvedPathCandidate(result, seen, candidate.Item);
            }

            return result;
        }

        private static ModelItem ResolveFrom(ModelItem start, string printedPath, IList<string> segments)
        {
            return segments == null
                ? TryResolvePrintedPath(start, printedPath)
                : TryResolveChildPath(start, segments, 0);
        }

        /// <summary>
        /// Resolves a printed path by consuming one node at a time, so a node whose
        /// own name contains the separator resolves instead of being split in two.
        /// </summary>
        private static ModelItem TryResolvePrintedPath(ModelItem start, string remainingPath)
        {
            if (start == null || string.IsNullOrEmpty(remainingPath))
                return null;

            var rest = FindItemsPathSegments.TryConsume(ItemPathCandidateNames(start), remainingPath);
            if (rest == null)
                return null;
            if (rest.Length == 0)
                return start;

            foreach (ModelItem child in start.Children)
            {
                var resolved = TryResolvePrintedPath(child, rest);
                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        /// <summary>
        /// Every name a path segment may legitimately address for this item. Kept in
        /// step with ItemNameMatchesSegment, which the segment path still uses.
        /// </summary>
        private static IEnumerable<string> ItemPathCandidateNames(ModelItem item)
        {
            if (item == null)
                yield break;

            var sourceFile = TryGetSourceFile(item);
            yield return item.DisplayName;
            yield return item.ClassDisplayName;
            yield return sourceFile;
            yield return GetRootCandidateFileName(item.DisplayName, sourceFile);
        }

        private static ModelItem TryResolveChildPath(ModelItem start, IList<string> segments, int segmentIndex)
        {
            if (start == null || segments == null || segmentIndex >= segments.Count)
                return null;
            if (!ItemNameMatchesSegment(start, segments[segmentIndex]))
                return null;
            if (segmentIndex == segments.Count - 1)
                return start;

            foreach (ModelItem child in start.Children)
            {
                var resolved = TryResolveChildPath(child, segments, segmentIndex + 1);
                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        private static bool ItemNameMatchesSegment(ModelItem item, string segment)
        {
            if (item == null || string.IsNullOrWhiteSpace(segment))
                return false;

            return string.Equals(item.DisplayName ?? string.Empty, segment, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.ClassDisplayName ?? string.Empty, segment, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(TryGetSourceFile(item) ?? string.Empty, segment, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(GetRootCandidateFileName(item.DisplayName, TryGetSourceFile(item)), segment, StringComparison.OrdinalIgnoreCase);
        }

        // Dedup by item identity. ResolveListChildrenParent already reports more
        // than one match as an error, so a path key did not merge two same-named
        // roots into one answer -- it picked one of them and hid the ambiguity,
        // while the parentMatchHandle path, which keys on Distinct(), reported it.
        private static void AddResolvedPathCandidate(ICollection<ModelItem> result, ISet<ModelItem> seen, ModelItem item)
        {
            if (result == null || item == null)
                return;

            if (seen != null && !seen.Add(item))
                return;

            result.Add(item);
        }

        private static string NormalizeRootNameComparison(string comparison)
        {
            var normalized = NormalizeComparison(comparison);
            if (string.Equals(normalized, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                return normalized;

            throw new AgentCommandException(
                ErrorCodes.SchemaViolation,
                "find_root_items_by_name supports only equals, contains, and wildcard comparisons.");
        }

        private static int GetNextSearchDeadlineMilliseconds(int requestTimeoutMs)
        {
            if (requestTimeoutMs <= 0)
                return 0;

            var deadline = requestTimeoutMs - RequestTimeoutSafetyMarginMilliseconds;
            return deadline < 1 ? 1 : deadline;
        }

        private static void EnsureCanStartNextSearch(Stopwatch requestStarted, int nextSearchDeadlineMs)
        {
            if (requestStarted == null || nextSearchDeadlineMs <= 0)
                return;

            if (requestStarted.ElapsedMilliseconds < nextSearchDeadlineMs)
                return;

            throw new AgentCommandException(
                ErrorCodes.RequestTimeout,
                "The find_items request is close to its timeout. Run exactly one search/query per find_items call.");
        }

        private RootSearchIndex GetRootSearchIndex(Document document)
        {
            var cacheKey = GetRootSearchCacheKey(document);
            lock (_rootSearchIndexLock)
            {
                if (_rootSearchIndex != null && string.Equals(_rootSearchIndex.CacheKey, cacheKey, StringComparison.Ordinal))
                    return _rootSearchIndex;

                _rootSearchIndex = BuildRootSearchIndex(document, cacheKey);
                return _rootSearchIndex;
            }
        }

        private static RootSearchIndex BuildRootSearchIndex(Document document, string cacheKey)
        {
            var result = new List<RootSearchCandidate>();
            if (document == null || document.Models == null)
                return new RootSearchIndex(cacheKey, 0, result);

            // Dedup by item identity, not by the display-name path. Two appended
            // files that share a display name produce the same path, so a path key
            // drops the second root from the index that every root tool reads.
            var seenItems = new HashSet<ModelItem>();
            var modelCount = 0;

            foreach (Model model in document.Models)
            {
                modelCount++;
                if (model == null || model.RootItem == null)
                    continue;

                AddRootSearchCandidate(result, model.RootItem, seenItems);
                foreach (ModelItem child in model.RootItem.Children)
                    AddRootSearchCandidate(result, child, seenItems);
            }

            return new RootSearchIndex(cacheKey, modelCount, result);
        }

        private static string GetRootSearchCacheKey(Document document)
        {
            if (document == null)
                return "<null>";

            var fileName = document.FileName ?? string.Empty;
            var modelCount = 0;
            var modelFingerprint = 17;

            if (document.Models != null)
            {
                foreach (Model model in document.Models)
                {
                    modelCount++;
                    modelFingerprint = unchecked((modelFingerprint * 31) + (model == null ? 0 : model.GetHashCode()));
                    modelFingerprint = unchecked((modelFingerprint * 31) + (model != null && model.RootItem != null ? model.RootItem.GetHashCode() : 0));
                }
            }

            return fileName + "|" +
                   modelCount.ToString(CultureInfo.InvariantCulture) + "|" +
                   modelFingerprint.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddRootSearchCandidate(
            ICollection<RootSearchCandidate> candidates,
            ModelItem item,
            ISet<ModelItem> seenItems)
        {
            if (candidates == null || item == null)
                return;

            if (seenItems != null && !seenItems.Add(item))
                return;

            var path = BuildItemPath(item);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceFile = TryGetSourceFile(item);
            AddRootAlias(aliases, item.DisplayName);
            AddRootAlias(aliases, sourceFile);

            foreach (var alias in aliases.ToList())
                AddRootAlias(aliases, GetModelContentFileName(alias));

            candidates.Add(new RootSearchCandidate(
                item,
                item.DisplayName ?? string.Empty,
                sourceFile ?? string.Empty,
                GetRootCandidateFileName(item.DisplayName, sourceFile),
                path,
                aliases.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase).ToList()));
        }

        private static bool RootCandidateMatches(RootSearchCandidate candidate, string query, string comparison)
        {
            if (candidate == null || candidate.Aliases == null || string.IsNullOrWhiteSpace(query))
                return false;

            foreach (var alias in candidate.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                    continue;

                if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(alias, query, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) &&
                    alias.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase) &&
                    WildcardMatches(alias, query))
                    return true;
            }

            return false;
        }

        private static void AddRootAlias(ICollection<string> aliases, string value)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(value))
                return;

            aliases.Add(value.Trim());
        }

        private static string GetRootCandidateFileName(string displayName, string sourceFile)
        {
            if (!string.IsNullOrWhiteSpace(sourceFile))
                return GetModelContentFileName(sourceFile);

            if (string.IsNullOrWhiteSpace(displayName))
                return string.Empty;

            var trimmed = displayName.Trim();
            return trimmed.EndsWith(".rvm", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : string.Empty;
        }

        private static string GetModelContentFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            var lastSeparator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return lastSeparator >= 0 && lastSeparator + 1 < trimmed.Length
                ? trimmed.Substring(lastSeparator + 1)
                : trimmed;
        }

        private static RootItemInfo ToRootItemInfo(RootSearchCandidate candidate, bool includeAliases)
        {
            return new RootItemInfo
            {
                DisplayName = candidate == null ? string.Empty : candidate.DisplayName,
                SourceFile = candidate == null ? string.Empty : candidate.SourceFile,
                FileName = candidate == null ? string.Empty : candidate.FileName,
                Path = candidate == null ? string.Empty : candidate.Path,
                Aliases = includeAliases && candidate != null
                    ? candidate.Aliases.ToList()
                    : new List<string>(),
            };
        }

        private static string GetDocumentTitle(Document document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.FileName))
                return string.Empty;

            return Path.GetFileName(document.FileName);
        }

        private static string TryGetSourceFile(ModelItem item)
        {
            var current = item;
            while (current != null)
            {
                var sourceFileProperty = TryFindSourceFileProperty(current);
                if (sourceFileProperty != null)
                    return GetPropertyDisplayValue(sourceFileProperty);

                current = current.Parent;
            }

            return string.Empty;
        }

        private static DataProperty TryFindSourceFileProperty(ModelItem item)
        {
            var property = TryFindInternalPropertyCore(item, ItemInternalCategory, SourceFileInternalProperty);
            if (property != null)
                return property;

            foreach (var alias in SourceFileDisplayProperties)
            {
                property = TryFindDisplayPropertyCore(item, alias.Category, alias.Property);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static string BuildItemPath(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            var stack = new Stack<string>();
            var current = item;

            while (current != null)
            {
                stack.Push(string.IsNullOrWhiteSpace(current.DisplayName)
                    ? current.ClassDisplayName
                    : current.DisplayName);
                current = current.Parent;
            }

            return string.Join(" / ", stack.ToArray());
        }

        private static string GetCachedPath(ModelItem item, IDictionary<ModelItem, string> pathCache)
        {
            string path;
            if (pathCache.TryGetValue(item, out path))
                return path;

            path = BuildItemPath(item);
            pathCache[item] = path;
            return path;
        }
    }
}