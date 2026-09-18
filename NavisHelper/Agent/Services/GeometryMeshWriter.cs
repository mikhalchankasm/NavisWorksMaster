using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Services
{
    // Reads a bounded binary spool instead of retaining a mesh in host memory.
    internal static class GeometryMeshWriter
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        { ContractResolver = new CamelCasePropertyNamesContractResolver(), Culture = CultureInfo.InvariantCulture,
            Converters = new List<JsonConverter> { new ExactDoubleConverter() } };
        private static string Json(object value) => JsonConvert.SerializeObject(value, JsonSettings);
        private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        private static string Vertex(double[] p) => string.Join(" ", p.Select(Number));
        private static double[] ReadVertex(BinaryReader reader) => new[] { reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble() };

        public static void Write(string output, string spool, string format, string groupBy, string space,
            string units, IList<GeometryFragmentInfo> fragments, Action checkDeadline)
        {
            var metadata = new { schemaVersion=1, coordinateSpace=space, units=space=="world" ? units : "item_native", documentUnits=units,
                matrixLayout="column-major", localFrame="item_local uses the first owned fragment frame per geometry item", tessellation="native, no decimation or smoothing" };
            using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            using (var reader = new BinaryReader(File.OpenRead(spool)))
            {
                writer.NewLine = "\n";
                var triangles = fragments.Sum(f => f.TriangleCount);
                if (format == "ply")
                {
                    writer.WriteLine("ply\nformat ascii 1.0");
                    writer.WriteLine("comment " + Json(metadata));
                    foreach(var f in fragments) writer.WriteLine("comment fragment " + Json(f));
                    writer.WriteLine("element vertex " + (triangles*3).ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("property double x\nproperty double y\nproperty double z");
                    writer.WriteLine("element face " + triangles.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine("property list uchar int vertex_indices\nproperty int item_id\nproperty int fragment_id\nend_header");
                    for(var i=0;i<triangles*3;i++) { checkDeadline(); writer.WriteLine(Vertex(ReadVertex(reader))); }
                    var index=0;
                    foreach(var f in fragments) for(var i=0;i<f.TriangleCount;i++)
                    {
                        checkDeadline();
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"3 {0} {1} {2} {3} {4}",index,index+1,index+2,f.ItemId,f.FragmentId));
                        index+=3;
                    }
                    return;
                }
                if(format=="obj") writer.WriteLine("# " + Json(metadata));
                if(format=="jsonl") writer.WriteLine(Json(new { type="metadata", metadata }));
                long vertexIndex=1;
                foreach(var f in fragments)
                {
                    checkDeadline();
                    var group="item_" + f.ItemId.ToString(CultureInfo.InvariantCulture) +
                        (groupBy=="fragment" ? "_fragment_" + f.FragmentId.ToString(CultureInfo.InvariantCulture) : "") + "_" + Uri.EscapeDataString(f.Path ?? "");
                    if(format=="obj") { writer.WriteLine("# fragment " + Json(f)); writer.WriteLine("g " + group); }
                    if(format=="jsonl") writer.WriteLine(Json(new { type="fragment", fragment=f }));
                    // STL has no metadata fields; its solid name carries URL-encoded JSON.
                    var solid=Uri.EscapeDataString(Json(new { metadata, fragment=f }));
                    if(format=="stl") writer.WriteLine("solid " + solid);
                    for(var i=0;i<f.TriangleCount;i++)
                    {
                        checkDeadline();
                        var a=ReadVertex(reader); var b=ReadVertex(reader); var c=ReadVertex(reader);
                        if(format=="obj")
                        {
                            writer.WriteLine("v " + Vertex(a)); writer.WriteLine("v " + Vertex(b)); writer.WriteLine("v " + Vertex(c));
                            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"f {0} {1} {2}",vertexIndex,vertexIndex+1,vertexIndex+2));
                            vertexIndex+=3;
                        }
                        else if(format=="jsonl") writer.WriteLine(Json(new { type="triangle", itemId=f.ItemId, fragmentId=f.FragmentId, path=f.Path, vertices=new[] {a,b,c} }));
                        else
                        {
                            var n = new[] { (b[1]-a[1])*(c[2]-a[2])-(b[2]-a[2])*(c[1]-a[1]),
                                (b[2]-a[2])*(c[0]-a[0])-(b[0]-a[0])*(c[2]-a[2]),
                                (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]) };
                            var length=Math.Sqrt(n.Sum(v=>v*v));
                            if(length>0) for(var j=0;j<3;j++) n[j]/=length;
                            writer.WriteLine("facet normal " + Vertex(n) + "\nouter loop");
                            writer.WriteLine("vertex " + Vertex(a)); writer.WriteLine("vertex " + Vertex(b)); writer.WriteLine("vertex " + Vertex(c));
                            writer.WriteLine("endloop\nendfacet");
                        }
                    }
                    if(format=="stl") writer.WriteLine("endsolid " + solid);
                }
            }
        }

        // Json.NET's default double writer uses R, which does not guarantee a
        // Double round-trip on .NET Framework x64. This also covers matrices.
        private sealed class ExactDoubleConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(double);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidOperationException("Non-finite mesh number.");
                writer.WriteRawValue(Number(number));
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
                => throw new NotSupportedException();
        }
    }
}
