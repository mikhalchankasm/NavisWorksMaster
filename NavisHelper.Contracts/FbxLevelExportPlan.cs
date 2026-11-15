using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>A lazily adapted Selection Tree node.</summary>
    public interface IFbxTreeNode
    {
        string Name { get; }
        string ClassName { get; }
        bool IsHidden { get; }
        /// <summary>The parent node, or null for a root item.</summary>
        IFbxTreeNode Parent { get; }
        IEnumerable<IFbxTreeNode> Children { get; }
    }

    /// <summary>One export, retaining the branch used to isolate its item.</summary>
    public sealed class FbxLevelExportPlanItem
    {
        internal FbxLevelExportPlanItem(int index, FbxLevelExportPlan.Branch branch, string fileName)
        {
            Index = index;
            Item = branch.Node;
            FileName = fileName;
            Path = branch;
        }

        public int Index { get; }
        public IFbxTreeNode Item { get; }
        public string FileName { get; }
        internal FbxLevelExportPlan.Branch Path { get; }
    }

    /// <summary>An item omitted because it or an ancestor was hidden.</summary>
    public sealed class FbxLevelExportSkip
    {
        internal FbxLevelExportSkip(int index, IFbxTreeNode item, string reason)
        {
            Index = index;
            Item = item;
            Reason = reason;
        }

        public int Index { get; }
        public IFbxTreeNode Item { get; }
        public string Reason { get; }
    }

    /// <summary>A pure export plan with no filesystem or Navisworks dependencies.</summary>
    public sealed class FbxLevelExportPlan
    {
        public const int MaxLevel = 20;
        public const int MaxItems = 5000;
        public const int MaxNameLength = 80;
        public const int NameSearchLimit = 1000;
        public const long MinFreeBytes = 1073741824;

        private FbxLevelExportPlan(int level, int total, List<FbxLevelExportPlanItem> items,
            List<FbxLevelExportSkip> skipped)
        {
            Level = level;
            TotalItems = total;
            Items = items.AsReadOnly();
            Skipped = skipped.AsReadOnly();
        }

        public int Level { get; }
        public int TotalItems { get; }
        public IReadOnlyList<FbxLevelExportPlanItem> Items { get; }
        public IReadOnlyList<FbxLevelExportSkip> Skipped { get; }

        internal sealed class Branch
        {
            internal IFbxTreeNode Node;
            internal IReadOnlyList<IFbxTreeNode> Siblings;
            internal Branch Parent;
            internal bool Hidden;
            // The node's children, read once and shared as the Siblings of every child branch.
            internal IReadOnlyList<IFbxTreeNode> ChildList;

            internal IReadOnlyList<IFbxTreeNode> ReadChildren()
            {
                return ChildList ?? (ChildList = new List<IFbxTreeNode>(
                    Node.Children ?? Array.Empty<IFbxTreeNode>()));
            }
        }

        /// <summary>Walks only as far as the target level, then names its visible items.</summary>
        public static FbxLevelExportPlan Build(IReadOnlyList<IFbxTreeNode> rootItems,
            int level, out string error)
        {
            error = null;
            if (level < 1 || level > MaxLevel)
            {
                error = "Export level must be between 1 and " + MaxLevel + ".";
                return null;
            }

            var candidates = new List<Branch>();
            int total = 0;
            CollectLevel(rootItems ?? Array.Empty<IFbxTreeNode>(), null, 1, level,
                candidates, ref total);
            if (total == 0)
            {
                error = "There are no items at the requested level.";
                return null;
            }
            if (total > MaxItems)
            {
                error = "The requested level has " + total.ToString(CultureInfo.InvariantCulture)
                    + " items; the limit is " + MaxItems + ".";
                return null;
            }

            var items = new List<FbxLevelExportPlanItem>();
            var skipped = new List<FbxLevelExportSkip>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Branch branch = candidates[i];
                int index = i + 1;
                if (branch.Hidden)
                    skipped.Add(new FbxLevelExportSkip(index, branch.Node,
                        "The item or an ancestor is hidden."));
                else
                    items.Add(new FbxLevelExportPlanItem(index, branch,
                        FileName(index, total, branch.Node)));
            }
            return new FbxLevelExportPlan(level, total, items, skipped);
        }

        /// <summary>Plans exports at a depth below the picked items, retaining their ancestors.</summary>
        public static FbxLevelExportPlan BuildFromItems(IReadOnlyList<IFbxTreeNode> rootItems,
            IReadOnlyList<IFbxTreeNode> items, int depth, out string error)
        {
            error = null;
            if (depth < 0 || depth > MaxLevel)
            {
                error = "Export depth must be between 0 and " + MaxLevel + ".";
                return null;
            }
            if (items == null || items.Count == 0)
            {
                error = "Select at least one item to export.";
                return null;
            }

            var seenItems = new HashSet<IFbxTreeNode>(NodeIdentity.Instance);
            var seenCandidates = new HashSet<IFbxTreeNode>(NodeIdentity.Instance);
            var candidates = new List<Branch>();
            var branches = new Dictionary<IFbxTreeNode, Branch>(NodeIdentity.Instance);
            foreach (IFbxTreeNode item in items)
                if (seenItems.Add(item))
                    CollectItemsLevel(CreateBranch(rootItems, item, branches), depth, candidates, seenCandidates);

            int total = seenCandidates.Count;
            if (total == 0)
            {
                error = "The selected items have no descendants at depth "
                    + depth.ToString(CultureInfo.InvariantCulture) + ".";
                return null;
            }
            if (total > MaxItems)
            {
                error = "The selection expands to " + total.ToString(CultureInfo.InvariantCulture)
                    + " items; the limit is " + MaxItems + ".";
                return null;
            }

            var exports = new List<FbxLevelExportPlanItem>();
            var skipped = new List<FbxLevelExportSkip>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Branch branch = candidates[i];
                int index = i + 1;
                if (branch.Hidden)
                    skipped.Add(new FbxLevelExportSkip(index, branch.Node,
                        "The item or an ancestor is hidden."));
                else
                    exports.Add(new FbxLevelExportPlanItem(index, branch,
                        FileName(index, total, branch.Node)));
            }
            return new FbxLevelExportPlan(depth, total, exports, skipped);
        }

        // Branches are cached by node, so items that share ancestors share their branches and each
        // parent's children are read once (Codex review P2: per-item copies were quadratic).
        private static Branch CreateBranch(IReadOnlyList<IFbxTreeNode> rootItems, IFbxTreeNode item,
            Dictionary<IFbxTreeNode, Branch> branches)
        {
            var ancestors = new Stack<IFbxTreeNode>();
            Branch branch = null;
            for (IFbxTreeNode node = item; node != null; node = node.Parent)
            {
                if (branches.TryGetValue(node, out branch))
                    break;
                ancestors.Push(node);
            }

            while (ancestors.Count > 0)
            {
                IFbxTreeNode node = ancestors.Pop();
                branch = new Branch
                {
                    Node = node,
                    Parent = branch,
                    Siblings = branch == null ? rootItems ?? Array.Empty<IFbxTreeNode>() : branch.ReadChildren(),
                    Hidden = node.IsHidden || (branch != null && branch.Hidden)
                };
                branches[node] = branch;
            }
            return branch;
        }

        private static void CollectItemsLevel(Branch branch, int depth, List<Branch> candidates,
            HashSet<IFbxTreeNode> seen)
        {
            if (depth == 0)
            {
                if (seen.Add(branch.Node) && seen.Count <= MaxItems)
                    candidates.Add(branch);
                return;
            }

            IReadOnlyList<IFbxTreeNode> children = branch.ReadChildren();
            foreach (IFbxTreeNode node in children)
                CollectItemsLevel(new Branch
                {
                    Node = node,
                    Parent = branch,
                    Siblings = children,
                    Hidden = node.IsHidden || branch.Hidden
                }, depth - 1, candidates, seen);
        }

        private static void CollectLevel(IReadOnlyList<IFbxTreeNode> siblings, Branch parent,
            int depth, int level, List<Branch> candidates, ref int total)
        {
            foreach (IFbxTreeNode node in siblings)
            {
                var branch = new Branch
                {
                    Node = node,
                    Siblings = siblings,
                    Parent = parent,
                    Hidden = node.IsHidden || (parent != null && parent.Hidden)
                };
                if (depth == level)
                {
                    total++;
                    if (total <= MaxItems)
                        candidates.Add(branch);
                }
                else
                {
                    IEnumerable<IFbxTreeNode> children = node.Children;
                    if (children != null)
                        CollectLevel(new List<IFbxTreeNode>(children), branch, depth + 1,
                            level, candidates, ref total);
                }
            }
        }

        /// <summary>Returns siblings along the branch, excluding the branch itself.</summary>
        public static IReadOnlyList<IFbxTreeNode> HideSet(FbxLevelExportPlanItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var excluded = new HashSet<IFbxTreeNode>(NodeIdentity.Instance);
            for (Branch branch = item.Path; branch != null; branch = branch.Parent)
                excluded.Add(branch.Node);

            var seen = new HashSet<IFbxTreeNode>(NodeIdentity.Instance);
            var result = new List<IFbxTreeNode>();
            for (Branch branch = item.Path; branch != null; branch = branch.Parent)
            {
                foreach (IFbxTreeNode sibling in branch.Siblings)
                    if (!excluded.Contains(sibling) && seen.Add(sibling))
                        result.Add(sibling);
            }
            return result.AsReadOnly();
        }

        /// <summary>Filters out nodes that were already hidden by the user.</summary>
        public static IReadOnlyList<IFbxTreeNode> UnhideSet(IEnumerable<IFbxTreeNode> hideSet)
        {
            if (hideSet == null)
                throw new ArgumentNullException(nameof(hideSet));

            var result = new List<IFbxTreeNode>();
            foreach (IFbxTreeNode node in hideSet)
                if (!node.IsHidden)
                    result.Add(node);
            return result.AsReadOnly();
        }

        /// <summary>Produces a portable, bounded Windows filename component.</summary>
        public static string CleanName(string name)
        {
            if (name == null)
                return string.Empty;

            char[] chars = name.TrimStart('/', ' ', '.').ToCharArray();
            const string invalid = "<>:\"/\\|?*";
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < 32 || invalid.IndexOf(chars[i]) >= 0)
                    chars[i] = '_';

            string result = new string(chars).TrimEnd(' ', '.');
            if (result.Length > MaxNameLength)
                result = result.Substring(0, MaxNameLength);
            return result.TrimEnd(' ', '.');
        }

        /// <summary>Uses the item, descendants, class or a fixed fallback for its name.</summary>
        public static string FileName(int index, int total, IFbxTreeNode item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            string name = CleanName(item.Name);
            if (name.Length == 0)
                name = FindDescendantName(item);
            if (name.Length == 0)
                name = CleanName(item.ClassName);
            if (name.Length == 0)
                name = "item";

            int digits = Math.Max(3, total.ToString(CultureInfo.InvariantCulture).Length);
            return index.ToString("D" + digits, CultureInfo.InvariantCulture) + "_" + name + ".fbx";
        }

        private static string FindDescendantName(IFbxTreeNode item)
        {
            var stack = new Stack<IEnumerator<IFbxTreeNode>>();
            try
            {
                IEnumerable<IFbxTreeNode> children = item.Children;
                if (children != null)
                    stack.Push(children.GetEnumerator());
                int visited = 0;
                while (stack.Count > 0 && visited < NameSearchLimit)
                {
                    IEnumerator<IFbxTreeNode> current = stack.Peek();
                    if (!current.MoveNext())
                    {
                        stack.Pop().Dispose();
                        continue;
                    }

                    IFbxTreeNode node = current.Current;
                    visited++;
                    string name = CleanName(node.Name);
                    if (name.Length > 0)
                        return name;
                    if (visited < NameSearchLimit)
                    {
                        children = node.Children;
                        if (children != null)
                            stack.Push(children.GetEnumerator());
                    }
                }
                return string.Empty;
            }
            finally
            {
                while (stack.Count > 0)
                    stack.Pop().Dispose();
            }
        }

        /// <summary>True for a drive path (<c>C:\x</c>, <c>C:/x</c>) or a UNC path. False for
        /// <c>C:x</c> and <c>\x</c>, which depend on the process's current directory.</summary>
        public static bool IsFullyQualifiedFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return false;
            if (folder.Length >= 3 && char.IsLetter(folder[0]) && folder[1] == ':'
                && (folder[2] == '\\' || folder[2] == '/'))
                return true;
            return folder.Length >= 3 && (folder[0] == '\\' || folder[0] == '/')
                && (folder[1] == '\\' || folder[1] == '/');
        }

        /// <summary>Reserves at least 1 GiB or 150 percent of the largest written file.</summary>
        public static bool HasRoomForNext(long freeBytes, long largestWrittenBytes)
        {
            long extra = largestWrittenBytes / 2;
            if (largestWrittenBytes > long.MaxValue - extra)
                return false;
            return freeBytes >= Math.Max(MinFreeBytes, largestWrittenBytes + extra);
        }

        private sealed class NodeIdentity : IEqualityComparer<IFbxTreeNode>
        {
            internal static readonly NodeIdentity Instance = new NodeIdentity();
            public bool Equals(IFbxTreeNode x, IFbxTreeNode y) { return object.Equals(x, y); }
            public int GetHashCode(IFbxTreeNode obj) { return obj == null ? 0 : obj.GetHashCode(); }
        }
    }
}
