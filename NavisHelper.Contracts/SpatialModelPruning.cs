using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Whether a whole appended model -- or any subtree, by the same test -- can be
    /// skipped before the items inside it are scanned.
    ///
    /// `find_items_by_bbox` had exactly one lever for a large federated model: raise
    /// `maxScannedItems` to its 500 000 ceiling and hope to finish inside the host's ten
    /// second budget. Narrowing the zone did not help, because the zone was read after
    /// an item had been scanned and its box computed, and neither did
    /// `sourceFileContains`, because the scan counter increments before every filter. A
    /// caller past the cap had nothing left to try.
    ///
    /// Pruning supplied the other levers. `sourceFileContains` rules out whole models
    /// by their file name, and since the per-model walk became a subtree walk
    /// (`SpatialSubtreeWalk`), the zone rules out any item whose own box misses it:
    /// Autodesk defines `ModelItem.BoundingBox()` as the box of the item *and its
    /// children*, so the argument below, read on an item's box instead of a model's,
    /// rules out that item's whole subtree in every match mode. Narrowing the zone now
    /// reduces the scan, not only the match count.
    ///
    /// A model ruled out here costs nothing at all -- its items are never enumerated,
    /// so they never reach the counter. A subtree ruled out the same way still pays for
    /// its own item to be scanned and its box read; everything under it pays nothing.
    ///
    /// This lives in the contracts assembly, away from the Navisworks API, so the
    /// decision can be tested without a live document. The plugin supplies each model's
    /// own extents, and every scanned item's own box.
    /// </summary>
    public static class SpatialModelPruning
    {
        /// <summary>
        /// False when no item inside this model can match, whatever the match mode.
        ///
        /// Mode-independence is the whole argument, so it is worth stating. If the model's
        /// extents do not overlap the zone then:
        /// <list type="bullet">
        /// <item>no descendant box can overlap it either, ruling out `intersects`;</item>
        /// <item>a box wholly inside the zone would have to overlap it, ruling out
        /// `contains`;</item>
        /// <item>a box's centre lies within that box, hence within the model extents,
        /// ruling out `center`.</item>
        /// </list>
        /// So non-overlap rules out all three, and this needs no match mode.
        ///
        /// It rests on one assumption that cannot be checked from here: that a model's
        /// extents enclose its descendants' boxes. That is what model extents mean, and it
        /// is verified on the rig by running the same query with pruning disabled and
        /// comparing `matchedItemCount` -- a wrong assumption can only lose matches, never
        /// invent them, so the comparison is the test.
        ///
        /// The same test prunes subtrees, not only models: the plugin reads each scanned
        /// item's own box -- which Autodesk defines as enclosing its children, the
        /// enclosure assumption above made local -- and the walk skips that item's
        /// children when this returns false. `SpatialSubtreeWalk` owns the skipping
        /// mechanics; this method only answers the question, for any box that encloses
        /// its descendants.
        ///
        /// Unknown extents are never pruned. A model whose box could not be read is scanned,
        /// because "we could not tell" must not read as "nothing here".
        /// </summary>
        public static bool ModelExtentsCanHoldAMatch(
            SpatialPoint modelMin,
            SpatialPoint modelMax,
            SpatialPoint zoneMin,
            SpatialPoint zoneMax)
        {
            if (modelMin == null || modelMax == null || zoneMin == null || zoneMax == null)
                return true;

            if (!IsFinite(modelMin) || !IsFinite(modelMax) || !IsFinite(zoneMin) || !IsFinite(zoneMax))
                return true;

            return modelMin.X <= zoneMax.X && modelMax.X >= zoneMin.X &&
                   modelMin.Y <= zoneMax.Y && modelMax.Y >= zoneMin.Y &&
                   modelMin.Z <= zoneMax.Z && modelMax.Z >= zoneMin.Z;
        }

        /// <summary>
        /// False when this model's file cannot satisfy a `sourceFileContains` filter.
        ///
        /// Separate from the extents test because it rests on a different and weaker
        /// assumption: that the items inside a model report that model's source file. That
        /// holds for an appended file, which is how `document.Models` is populated, and the
        /// same rig comparison covers it -- if a model can serve items from some other file,
        /// pruning on the model's name would lose them and the counts would differ.
        ///
        /// An empty filter prunes nothing, and an unknown file name is never pruned, for the
        /// same reason unknown extents are not.
        /// </summary>
        public static bool ModelFileCanSatisfyFilter(string modelSourceFile, string sourceFileContains)
        {
            var filter = (sourceFileContains ?? string.Empty).Trim();
            if (filter.Length == 0)
                return true;

            if (string.IsNullOrWhiteSpace(modelSourceFile))
                return true;

            return modelSourceFile.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFinite(SpatialPoint point) =>
            !double.IsNaN(point.X) && !double.IsInfinity(point.X) &&
            !double.IsNaN(point.Y) && !double.IsInfinity(point.Y) &&
            !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);
    }
}
