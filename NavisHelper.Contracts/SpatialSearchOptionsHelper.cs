using System;

namespace NavisHelper.Agent.Contracts
{
    public static class SpatialSearchOptionsHelper
    {
        public const string Intersects = "intersects";
        public const string Contains = "contains";
        public const string Center = "center";

        public const int DefaultMaxScannedItems = 100000;
        public const int MaxMaxScannedItems = 500000;
        public const int DefaultMaxResults = 5000;
        public const int MaxMaxResults = 10000;
        public const int DefaultPreviewLimit = 10;
        public const int MaxPreviewLimit = 50;

        public static string NormalizeMatchMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized) || normalized == Intersects || normalized == "overlaps")
                return Intersects;
            if (normalized == Contains || normalized == "inside")
                return Contains;
            if (normalized == Center || normalized == "centre")
                return Center;
            throw new ArgumentException("matchMode must be intersects, contains, or center.");
        }

        public static int ClampMaxScannedItems(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultMaxScannedItems), 1, MaxMaxScannedItems);
        }

        public static int ClampMaxResults(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultMaxResults), 1, MaxMaxResults);
        }

        public static int ClampPreviewLimit(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultPreviewLimit), 1, MaxPreviewLimit);
        }

        public static void ValidateBounds(SpatialPoint min, SpatialPoint max)
        {
            if (min == null || max == null)
                throw new ArgumentException("Both min and max coordinates are required.");
            if (!IsFinite(min.X) || !IsFinite(min.Y) || !IsFinite(min.Z) ||
                !IsFinite(max.X) || !IsFinite(max.Y) || !IsFinite(max.Z))
            {
                throw new ArgumentException("Spatial coordinates must be finite numbers.");
            }
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                throw new ArgumentException("min coordinates must not exceed max coordinates.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
