using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashZoneIfcDocument
    {
        public string ProjectName { get; set; }
        public string SiteName { get; set; }
        public string RunId { get; set; }
        public string TimeStamp { get; set; }
        public string PropertySetName { get; set; } = "NH_ClashZone";
        public int LatitudeBands { get; set; } = 10;
        public int LongitudeBands { get; set; } = 20;
        public IList<ClashZoneMarkerStyle> Styles { get; set; } = new List<ClashZoneMarkerStyle>();
        public IList<ClashZoneMarker> Markers { get; set; } = new List<ClashZoneMarker>();
    }

    public sealed class ClashZoneMarkerStyle
    {
        public string Key { get; set; }
        public double Red { get; set; }
        public double Green { get; set; }
        public double Blue { get; set; }
        public double Transparency { get; set; }
    }

    public sealed class ClashZoneMarker
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RadiusMm { get; set; }
        public string StyleKey { get; set; }
        public IList<ClashZoneMarkerProperty> Properties { get; set; } = new List<ClashZoneMarkerProperty>();
    }

    public sealed class ClashZoneMarkerProperty
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    /// <summary>Writes an IFC4 STEP document in millimetres without accessing the filesystem.</summary>
    public static class ClashZoneIfcWriter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Write(ClashZoneIfcDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var styles = Validate(document);
            var writer = new StepWriter(document);
            return writer.Write(styles);
        }

        private static Dictionary<string, ClashZoneMarkerStyle> Validate(ClashZoneIfcDocument document)
        {
            if (document.LatitudeBands < 3)
                throw new ArgumentOutOfRangeException(nameof(document.LatitudeBands));
            if (document.LongitudeBands < 3)
                throw new ArgumentOutOfRangeException(nameof(document.LongitudeBands));
            if (document.Styles == null || document.Markers == null)
                throw new ArgumentException("Styles and markers must be supplied.", nameof(document));
            var styles = new Dictionary<string, ClashZoneMarkerStyle>(StringComparer.Ordinal);
            foreach (var style in document.Styles)
            {
                if (style == null || style.Key == null || styles.ContainsKey(style.Key))
                    throw new ArgumentException("Style keys must be non-null and unique.", nameof(document));
                CheckChannel(style.Red, nameof(style.Red));
                CheckChannel(style.Green, nameof(style.Green));
                CheckChannel(style.Blue, nameof(style.Blue));
                CheckChannel(style.Transparency, nameof(style.Transparency));
                styles.Add(style.Key, style);
            }
            foreach (var marker in document.Markers)
            {
                if (marker == null || !Finite(marker.X) || !Finite(marker.Y) || !Finite(marker.Z)
                    || !Finite(marker.RadiusMm) || marker.RadiusMm <= 0
                    || marker.StyleKey == null || !styles.ContainsKey(marker.StyleKey))
                    throw new ArgumentException("Markers require finite coordinates, a positive radius and a known style.", nameof(document));
            }
            return styles;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void CheckChannel(double value, string name)
        {
            if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
        }

        private static string Real(double value)
        {
            if (!Finite(value)) throw new ArgumentException("IFC real values must be finite.", nameof(value));
            return RealText(value.ToString("R", Invariant));
        }

        private static string RealText(string text)
        {
            if (text.IndexOf('.') >= 0) return text;
            var exponent = text.IndexOf('E');
            return exponent < 0 ? text + "." : text.Insert(exponent, ".");
        }

        private static string Text(string value)
        {
            var result = new StringBuilder("'");
            var unicode = false;
            foreach (var ch in value ?? string.Empty)
            {
                if (ch >= 128)
                {
                    if (!unicode) result.Append("\\X2\\");
                    unicode = true;
                    result.Append(((int)ch).ToString("X4", Invariant));
                    continue;
                }
                if (unicode) result.Append("\\X0\\");
                unicode = false;
                if (ch < 32 || ch == 127) continue;
                if (ch == '\'' || ch == '\\') result.Append(ch);
                result.Append(ch);
            }
            if (unicode) result.Append("\\X0\\");
            return result.Append('\'').ToString();
        }

        private static string PropertyValue(object value)
        {
            if (value is bool) return (bool)value ? "IFCBOOLEAN(.T.)" : "IFCBOOLEAN(.F.)";
            if (value is int || value is long || value is short)
                return "IFCINTEGER(" + Convert.ToString(value, Invariant) + ")";
            if (value is double) return "IFCREAL(" + Real((double)value) + ")";
            if (value is float)
            {
                if (!Finite((float)value)) throw new ArgumentException("IFC real values must be finite.", nameof(value));
                return "IFCREAL(" + RealText(((float)value).ToString("R", Invariant)) + ")";
            }
            if (value is decimal)
                return "IFCREAL(" + RealText(((decimal)value).ToString(Invariant)) + ")";
            var label = Convert.ToString(value, Invariant) ?? string.Empty;
            if (label.Length > 255) label = label.Substring(0, 255);
            return "IFCLABEL(" + Text(label) + ")";
        }

        private static string Tuple(params string[] values) => "(" + string.Join(",", values) + ")";

        private sealed class StepWriter
        {
            private readonly ClashZoneIfcDocument document;
            private readonly StringBuilder output = new StringBuilder();
            private int nextId;

            public StepWriter(ClashZoneIfcDocument document) { this.document = document; }

            private string Entity(string type, params string[] args)
            {
                var reference = "#" + (++nextId).ToString(Invariant);
                output.Append(reference).Append('=').Append(type).Append(Tuple(args)).Append(";\n");
                return reference;
            }

            private string GlobalId(string kind, int index = 0)
            {
                const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
                byte[] digest;
                using (var sha = SHA256.Create())
                    digest = sha.ComputeHash(Encoding.UTF8.GetBytes(document.RunId + "|" + document.ProjectName
                        + "|" + kind + "|" + index.ToString(Invariant)));
                var encoded = new StringBuilder(22);
                // A 128-bit big-endian number occupies 22 base64 digits, with four leading zero bits.
                for (var digit = 0; digit < 22; digit++)
                {
                    var value = 0;
                    for (var offset = 0; offset < 6; offset++)
                    {
                        var bit = digit * 6 + offset - 4;
                        value = (value << 1) | (bit < 0 ? 0 : (digest[bit / 8] >> (7 - bit % 8)) & 1);
                    }
                    encoded.Append(alphabet[value]);
                }
                return Text(encoded.ToString());
            }

            public string Write(Dictionary<string, ClashZoneMarkerStyle> styles)
            {
                output.Append("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [ReferenceView]'),'2;1');\n")
                    .Append("FILE_NAME('zones.ifc',").Append(Text(document.TimeStamp))
                    .Append(",(''),(''),'NavisHelper clash zones','',' ');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n");
                var origin = Entity("IFCCARTESIANPOINT", "(0.,0.,0.)");
                var z = Entity("IFCDIRECTION", "(0.,0.,1.)");
                var x = Entity("IFCDIRECTION", "(1.,0.,0.)");
                var axis = Entity("IFCAXIS2PLACEMENT3D", origin, z, x);
                var context = Entity("IFCGEOMETRICREPRESENTATIONCONTEXT", "$", "'Model'", "3", "1.E-05", axis, "$");
                var body = Entity("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "'Body'", "'Model'", "*", "*", "*", "*", context, "$", ".MODEL_VIEW.", "$");
                var mm = Entity("IFCSIUNIT", "*", ".LENGTHUNIT.", ".MILLI.", ".METRE.");
                var units = Entity("IFCUNITASSIGNMENT", Tuple(mm));
                var project = Entity("IFCPROJECT", GlobalId("IFCPROJECT"), "$", Text(document.ProjectName), "$", "$", "$", "$", Tuple(context), units);
                var sitePlace = Entity("IFCLOCALPLACEMENT", "$", axis);
                var site = Entity("IFCSITE", GlobalId("IFCSITE"), "$", Text(document.SiteName), "$", "$", sitePlace, "$", "$", ".ELEMENT.", "$", "$", "$", "$", "$");
                Entity("IFCRELAGGREGATES", GlobalId("IFCRELAGGREGATES"), "$", "$", "$", project, Tuple(site));
                var map = Sphere(axis, body);
                var styleRefs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var marker in document.Markers)
                {
                    if (styleRefs.ContainsKey(marker.StyleKey)) continue;
                    var style = styles[marker.StyleKey];
                    var colour = Entity("IFCCOLOURRGB", "$", Real(style.Red), Real(style.Green), Real(style.Blue));
                    var shading = Entity("IFCSURFACESTYLESHADING", colour, Real(style.Transparency));
                    styleRefs.Add(style.Key, Entity("IFCSURFACESTYLE", Text(style.Key), ".BOTH.", Tuple(shading)));
                }
                var proxies = new List<string>();
                for (var index = 0; index < document.Markers.Count; index++)
                    proxies.Add(Marker(document.Markers[index], index, map, styleRefs, axis, body, sitePlace));
                if (proxies.Count > 0)
                    Entity("IFCRELCONTAINEDINSPATIALSTRUCTURE", GlobalId("IFCRELCONTAINEDINSPATIALSTRUCTURE"), "$", "$", "$", Tuple(proxies.ToArray()), site);
                return output.Append("ENDSEC;\nEND-ISO-10303-21;\n").ToString();
            }

            private string Sphere(string axis, string body)
            {
                var latitudes = document.LatitudeBands;
                var longitudes = document.LongitudeBands;
                var points = new List<string> { "(0.,0.,1.)" };
                for (var latitude = 1; latitude < latitudes; latitude++)
                {
                    var theta = Math.PI * latitude / latitudes;
                    for (var longitude = 0; longitude < longitudes; longitude++)
                    {
                        var phi = 2 * Math.PI * longitude / longitudes;
                        points.Add(Tuple(Real(Math.Sin(theta) * Math.Cos(phi)),
                            Real(Math.Sin(theta) * Math.Sin(phi)), Real(Math.Cos(theta))));
                    }
                }
                points.Add("(0.,0.,-1.)");
                var faces = new List<string>();
                for (var longitude = 0; longitude < longitudes; longitude++)
                {
                    var next = (longitude + 1) % longitudes;
                    faces.Add(Face(1, 2 + longitude, 2 + next));
                    for (var ring = 0; ring < latitudes - 2; ring++)
                    {
                        var top = 2 + ring * longitudes;
                        var bottom = top + longitudes;
                        faces.Add(Face(top + longitude, bottom + longitude, bottom + next));
                        faces.Add(Face(top + longitude, bottom + next, top + next));
                    }
                    var last = 2 + (latitudes - 2) * longitudes;
                    faces.Add(Face(points.Count, last + next, last + longitude));
                }
                var pointList = Entity("IFCCARTESIANPOINTLIST3D", Tuple(points.ToArray()));
                var faceSet = Entity("IFCTRIANGULATEDFACESET", pointList, "$", ".T.", Tuple(faces.ToArray()), "$");
                var representation = Entity("IFCSHAPEREPRESENTATION", body, "'Body'", "'Tessellation'", Tuple(faceSet));
                return Entity("IFCREPRESENTATIONMAP", axis, representation);
            }

            private static string Face(int a, int b, int c)
                => Tuple(a.ToString(Invariant), b.ToString(Invariant), c.ToString(Invariant));

            private string Marker(ClashZoneMarker marker, int index, string map,
                Dictionary<string, string> styles, string axis, string body, string sitePlace)
            {
                var point = Entity("IFCCARTESIANPOINT", Tuple(Real(marker.X), Real(marker.Y), Real(marker.Z)));
                var transform = Entity("IFCCARTESIANTRANSFORMATIONOPERATOR3D", "$", "$", point, Real(marker.RadiusMm), "$");
                var mapped = Entity("IFCMAPPEDITEM", map, transform);
                Entity("IFCSTYLEDITEM", mapped, Tuple(styles[marker.StyleKey]), "$");
                var representation = Entity("IFCSHAPEREPRESENTATION", body, "'Body'", "'MappedRepresentation'", Tuple(mapped));
                var shape = Entity("IFCPRODUCTDEFINITIONSHAPE", "$", "$", Tuple(representation));
                var placement = Entity("IFCLOCALPLACEMENT", sitePlace, axis);
                var proxy = Entity("IFCBUILDINGELEMENTPROXY", GlobalId("IFCBUILDINGELEMENTPROXY", index), "$", Text(marker.Name), "$", "'ClashZone'", placement, shape, Text(marker.Name), ".USERDEFINED.");
                var properties = new List<string>();
                if (marker.Properties != null)
                    foreach (var property in marker.Properties)
                        if (property != null && property.Value != null)
                            properties.Add(Entity("IFCPROPERTYSINGLEVALUE", Text(property.Name), "$", PropertyValue(property.Value), "$"));
                // IFC4 requires at least one property in a property set; a marker without values gets none.
                if (properties.Count == 0)
                    return proxy;
                var set = Entity("IFCPROPERTYSET", GlobalId("IFCPROPERTYSET", index), "$", Text(document.PropertySetName), "$", Tuple(properties.ToArray()));
                Entity("IFCRELDEFINESBYPROPERTIES", GlobalId("IFCRELDEFINESBYPROPERTIES", index), "$", "$", "$", Tuple(proxy), set);
                return proxy;
            }
        }
    }
}
