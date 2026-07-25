using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashVirtualGroupCachePolicy
    {
        public static bool ShouldStore<T>(IEnumerable<T> results)
            where T : class
        {
            return results != null && results.Any(result => result != null);
        }

        public static List<T> CreateResultSnapshot<T>(IEnumerable<T> results)
            where T : class
        {
            return results == null
                ? new List<T>()
                : results.Where(result => result != null).Distinct().ToList();
        }

        public static bool MatchesRestoreDuplicate<TPersistent>(
            ClashVirtualGroupSide existingSide,
            string existingPath,
            string existingLabel,
            TPersistent existingPersistentGroup,
            ClashVirtualGroupSide cachedSide,
            string cachedPath,
            string cachedLabel,
            TPersistent resolvedPersistentGroup)
            where TPersistent : class
        {
            // Preserve the original ReferenceEquals rule, including null/null.
            // The restore adapter resolves and rejects a null candidate group first.
            return ClashVirtualGroupIdentityHelper.AreSame(
                       existingSide,
                       existingPath,
                       existingLabel,
                       cachedSide,
                       cachedPath,
                       cachedLabel) ||
                   object.ReferenceEquals(existingPersistentGroup, resolvedPersistentGroup) ||
                   (existingSide == cachedSide &&
                    string.Equals(
                        ClashVirtualGroupIdentityHelper.GetUserName(existingLabel),
                        ClashVirtualGroupIdentityHelper.GetUserName(cachedLabel),
                        System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
