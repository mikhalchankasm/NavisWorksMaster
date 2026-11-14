using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>A lazily adapted Selection Tree node.</summary>
    public interface IFbxTreeNode
    {
        string Name { get; }
        string ClassName { get; }
        bool IsHidden { get; }
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
            public bool Equals(IFbxTreeNode x, IFbxTreeNode y) { return ReferenceEquals(x, y); }
            public int GetHashCode(IFbxTreeNode obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
