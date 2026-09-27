using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// The `find_items_by_bbox` zone match, as a pure function on points.
    ///
    /// Every read of `BoundingBox3D.Min`, `.Max` and `.Center` returns a fresh native
    /// wrapper, so the walk reads each item's corners once and shares them between
    /// the prune and the match. This type is the match half, away from the
    /// Navisworks API, with the same three modes and the same dispatch the tool has
    /// always had: `contains` and `center` when the mode string names them,
    /// `intersects` otherwise.
    ///
    /// Corners that were never read -- a box the walk could not obtain -- never
    /// match, exactly as a null box never did. Zone corners are assumed present and
    /// finite; `SpatialSearchOptionsHelper.ValidateBounds` has already checked them
    /// before the walk starts.
    /// </summary>
    public static class SpatialBoxMatch
    {
        /// <summary>
        /// Whether the item box described by <paramref name="itemMin"/> and
        /// <paramref name="itemMax"/> satisfies the zone in <paramref name="matchMode"/>.
        /// In `center` mode the caller reads `box.Center` once and passes it as
        /// <paramref name="itemCenter"/>; in the other modes that argument is null and
        /// the corners decide.
        /// </summary>
        public static bool Matches(
            SpatialPoint itemMin,
            SpatialPoint itemMax,
            SpatialPoint itemCenter,
            SpatialPoint zoneMin,
            SpatialPoint zoneMax,
            string matchMode)
        {
            if (string.Equals(matchMode, SpatialSearchOptionsHelper.Contains, StringComparison.OrdinalIgnoreCase))
            {
                return ItemIsInsideZone(itemMin, itemMax, zoneMin, zoneMax);
            }

            if (string.Equals(matchMode, SpatialSearchOptionsHelper.Center, StringComparison.OrdinalIgnoreCase))
            {
                return CenterIsInsideZone(itemCenter, zoneMin, zoneMax);
            }

            return ItemOverlapsZone(itemMin, itemMax, zoneMin, zoneMax);
        }

        private static bool ItemIsInsideZone(
            SpatialPoint itemMin,
            SpatialPoint itemMax,
            SpatialPoint zoneMin,
            SpatialPoint zoneMax)
        {
            if (itemMin == null || itemMax == null)
                return false;

            return itemMin.X >= zoneMin.X && itemMax.X <= zoneMax.X &&
                   itemMin.Y >= zoneMin.Y && itemMax.Y <= zoneMax.Y &&
                   itemMin.Z >= zoneMin.Z && itemMax.Z <= zoneMax.Z;
        }

        private static bool CenterIsInsideZone(
            SpatialPoint itemCenter,
            SpatialPoint zoneMin,
            SpatialPoint zoneMax)
        {
            if (itemCenter == null)
                return false;

            return itemCenter.X >= zoneMin.X && itemCenter.X <= zoneMax.X &&
                   itemCenter.Y >= zoneMin.Y && itemCenter.Y <= zoneMax.Y &&
                   itemCenter.Z >= zoneMin.Z && itemCenter.Z <= zoneMax.Z;
        }

        private static bool ItemOverlapsZone(
            SpatialPoint itemMin,
            SpatialPoint itemMax,
            SpatialPoint zoneMin,
            SpatialPoint zoneMax)
        {
            if (itemMin == null || itemMax == null)
                return false;

            return itemMin.X <= zoneMax.X && itemMax.X >= zoneMin.X &&
                   itemMin.Y <= zoneMax.Y && itemMax.Y >= zoneMin.Y &&
                   itemMin.Z <= zoneMax.Z && itemMax.Z >= zoneMin.Z;
        }
    }
}
