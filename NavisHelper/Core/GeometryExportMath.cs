using System;
using System.Globalization;

namespace NavisHelper.Core
{
    internal static class GeometryExportMath
    {
        public static double[] Identity => new double[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };

        public static double[] ReadArray(object value, int length)
        {
            var array = value as Array;
            if (array == null || array.Rank != 1 || array.Length != length)
                throw new InvalidOperationException("Invalid COM geometry array length.");
            var result = new double[length];
            for (var i = 0; i < length; i++)
            {
                result[i] = Convert.ToDouble(array.GetValue(array.GetLowerBound(0) + i), CultureInfo.InvariantCulture);
                if (double.IsNaN(result[i]) || double.IsInfinity(result[i]))
                    throw new InvalidOperationException("Geometry contains non-finite coordinates.");
            }
            return result;
        }

        // COM matrices are column-major: translation at offsets 12, 13, 14.
        public static double[] Transform(double[] m, double[] p)
        {
            var w = m[3]*p[0] + m[7]*p[1] + m[11]*p[2] + m[15];
            if (Math.Abs(w) < 1e-20) throw new InvalidOperationException("Invalid homogeneous geometry transform.");
            var result = new double[3];
            for (var i = 0; i < 3; i++)
            {
                result[i] = (m[i]*p[0] + m[4+i]*p[1] + m[8+i]*p[2] + m[12+i]) / w;
                if (double.IsNaN(result[i]) || double.IsInfinity(result[i])) throw new InvalidOperationException("Non-finite transformed geometry.");
            }
            return result;
        }

        public static double[] Inverse(double[] m)
        {
            var a = new double[4,8];
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 4; c++) { a[r,c] = m[c*4+r]; a[r,c+4] = r == c ? 1 : 0; }
            for (var c = 0; c < 4; c++)
            {
                var pivot = c;
                for (var r = c+1; r < 4; r++) if (Math.Abs(a[r,c]) > Math.Abs(a[pivot,c])) pivot = r;
                if (Math.Abs(a[pivot,c]) < 1e-20) throw new InvalidOperationException("Singular item-local frame.");
                for (var j = 0; j < 8; j++) { var t=a[c,j]; a[c,j]=a[pivot,j]; a[pivot,j]=t; }
                var divisor=a[c,c];
                for (var j=0;j<8;j++) a[c,j]/=divisor;
                for (var r=0;r<4;r++) if (r!=c)
                {
                    var factor=a[r,c];
                    for (var j=0;j<8;j++) a[r,j]-=factor*a[c,j];
                }
            }
            var result=new double[16];
            for(var r=0;r<4;r++) for(var c=0;c<4;c++) result[c*4+r]=a[r,c+4];
            return result;
        }
    }
}
