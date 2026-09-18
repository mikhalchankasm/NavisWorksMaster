using System.Globalization;
using Autodesk.Navisworks.Api;

namespace NavisHelper.Agent.Services
{
    internal static class PropertyValueFormatter
    {
        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo> ModernNumericAccessors =
            new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo>
            {
                { "Int64", typeof(VariantData).GetMethod("ToInt64", System.Type.EmptyTypes) },
                { "Nat32", typeof(VariantData).GetMethod("ToNat32", System.Type.EmptyTypes) },
                { "Nat64", typeof(VariantData).GetMethod("ToNat64", System.Type.EmptyTypes) },
            };
        public static string Clean(VariantData value)
        {
            if (value == null || value.IsNone) return string.Empty;
            if (value.IsDisplayString) return value.ToDisplayString();
            if (value.IsIdentifierString) return value.ToIdentifierString();
            if (value.IsAnyDouble) return value.ToAnyDouble().ToString("G17", CultureInfo.InvariantCulture);
            if (value.IsInt32) return value.ToInt32().ToString(CultureInfo.InvariantCulture);
            // These numeric kinds were added after the 2024/2025 SDKs.
            // Resolve only known numeric accessors to keep the same source matrix.
            var kind = value.DataType.ToString();
            if (kind == "Int64" || kind == "Nat32" || kind == "Nat64")
            {
                var method = ModernNumericAccessors[kind];
                if (method != null) return System.Convert.ToString(method.Invoke(value, null), CultureInfo.InvariantCulture);
            }
            if (value.IsBoolean) return value.ToBoolean() ? "true" : "false";
            if (value.IsDateTime) return value.ToDateTime().ToString("O", CultureInfo.InvariantCulture);
            if (value.IsNamedConstant) return value.ToNamedConstant().DisplayName;
            if (value.IsPoint2D)
            {
                var p = value.ToPoint2D();
                return string.Format(CultureInfo.InvariantCulture, "{0:G17},{1:G17}", p.X, p.Y);
            }
            if (value.IsPoint3D)
            {
                var p = value.ToPoint3D();
                return string.Format(CultureInfo.InvariantCulture, "{0:G17},{1:G17},{2:G17}", p.X, p.Y, p.Z);
            }
            return value.ToString();
        }
    }
}
