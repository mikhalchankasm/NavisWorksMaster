using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Splits a model-tree path into the node names it addresses.
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

        public static IEnumerable<string> Split(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                yield break;

            foreach (var segment in path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = segment.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }
}
