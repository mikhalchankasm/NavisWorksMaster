using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NavisHelper.Core;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiLocalizationBindingRegistry : IDisposable
    {
        private readonly Dictionary<BindingIdentity, Action> _bindings =
            new Dictionary<BindingIdentity, Action>();
        private bool _isDisposed;

        internal int Count => _bindings.Count;

        internal bool Register(object target, string slot, Action refresh)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(UiLocalizationBindingRegistry));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(slot))
                throw new ArgumentException("A semantic binding slot is required.", nameof(slot));
            if (refresh == null)
                throw new ArgumentNullException(nameof(refresh));

            var identity = new BindingIdentity(target, slot);
            if (_bindings.ContainsKey(identity))
                return false;

            _bindings.Add(identity, refresh);
            try
            {
                refresh();
                return true;
            }
            catch (Exception ex)
            {
                // Registration is atomic: a binding whose initial refresh fails is
                // rolled back and must be registered again by its owner.
                _bindings.Remove(identity);
                LogRefreshFailure(identity, ex, "initial registration");
                return false;
            }
        }

        internal void Refresh()
        {
            if (_isDisposed)
                return;

            KeyValuePair<BindingIdentity, Action>[] snapshot = _bindings.ToArray();
            foreach (KeyValuePair<BindingIdentity, Action> binding in snapshot)
            {
                if (_isDisposed)
                    return;

                Action current;
                if (!_bindings.TryGetValue(binding.Key, out current) ||
                    !ReferenceEquals(current, binding.Value))
                {
                    continue;
                }

                try
                {
                    current();
                }
                catch (Exception ex)
                {
                    // A transient target failure must not prevent later bindings
                    // or a future language refresh from running.
                    LogRefreshFailure(binding.Key, ex, "refresh");
                }
            }
        }

        internal bool Unregister(object target, string slot)
        {
            if (_isDisposed || target == null || string.IsNullOrWhiteSpace(slot))
                return false;

            return _bindings.Remove(new BindingIdentity(target, slot));
        }

        public void Dispose()
        {
            _isDisposed = true;
            _bindings.Clear();
        }

        private static void LogRefreshFailure(
            BindingIdentity identity,
            Exception exception,
            string phase)
        {
            try
            {
                string targetType = identity.TargetType?.FullName ?? "<unknown>";
                string exceptionType = exception?.GetType().FullName ?? "<unknown>";
                Logger.Error(
                    "Localization binding " + phase +
                    " failed for slot '" + identity.Slot +
                    "' on target type '" + targetType +
                    "' (" + exceptionType + ").",
                    "UiLocalizationBindingRegistry");
            }
            catch
            {
                // Localization refresh must not depend on logging availability.
            }
        }

        private sealed class BindingIdentity : IEquatable<BindingIdentity>
        {
            private readonly object _target;
            private readonly string _slot;

            internal BindingIdentity(object target, string slot)
            {
                _target = target;
                _slot = slot;
            }

            internal string Slot => _slot;

            internal Type TargetType => _target?.GetType();

            public bool Equals(BindingIdentity other)
            {
                return other != null &&
                       ReferenceEquals(_target, other._target) &&
                       string.Equals(_slot, other._slot, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as BindingIdentity);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(_target) * 397) ^
                           StringComparer.Ordinal.GetHashCode(_slot);
                }
            }
        }
    }
}
