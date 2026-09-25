using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Resolves, for a tree visited parent before children, the path of a node and
    /// the value it inherits from the nearest ancestor that has one -- without
    /// climbing to the root for every node and without keeping a wrapper for every
    /// visited node.
    ///
    /// `clash_create_matrix_from_selection` with name filters needs the display
    /// path and the source file of every item it scans, and both used to cost one
    /// full `Parent` climb per item: the path builds a wrapper per ancestor, and
    /// the source file enumerates the property categories of each ancestor until
    /// it finds the source-file property, which usually sits near the root. A
    /// parent-first walk does not need any of that: an item's path is its parent's
    /// path plus one segment, and its source file is its own if it carries one and
    /// otherwise its parent's. This resolver keeps exactly that -- the current
    /// root-to-node ancestor chain -- and never a map of all visited nodes, which
    /// would retain one wrapper per item for the whole call.
    ///
    /// The delegates take the tree as data, so the semantics are pinned by tests
    /// that need no document. Node equality uses the provided comparer, defaulting
    /// to <see cref="EqualityComparer{T}.Default"/>, because the parent delegate
    /// typically returns a fresh wrapper per call -- as `ModelItem.Parent` does --
    /// while equality compares the underlying node. The type is not thread-safe:
    /// use one instance per walk.
    /// </summary>
    /// <typeparam name="TNode">The node type.</typeparam>
    public sealed class AncestorPathResolver<TNode> where TNode : class
    {
        private struct ChainEntry
        {
            public TNode Node;
            public string Path;
            public string Value;
        }

        private readonly Func<TNode, TNode> _parent;
        private readonly Func<TNode, string> _segment;
        private readonly Func<TNode, string> _ownValue;
        private readonly string _separator;
        private readonly IEqualityComparer<TNode> _comparer;
        private readonly List<ChainEntry> _chain = new List<ChainEntry>();
        private int _fullWalks;

        /// <summary>
        /// Initializes the resolver.
        /// </summary>
        /// <param name="parent">Returns the parent of a node, or null for a root. Called once per resolved node on the fast path, and for each climbed ancestor on a full walk.</param>
        /// <param name="segment">Returns the path segment a node contributes. A null segment counts as an empty one.</param>
        /// <param name="ownValue">Returns the node's own value, or null when it has none. An empty string is a value and stops the inherited climb.</param>
        /// <param name="separator">Joined between the segments of a path.</param>
        /// <param name="comparer">Node equality for matching a parent against the chain; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public AncestorPathResolver(
            Func<TNode, TNode> parent,
            Func<TNode, string> segment,
            Func<TNode, string> ownValue,
            string separator,
            IEqualityComparer<TNode> comparer = null)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));
            if (ownValue == null)
                throw new ArgumentNullException(nameof(ownValue));

            _parent = parent;
            _segment = segment;
            _ownValue = ownValue;
            _separator = separator ?? string.Empty;
            _comparer = comparer ?? EqualityComparer<TNode>.Default;
        }

        /// <summary>Chain entries held right now; bounded by the depth of the tree.</summary>
        public int ChainDepth
        {
            get { return _chain.Count; }
        }

        /// <summary>Resolve calls that had to climb to the root because the node's parent was not in the chain.</summary>
        public int FullWalks
        {
            get { return _fullWalks; }
        }

        /// <summary>
        /// Resolves the path and the inherited value of <paramref name="node"/>.
        ///
        /// <paramref name="path"/> is the <paramref name="segment"/> of every node
        /// from the root down to <paramref name="node"/>, joined by the separator;
        /// it matches what a naive climb-and-join produces, including an empty
        /// segment for a node. <paramref name="value"/> is the
        /// <paramref name="ownValue"/> of the nearest node, <paramref name="node"/>
        /// itself first, whose own value is not null; null when none has one. An
        /// empty own value is a value and stops that climb, it is not skipped over.
        ///
        /// When <paramref name="node"/>'s parent is the chain's last entry -- the
        /// parent-first case -- the delegates run once for
        /// <paramref name="node"/> and never for its ancestors. When the parent is
        /// elsewhere in the chain, entries after it are dropped and the same fast
        /// path answers. When the parent is not in the chain at all -- any other
        /// visiting order -- the resolver climbs to the root, rebuilds the chain
        /// from it and counts one full walk; the results are the same either way.
        /// </summary>
        /// <param name="node">The node to resolve.</param>
        /// <param name="path">Receives the node's path from the root.</param>
        /// <param name="value">Receives the node's own value, or the nearest ancestor's.</param>
        public void Resolve(TNode node, out string path, out string value)
        {
            if (node == null)
            {
                path = string.Empty;
                value = null;
                return;
            }

            var ownValue = _ownValue(node);
            var segment = _segment(node) ?? string.Empty;
            var parent = _parent(node);

            if (parent == null)
            {
                // A root: no ancestor shares its chain, so the chain restarts here.
                _chain.Clear();
                path = segment;
                value = ownValue;
                Append(node, path, value);
                return;
            }

            var index = _chain.Count - 1;
            while (index >= 0 && !_comparer.Equals(_chain[index].Node, parent))
                index--;

            if (index < 0)
            {
                _fullWalks++;
                RebuildChain(parent);
                index = _chain.Count - 1;
            }
            else if (index < _chain.Count - 1)
            {
                // The parent is an ancestor of the previously resolved node: drop
                // that node's branch, the parent's entry ends the shared prefix.
                _chain.RemoveRange(index + 1, _chain.Count - index - 1);
            }

            var parentEntry = _chain[index];
            path = parentEntry.Path + _separator + segment;
            value = ownValue ?? parentEntry.Value;
            Append(node, path, value);
        }

        private void RebuildChain(TNode deepest)
        {
            var climb = new List<TNode>();
            var current = deepest;
            while (current != null)
            {
                climb.Add(current);
                current = _parent(current);
            }

            _chain.Clear();
            string path = null;
            string value = null;
            for (var i = climb.Count - 1; i >= 0; i--)
            {
                var node = climb[i];
                var segment = _segment(node) ?? string.Empty;
                var ownValue = _ownValue(node);
                path = i == climb.Count - 1 ? segment : path + _separator + segment;
                value = ownValue ?? value;
                Append(node, path, value);
            }
        }

        private void Append(TNode node, string path, string value)
        {
            _chain.Add(new ChainEntry
            {
                Node = node,
                Path = path,
                Value = value,
            });
        }
    }
}
