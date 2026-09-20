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
        /// Every way one node can consume the front of a printed path.
        ///
        /// Splitting the whole path up front cannot be correct, because a
        /// `DisplayName` may itself contain the separator: `BuildItemPath` emits
        /// `Supply / Return / Child` for a node called `Supply / Return`, and those
        /// bytes are indistinguishable from two levels. Resolution therefore has to
        /// consult the tree rather than guessing where the boundaries are.
        ///
        /// This returns every continuation rather than the best one. The first version
        /// returned only the longest match, reasoning that a node named
        /// `Supply / Return` must beat a candidate name of `Supply` on the same node
        /// or the deeper match is lost. That is right about what must not be lost and
        /// wrong about how to keep it. A node offers several names -- `DisplayName`,
        /// `ClassDisplayName`, its source file -- and when a shorter one is a proper
        /// prefix of the remaining path, both readings can lead to a real node.
        /// Choosing by candidate-name length resolved the path by a property of the
        /// strings rather than a fact about the model. Returning both lets the caller
        /// see the path is ambiguous and refuse, instead of answering confidently
        /// about a node the caller did not name.
        ///
        /// <see cref="string.Empty"/> means a name consumed the whole remainder, so
        /// this node is the target. Any other value is the path left after the
        /// separator. Longest first, so a caller wanting the old behaviour takes the
        /// first element; the result is empty when no candidate matches.
        /// </summary>
        public static IEnumerable<string> Continuations(IEnumerable<string> candidateNames, string remainingPath)
        {
            if (candidateNames == null || string.IsNullOrEmpty(remainingPath))
                return new string[0];

            var matched = new List<string>();
            foreach (var name in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var consumesEverything = string.Equals(name, remainingPath, StringComparison.OrdinalIgnoreCase);
                var isPrefix = remainingPath.StartsWith(name + Separator, StringComparison.OrdinalIgnoreCase);
                if (!consumesEverything && !isPrefix)
                    continue;

                matched.Add(name);
            }

            matched.Sort((left, right) => right.Length.CompareTo(left.Length));

            var continuations = new List<string>();
            foreach (var name in matched)
            {
                var rest = string.Equals(name, remainingPath, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : remainingPath.Substring(name.Length + Separator.Length);

                // Two candidate names of the same length, or a DisplayName that equals
                // the source file, leave the same remainder. That is one reading of the
                // path, not two, and must not look like an ambiguity.
                var alreadyKnown = false;
                foreach (var known in continuations)
                {
                    if (string.Equals(known, rest, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyKnown = true;
                        break;
                    }
                }

                if (!alreadyKnown)
                    continuations.Add(rest);
            }

            return continuations;
        }

        /// <summary>
        /// Splits a caller-authored slash path, the form this tool accepted before
        /// it accepted printed paths: <c>Model/Level/Item</c>, backslashes included.
        ///
        /// A printed path never comes here. It is resolved with <see cref="Continuations"/>
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
