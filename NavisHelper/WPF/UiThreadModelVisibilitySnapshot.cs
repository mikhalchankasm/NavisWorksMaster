using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Captures the entire traversed hierarchy because SetHidden affects all
    /// items sharing instance data, including items outside the scan result.
    /// </summary>
    internal sealed class UiThreadModelVisibilitySnapshot
    {
        internal VisibilitySnapshot<ModelItem> State { get; } =
            new VisibilitySnapshot<ModelItem>(new InstanceComparer());

        internal bool TryRecord(ModelItem item, out bool hidden)
        {
            hidden = item.IsHidden;
            try
            {
                State.Record(item, hidden);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Refuse a mutation whose instance-wide setter cannot
                // reproduce the captured flags on recovery.
                return false;
            }
        }

        private sealed class InstanceComparer : IEqualityComparer<ModelItem>
        {
            public bool Equals(ModelItem x, ModelItem y)
            {
                return ReferenceEquals(x, y) ||
                       (x != null && y != null && x.IsSameInstance(y));
            }

            public int GetHashCode(ModelItem item) => item.InstanceHashCode;
        }
    }
}
