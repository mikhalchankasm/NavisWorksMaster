using System;
using System.Collections.Generic;

namespace NavisHelper.Core
{
    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        private NaturalStringComparer()
        {
        }

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            var ix = 0;
            var iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                var cx = x[ix];
                var cy = y[iy];

                if (char.IsDigit(cx) && char.IsDigit(cy))
                {
                    var numberCompare = CompareNumberRuns(x, ref ix, y, ref iy);
                    if (numberCompare != 0)
                        return numberCompare;
                    continue;
                }

                var charCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (charCompare != 0)
                    return charCompare;

                ix++;
                iy++;
            }

            return x.Length.CompareTo(y.Length);
        }

        private static int CompareNumberRuns(string x, ref int ix, string y, ref int iy)
        {
            var xStart = ix;
            var yStart = iy;

            while (ix < x.Length && char.IsDigit(x[ix]))
                ix++;
            while (iy < y.Length && char.IsDigit(y[iy]))
                iy++;

            var xSignificantStart = xStart;
            var ySignificantStart = yStart;
            while (xSignificantStart < ix && x[xSignificantStart] == '0')
                xSignificantStart++;
            while (ySignificantStart < iy && y[ySignificantStart] == '0')
                ySignificantStart++;

            var xSignificantLength = ix - xSignificantStart;
            var ySignificantLength = iy - ySignificantStart;
            if (xSignificantLength != ySignificantLength)
                return xSignificantLength.CompareTo(ySignificantLength);

            for (var offset = 0; offset < xSignificantLength; offset++)
            {
                var digitCompare = x[xSignificantStart + offset].CompareTo(y[ySignificantStart + offset]);
                if (digitCompare != 0)
                    return digitCompare;
            }

            return (ix - xStart).CompareTo(iy - yStart);
        }
    }
}
