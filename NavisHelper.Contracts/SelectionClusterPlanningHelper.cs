using System;

namespace NavisHelper.Agent.Contracts
{
    public static class SelectionClusterPlanningHelper
    {
        public static int GetCountClusterCount(int itemCount, int targetSize)
        {
            if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
            if (targetSize < 1) throw new ArgumentOutOfRangeException(nameof(targetSize));
            return itemCount == 0 ? 0 : (itemCount + targetSize - 1) / targetSize;
        }

        public static long GetGridIndex(double coordinate, double gridSize)
        {
            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate));
            if (double.IsNaN(gridSize) || double.IsInfinity(gridSize) || gridSize <= 0) throw new ArgumentOutOfRangeException(nameof(gridSize));
            return (long)Math.Floor(coordinate / gridSize);
        }
    }
}
