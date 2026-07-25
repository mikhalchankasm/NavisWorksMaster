using System;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashNumericOptionHelper
    {
        public static int ClampNonNegative(int? value)
        {
            var normalized = value.GetValueOrDefault(0);
            return normalized < 0 ? 0 : normalized;
        }

        public static bool TryNormalizePositiveDouble(double? value, double defaultValue, out double result)
        {
            result = value.GetValueOrDefault(defaultValue);
            return !(result <= 0);
        }

        public static bool TryNormalizeNonNegativeDouble(double value, out double result)
        {
            result = value;
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        public static bool TryNormalizeUnitDouble(double? value, double defaultValue, out double result)
        {
            result = value.GetValueOrDefault(defaultValue);
            return !(result < 0 || result > 1);
        }
    }
}
