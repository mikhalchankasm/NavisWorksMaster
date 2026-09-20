using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Turns a model-tree path into the node names it addresses.
    ///
    /// This is the inverse of the path the tools print. `list_root_items`,
    /// `list_item_children` and every find_items preview report a `path` built by
    /// joining `DisplayName` up the ancestor chain with the separator `" / "`, and
    /// `scope=under_named_node` with `scopeNodePath` is documented as taking that
    /// path back. The two must agree, or the fast deterministic scope option
    /// cannot be reached from any other tool's output.
    ///
    /// Moved here from a private method in SearchService so the rule can be tested
    /// without a live Navisworks document.
    /// </summary>
    public static class FindItemsPathSegments
    {
        /// <summary>
        /// The separator <c>BuildItemPath</c> joins node names with.
        /// </summary>
        public const string Separator = " / ";

        /// <summary>
        /// Consumes one node from the front of a printed path, or returns null.
        ///
        /// Splitting the whole path up front cannot be correct, because a
        /// `DisplayName` may itself contain the separator: `BuildItemPath` emits
        /// `Supply / Return / Child` for a node called `Supply / Return`, and those
        /// bytes are indistinguishable from two levels. Resolution therefore has to
        /// consult the tree, matching the longest candidate name that the remaining
        /// path starts with rather than guessing where the boundaries are.
        ///
        /// Returns <see cref="string.Empty"/> when a name consumes the whole
        /// remainder, meaning this node is the target; the remaining path after the
        /// separator when a name is a proper prefix; and null when no candidate
        /// matches.
        /// </summary>
        public static string TryConsume(IEnumerable<string> candidateNames, string remainingPath)
        {
            if (candidateNames == null || string.IsNullOrEmpty(remainingPath))
                return null;

            string best = null;
            foreach (var name in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var consumesEverything = string.Equals(name, remainingPath, StringComparison.OrdinalIgnoreCase);
                var isPrefix = remainingPath.StartsWith(name + Separator, StringComparison.OrdinalIgnoreCase);
                if (!consumesEverything && !isPrefix)
                    continue;

                // Longest wins: a node named "Supply / Return" must beat a candidate
                // name of "Supply" on the same node, or the deeper match is lost.
                if (best == null || name.Length > best.Length)
                    best = name;
            }

            if (best == null)
                return null;
            if (string.Equals(best, remainingPath, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return remainingPath.Substring(best.Length + Separator.Length);
        }

        /// <summary>
        /// Splits a caller-authored slash path, the form this tool accepted before
        /// it accepted printed paths: <c>Model/Level/Item</c>, backslashes included.
        ///
        /// A printed path never comes here. It is resolved with <see cref="TryConsume"/>
        /// against the tree, because a <c>DisplayName</c> may contain the separator and
        /// a slash, so no split of the text alone can find the boundaries. Splitting a
        /// printed path here would tear <c>/150.=79338/61632.1</c> into three segments,
        /// which is why the method says slash path in its name.
        /// </summary>
        public static IEnumerable<string> SplitSlashPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                yield break;

            var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var value = segment.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }
}
