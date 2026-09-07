using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NavisHelper.Core
{
    internal enum CooperativeModelRecoveryStatus
    {
        NotNeeded,
        Restored,
        Unknown
    }

    internal sealed class CooperativeModelApplyRecoveryResult
    {
        internal CooperativeModelApplyRecoveryResult(
            CooperativeModelRecoveryStatus status,
            IReadOnlyList<Exception> errors)
        {
            Status = status;
            Errors = errors;
        }

        internal CooperativeModelRecoveryStatus Status { get; }
        internal IReadOnlyList<Exception> Errors { get; }
    }

    /// <summary>
    /// Best-effort recovery after a mutation may already have changed state.
    /// User cancellation cannot abandon recovery; document lifetime guards
    /// still prevent writes to a replacement or invalid document. Callers
    /// own snapshot capture, mutation suppression and cooperative readback.
    /// </summary>
    internal static class CooperativeModelApplyRecovery
    {
        internal static async Task<CooperativeModelApplyRecoveryResult> RecoverAsync(
            IEnumerable<Action> restoreSteps,
            Action checkDocumentCurrent,
            Func<Task<bool>> verifyRestored)
        {
            if (restoreSteps == null) throw new ArgumentNullException(nameof(restoreSteps));
            if (checkDocumentCurrent == null) throw new ArgumentNullException(nameof(checkDocumentCurrent));
            if (verifyRestored == null) throw new ArgumentNullException(nameof(verifyRestored));

            var errors = new List<Exception>();
            bool verified = false;
            try
            {
                foreach (var restore in restoreSteps)
                {
                    // Guard failures leave this loop immediately. A failed
                    // restore step, however, must not skip independent steps.
                    checkDocumentCurrent();
                    try
                    {
                        restore();
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }

                checkDocumentCurrent();
                verified = await verifyRestored();
                checkDocumentCurrent();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }

            return new CooperativeModelApplyRecoveryResult(
                verified && errors.Count == 0
                    ? CooperativeModelRecoveryStatus.Restored
                    : CooperativeModelRecoveryStatus.Unknown,
                errors.AsReadOnly());
        }
    }

    /// <summary>
    /// Original flags for every visited hierarchy item, including aliases
    /// sharing instance data. Record is intended for an existing cooperative
    /// traversal. The injected comparer must match mutation instance identity,
    /// not merely a hash code. Conflicting alias flags reject the snapshot
    /// before the caller starts any document mutation.
    /// </summary>
    internal sealed class VisibilitySnapshot<T>
    {
        private readonly Dictionary<T, bool> _instanceFlags;
        private readonly List<T> _originallyVisible = new List<T>();
        private readonly List<T> _originallyHidden = new List<T>();

        internal VisibilitySnapshot(IEqualityComparer<T> instanceComparer = null)
        {
            _instanceFlags = new Dictionary<T, bool>(
                instanceComparer ?? EqualityComparer<T>.Default);
            Visible = _originallyVisible.AsReadOnly();
            Hidden = _originallyHidden.AsReadOnly();
        }

        internal IReadOnlyList<T> Visible { get; }
        internal IReadOnlyList<T> Hidden { get; }
        internal IEnumerable<KeyValuePair<T, bool>> Entries
        {
            get
            {
                foreach (var item in _originallyVisible)
                    yield return new KeyValuePair<T, bool>(item, false);
                foreach (var item in _originallyHidden)
                    yield return new KeyValuePair<T, bool>(item, true);
            }
        }

        internal void Record(T item, bool wasHidden)
        {
            if (ReferenceEquals(item, null))
                throw new ArgumentNullException(nameof(item));

            if (_instanceFlags.TryGetValue(item, out var previous))
            {
                if (previous != wasHidden)
                {
                    throw new InvalidOperationException(
                        "Items sharing instance data have conflicting original visibility flags.");
                }
            }
            else
            {
                _instanceFlags.Add(item, wasHidden);
            }

            // Keep each hierarchy entry for full readback, even when another
            // entry shares its instance identity.
            if (wasHidden)
                _originallyHidden.Add(item);
            else
                _originallyVisible.Add(item);
        }
    }
}
