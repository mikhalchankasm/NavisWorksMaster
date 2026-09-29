using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Builds the path of every node of one root-to-node chain in a single pass.
    ///
    /// `selected_items_tree` prints a `path` for each node of each selected
    /// item's chain, and the host built every one of them by climbing
    /// `ModelItem.Parent` to the root and joining the names: a chain of depth d
    /// cost d climbs, each materializing a wrapper per ancestor. A chain's names
    /// are read once anyway, and a node's path is its parent's path plus one
    /// segment, so the whole chain's paths come from the names alone.
    ///
    /// Element i is names[0..i] joined with <see cref="Separator"/> -- byte for
    /// byte what `BuildItemPath` prints for that node, and what
    /// `FindItemsPathSegments` parses back. A null name stays an empty segment
    /// rather than losing a level, as `string.Join` renders it in the
    /// climb-and-join this replaces; `BuildItemPath` prefers `ClassDisplayName`
    /// whenever `DisplayName` is null or whitespace, and `ClassDisplayName` is
    /// null for some item classes.
    /// </summary>
    public static class ItemChainPaths
    {
        /// <summary>
        /// The separator <c>BuildItemPath</c> joins node names with.
        /// </summary>
        public const string Separator = " / ";

        /// <summary>
        /// The path of every node of one chain.
        /// </summary>
        /// <param name="names">The chain's node names, root first.</param>
        /// <returns>One path per name, in the same order; nothing for an empty chain.</returns>
        public static IReadOnlyList<string> Build(IReadOnlyList<string> names)
        {
            if (names == null)
                throw new ArgumentNullException(nameof(names));

            var paths = new List<string>(names.Count);
            var path = string.Empty;
            for (var index = 0; index < names.Count; index++)
            {
                var segment = names[index] ?? string.Empty;
                path = index == 0 ? segment : path + Separator + segment;
                paths.Add(path);
            }

            return paths;
        }
    }
}
