using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    // How model_color_scheme apply treats a runtime scheme that is still active.
    public enum ModelColorSchemeApplyMode
    {
        // Restore the active scheme's overrides first, then apply the new one.
        Replace,

        // Keep the active scheme; the new apply adds to it and one reset undoes both.
        Accumulate,
    }

    public static class ModelColorSchemeApplyModes
    {
        public static bool TryParse(string value, out ModelColorSchemeApplyMode mode)
        {
            mode = ModelColorSchemeApplyMode.Replace;
            var text = value == null ? string.Empty : value.Trim();
            if (text.Length == 0 || string.Equals(text, "replace", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(text, "accumulate", StringComparison.OrdinalIgnoreCase))
            {
                mode = ModelColorSchemeApplyMode.Accumulate;
                return true;
            }
            return false;
        }

        public static string ToWire(ModelColorSchemeApplyMode mode)
        {
            return mode == ModelColorSchemeApplyMode.Accumulate ? "accumulate" : "replace";
        }
    }

    // The original state of every item the runtime scheme overrode, in capture order.
    // The first capture wins: when a later batch recolours an item, its current state is
    // an earlier batch's colour, and reset must go back past it to the pre-scheme state.
    public sealed class ModelColorSchemeLedger<TItem, TState>
    {
        private readonly HashSet<TItem> _items;
        private readonly List<TState> _states = new List<TState>();

        public ModelColorSchemeLedger(IEqualityComparer<TItem> comparer = null)
        {
            _items = new HashSet<TItem>(comparer ?? EqualityComparer<TItem>.Default);
        }

        public int Count
        {
            get { return _states.Count; }
        }

        public IReadOnlyList<TState> States
        {
            get { return _states; }
        }

        public bool Contains(TItem item)
        {
            return item != null && _items.Contains(item);
        }

        public bool TryCapture(TItem item, Func<TItem, TState> capture)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (capture == null)
                throw new ArgumentNullException(nameof(capture));
            if (_items.Contains(item))
                return false;

            var state = capture(item);
            _items.Add(item);
            _states.Add(state);
            return true;
        }

        public void Clear()
        {
            _items.Clear();
            _states.Clear();
        }
    }
}
