using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>Pure store logic for overlay world markers: (snapshot, request) -> (new snapshot, result); input snapshots are never mutated.</summary>
    public static class WorldMarkerOverlayPlanner
    {
        public const int MaxStoredMarkers = 500;
        public const int MinSizePx = 5;
        public const int MaxSizePx = 200;
        public const int DefaultSizePx = 12;

        public static (WorldMarkerOverlaySnapshot Snapshot, WorldMarkerOverlaySetResult Result) Set(
            WorldMarkerOverlaySnapshot current,
            WorldMarkerOverlaySetRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.Markers == null || request.Markers.Count == 0)
                throw new ArgumentException("markers must contain at least one marker.", nameof(request));

            var mode = request.Mode == null
                ? WorldMarkerOverlayModes.Upsert
                : request.Mode.Trim().ToLowerInvariant();
            if (mode != WorldMarkerOverlayModes.Upsert && mode != WorldMarkerOverlayModes.ReplaceAll)
                throw new ArgumentException("mode must be upsert or replace_all.", nameof(request));

            var markers = new List<WorldMarkerOverlayMarker>(request.Markers.Count);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < request.Markers.Count; index++)
            {
                var marker = NormalizeSpec(request.Markers[index], index);
                if (!seenIds.Add(marker.MarkerId))
                    throw new ArgumentException(
                        "markers contains duplicate names after normalization: " + marker.Name + ".",
                        nameof(request));
                markers.Add(marker);
            }

            var snapshot = current ?? WorldMarkerOverlaySnapshot.Empty;

            if (mode == WorldMarkerOverlayModes.ReplaceAll)
            {
                if (markers.Count > MaxStoredMarkers)
                    return (snapshot, Refused(snapshot.Count, markers.Count));

                return (new WorldMarkerOverlaySnapshot(markers), new WorldMarkerOverlaySetResult
                {
                    Accepted = true,
                    Created = markers.Count,
                    MarkerCount = markers.Count,
                });
            }

            var currentIds = new HashSet<string>(
                snapshot.Markers.Select(m => m.MarkerId), StringComparer.OrdinalIgnoreCase);
            var createdCount = markers.Count(m => !currentIds.Contains(m.MarkerId));
            if (snapshot.Count + createdCount > MaxStoredMarkers)
                return (snapshot, Refused(snapshot.Count, snapshot.Count + createdCount));

            var next = new List<WorldMarkerOverlayMarker>(snapshot.Count + createdCount);
            var requestedById = markers.ToDictionary(m => m.MarkerId, m => m, StringComparer.OrdinalIgnoreCase);
            foreach (var existing in snapshot.Markers)
                if (requestedById.TryGetValue(existing.MarkerId, out var replacement))
                    next.Add(replacement);
                else
                    next.Add(existing);

            next.AddRange(markers.Where(m => !currentIds.Contains(m.MarkerId)));

            return (new WorldMarkerOverlaySnapshot(next), new WorldMarkerOverlaySetResult
            {
                Accepted = true,
                Created = createdCount,
                Replaced = markers.Count - createdCount,
                MarkerCount = next.Count,
            });
        }

        public static (WorldMarkerOverlaySnapshot Snapshot, WorldMarkerOverlayManageResult Result) Manage(
            WorldMarkerOverlaySnapshot current,
            WorldMarkerOverlayManageRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var operation = (request.Operation ?? string.Empty).Trim().ToLowerInvariant();
            if (operation != WorldMarkerOverlayOperations.Hide &&
                operation != WorldMarkerOverlayOperations.Show &&
                operation != WorldMarkerOverlayOperations.Delete &&
                operation != WorldMarkerOverlayOperations.Clear)
                throw new ArgumentException("operation must be hide, show, delete, or clear.", nameof(request));

            var snapshot = current ?? WorldMarkerOverlaySnapshot.Empty;
            var (nameSet, missingNames) = CollectLookups(request.Names, snapshot, m => m.Name, StringComparer.Ordinal);
            var (idSet, missingIds) = CollectLookups(request.Ids, snapshot, m => m.MarkerId, StringComparer.OrdinalIgnoreCase);
            var group = NormalizeGroup(request.Group);
            var hasSelector = nameSet.Count > 0 || idSet.Count > 0 || group != null;
            var selectAll = operation == WorldMarkerOverlayOperations.Clear && !hasSelector;

            var next = new List<WorldMarkerOverlayMarker>(snapshot.Count);
            var hidden = 0;
            var shown = 0;
            var deleted = 0;
            foreach (var marker in snapshot.Markers)
            {
                var selected = selectAll ||
                    (idSet.Count > 0 && idSet.Contains(marker.MarkerId)) ||
                    (nameSet.Count > 0 && nameSet.Contains(marker.Name)) ||
                    (group != null && marker.Group == group);
                if (!selected)
                {
                    next.Add(marker);
                    continue;
                }

                if (operation == WorldMarkerOverlayOperations.Delete ||
                    operation == WorldMarkerOverlayOperations.Clear)
                {
                    deleted++;
                    continue;
                }

                var targetVisible = operation == WorldMarkerOverlayOperations.Show;
                if (marker.Visible == targetVisible)
                {
                    next.Add(marker);
                    continue;
                }

                next.Add(WithVisible(marker, targetVisible));
                if (targetVisible)
                    shown++;
                else
                    hidden++;
            }

            var result2 = new WorldMarkerOverlayManageResult
            {
                Operation = operation,
                Hidden = hidden,
                Shown = shown,
                Deleted = deleted,
                MarkerCount = next.Count,
                MissingNames = missingNames,
                MissingIds = missingIds,
            };

            if (hidden + shown + deleted == 0)
                return (snapshot, result2);
            return (new WorldMarkerOverlaySnapshot(next), result2);
        }

        /// <summary>Union world bounds of the visible markers: anchor, pole base and top, +/- world size. Empty when nothing is visible.</summary>
        public static WorldMarkerOverlayBounds VisibleBounds(WorldMarkerOverlaySnapshot snapshot)
        {
            if (snapshot == null)
                return WorldMarkerOverlayBounds.Empty;

            var hasPoint = false;
            var minX = 0.0;
            var minY = 0.0;
            var minZ = 0.0;
            var maxX = 0.0;
            var maxY = 0.0;
            var maxZ = 0.0;

            void Include(double x, double y, double z)
            {
                if (!hasPoint)
                {
                    minX = maxX = x; minY = maxY = y; minZ = maxZ = z; hasPoint = true;
                    return;
                }

                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
            }

            foreach (var marker in snapshot.Markers)
            {
                if (!marker.Visible)
                    continue;
                var span = marker.WorldSize ?? 0.0;
                Include(marker.X - span, marker.Y - span, marker.Z - span);
                Include(marker.X + span, marker.Y + span, marker.Z + span);
                if (marker.PoleEnabled)
                {
                    Include(marker.X, marker.Y, marker.PoleBaseZ);
                    Include(marker.X, marker.Y, marker.PoleTopZ);
                }
            }

            return hasPoint
                ? new WorldMarkerOverlayBounds(minX, minY, minZ, maxX, maxY, maxZ)
                : WorldMarkerOverlayBounds.Empty;
        }

        private static WorldMarkerOverlaySetResult Refused(int currentCount, int attemptedCount)
        {
            return new WorldMarkerOverlaySetResult
            {
                Accepted = false,
                CapExceeded = true,
                RefusalReason = "the store holds at most " + MaxStoredMarkers.ToString(CultureInfo.InvariantCulture) +
                    " markers; the request would leave " + attemptedCount.ToString(CultureInfo.InvariantCulture) +
                    "; the snapshot is unchanged at " + currentCount.ToString(CultureInfo.InvariantCulture) + ".",
                MarkerCount = currentCount,
            };
        }

        private static WorldMarkerOverlayMarker NormalizeSpec(WorldMarkerOverlaySpec spec, int index)
        {
            if (spec == null)
                throw new ArgumentException("markers[" + index.ToString(CultureInfo.InvariantCulture) + "] is required.");

            var plan = WorldMarkerInputPolicy.NormalizeMarker(new WorldMarkerSpec
            {
                Name = spec.Name,
                X = spec.X,
                Y = spec.Y,
                Z = spec.Z,
                Style = spec.Style,
                Size = spec.Size,
                Color = spec.Color,
                Label = spec.Label,
                Pole = spec.Pole,
            });

            var sizePx = spec.SizePx ?? DefaultSizePx;
            if (sizePx < MinSizePx || sizePx > MaxSizePx)
                throw new ArgumentException("sizePx must be between " + MinSizePx + " and " + MaxSizePx + " pixels.");
            if (spec.Alpha.HasValue && (spec.Alpha.Value < 0 || spec.Alpha.Value > 255))
                throw new ArgumentException("alpha must be between 0 and 255.");

            return new WorldMarkerOverlayMarker
            {
                MarkerId = plan.MarkerId,
                Name = plan.Name,
                X = plan.X,
                Y = plan.Y,
                Z = plan.Z,
                Style = plan.Style,
                SizePx = sizePx,
                WorldSize = spec.Size,
                Color = plan.Color,
                Alpha = spec.Alpha,
                Label = plan.Label,
                PoleEnabled = plan.PoleEnabled,
                PoleBaseZ = plan.PoleBaseZ,
                PoleTopZ = plan.PoleTopZ,
                Group = NormalizeGroup(spec.Group),
                Visible = true,
            };
        }

        public static string NormalizeGroup(string group)
        {
            var normalized = (group ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            if (normalized.Length == 0)
                return null;
            if (normalized.Length > WorldMarkerInputPolicy.MaxNameLength)
                throw new ArgumentException(
                    "group must not exceed " + WorldMarkerInputPolicy.MaxNameLength + " characters.");
            foreach (var character in normalized)
                if (char.IsControl(character))
                    throw new ArgumentException("group must not contain control characters.");
            return normalized;
        }

        private static (HashSet<string> Values, List<string> Missing) CollectLookups(
            IEnumerable<string> values,
            WorldMarkerOverlaySnapshot snapshot,
            Func<WorldMarkerOverlayMarker, string> keyOf,
            StringComparer comparer)
        {
            var set = new HashSet<string>(comparer);
            var missing = new List<string>();
            if (values != null)
                foreach (var value in values)
                {
                    var key = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
                    if (key.Length == 0 || !set.Add(key))
                        continue;
                    if (!snapshot.Markers.Any(m => comparer.Equals(keyOf(m), key)))
                        missing.Add(key);
                }

            return (set, missing);
        }

        private static WorldMarkerOverlayMarker WithVisible(WorldMarkerOverlayMarker marker, bool visible)
        {
            return new WorldMarkerOverlayMarker
            {
                MarkerId = marker.MarkerId,
                Name = marker.Name,
                X = marker.X,
                Y = marker.Y,
                Z = marker.Z,
                Style = marker.Style,
                SizePx = marker.SizePx,
                WorldSize = marker.WorldSize,
                Color = marker.Color,
                Alpha = marker.Alpha,
                Label = marker.Label,
                PoleEnabled = marker.PoleEnabled,
                PoleBaseZ = marker.PoleBaseZ,
                PoleTopZ = marker.PoleTopZ,
                Group = marker.Group,
                Visible = visible,
            };
        }
    }
}
