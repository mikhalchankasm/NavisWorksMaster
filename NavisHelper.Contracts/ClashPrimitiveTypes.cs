using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    [Flags]
    public enum ClashPrimitiveKinds
    {
        None = 0,
        Triangles = 1,
        Lines = 2,
        Points = 4
    }

    public static class ClashPrimitiveTypesParser
    {
        public static bool TryParse(IEnumerable<string> values, out ClashPrimitiveKinds kinds, out string error)
        {
            kinds = ClashPrimitiveKinds.None;
            error = null;
            if (values == null)
                return true;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                switch (value.Trim().ToLowerInvariant())
                {
                    case "triangles": kinds |= ClashPrimitiveKinds.Triangles; break;
                    case "lines": kinds |= ClashPrimitiveKinds.Lines; break;
                    case "points": kinds |= ClashPrimitiveKinds.Points; break;
                    default:
                        kinds = ClashPrimitiveKinds.None;
                        error = "Unknown primitive type '" + value + "'; expected triangles, lines, points.";
                        return false;
                }
            }
            return true;
        }
    }
}
