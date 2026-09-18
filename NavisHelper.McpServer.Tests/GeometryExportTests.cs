using System.Globalization;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class GeometryExportTests
{
    [Theory]
    [InlineData(@"C:\exports\mesh.obj", true)]
    [InlineData("D:/exports/mesh.obj", true)]
    [InlineData(@"\\server\share\mesh.obj", true)]
    [InlineData(@"\exports\mesh.obj", false)]
    [InlineData("C:mesh.obj", false)]
    [InlineData("mesh.obj", false)]
    [InlineData(null, false)]
    public void OutputPathRequiresAnExplicitDriveOrShare(string path, bool expected)
    {
        Assert.Equal(expected, GeometryExportPath.IsFullyQualifiedWindowsPath(path));
    }

    [Fact]
    public void ComOneBasedArraysRetainPrecision()
    {
        var source=Array.CreateInstance(typeof(double),new[] {3},new[] {1});
        source.SetValue(1234567.8901234567,1); source.SetValue(-2.25,2); source.SetValue(0.0,3);
        Assert.Equal(new[] {1234567.8901234567,-2.25,0}, GeometryExportMath.ReadArray(source,3));
        Assert.Throws<InvalidOperationException>(()=>GeometryExportMath.ReadArray(new[] {double.NaN,0,0},3));
        Assert.Throws<InvalidOperationException>(()=>GeometryExportMath.ReadArray(new double[2,2],4));
    }

    [Fact]
    public void RotatedScaledTranslatedAndMirroredFrameRoundTrips()
    {
        var matrix=new double[] {0,2,0,0, -3,0,0,0, 0,0,-4,0, 100,200,300,1};
        var p=new[] {1.25,-2.5,3.75};
        var world=GeometryExportMath.Transform(matrix,p);
        Assert.Equal(new[] {107.5,202.5,285},world);
        var local=GeometryExportMath.Transform(GeometryExportMath.Inverse(matrix),world);
        for(var i=0;i<3;i++) Assert.Equal(p[i],local[i],10);
        Assert.Throws<InvalidOperationException>(()=>GeometryExportMath.Inverse(new double[16]));
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("jsonl")]
    [InlineData("ply")]
    [InlineData("stl")]
    public void FormatsKeepTrianglesFragmentIdentityAndInvariantNumbers(string format)
    {
        var dir=Path.Combine(Path.GetTempPath(),"navis-geometry-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var oldCulture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("ru-RU");
            var spool=Path.Combine(dir,"triangles.bin");
            using(var binary=new BinaryWriter(File.Create(spool)))
                for(var triangle=0;triangle<2;triangle++)
                    foreach(var d in new[] {0.5,0,0, 1.5,0,0, 0.5,1,0}) binary.Write(d);
            var fragments=Enumerable.Range(1,2).Select(i=>new GeometryFragmentInfo
            {
                ItemId=i, FragmentId=i, DisplayName="/Штуцер", Path="6501.5.nwd / /Штуцер\n# unsafe label",
                TriangleCount=1, FragmentToWorld=GeometryExportMath.Identity, OutputToWorld=GeometryExportMath.Identity,
            }).ToList();
            var output=Path.Combine(dir,"mesh."+format);
            GeometryMeshWriter.Write(output,spool,format,"fragment","world","Millimeters",fragments,()=>{});
            var text=File.ReadAllText(output);
            Assert.Contains("0.5",text);
            Assert.DoesNotContain("0,5",text);
            Assert.DoesNotContain("\n# unsafe label",text);
            if(format=="obj")
            {
                Assert.Equal(6,text.Split('\n').Count(line=>line.StartsWith("v ")));
                Assert.Contains("f 1 2 3",text); Assert.Contains("f 4 5 6",text);
                Assert.Equal(2,text.Split('\n').Count(line=>line.StartsWith("# fragment ")));
                Assert.Contains("g item_2_fragment_2_",text);
            }
            else if(format=="jsonl")
            {
                var records=File.ReadLines(output).Select(JObject.Parse).ToList();
                var triangles=records.Where(j=>(string)j["type"]=="triangle").ToList();
                Assert.Equal(2,triangles.Count);
                Assert.Equal(fragments[0].Path,(string)triangles[0]["path"]);
                Assert.Equal(2,(int)triangles[1]["itemId"]);
            }
            else if(format=="ply")
            {
                Assert.Contains("element vertex 6",text); Assert.Contains("element face 2",text);
                Assert.Contains("3 0 1 2 1 1",text); Assert.Contains("3 3 4 5 2 2",text);
            }
            else
            {
                Assert.Equal(2,text.Split('\n').Count(line=>line.StartsWith("facet normal")));
                var solid=text.Split('\n').First(line=>line.StartsWith("solid ")).Substring(6).Trim();
                Assert.Equal(fragments[0].Path,(string)JObject.Parse(Uri.UnescapeDataString(solid))["fragment"]["path"]);
            }
        }
        finally { CultureInfo.CurrentCulture=oldCulture; Directory.Delete(dir,true); }
    }

    [Fact]
    public void WriterHonorsDeadlineBeforeReadingMesh()
    {
        var dir=Path.Combine(Path.GetTempPath(),"navis-geometry-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var spool=Path.Combine(dir,"spool"); File.WriteAllBytes(spool,Array.Empty<byte>());
            Assert.Throws<TimeoutException>(()=>GeometryMeshWriter.Write(Path.Combine(dir,"output"),spool,"obj","fragment","world","Meters",
                new[] {new GeometryFragmentInfo { TriangleCount=1, Path="a" }},()=>throw new TimeoutException()));
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact]
    public void JsonNumbersAndMatricesUseSeventeenDigitsAndRemainNumeric()
    {
        var dir=Path.Combine(Path.GetTempPath(),"navis-geometry-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var spool=Path.Combine(dir,"spool");
            using(var writer=new BinaryWriter(File.Create(spool)))
                foreach(var d in new[] {0.1,0,0, 1,0,0, 0,1,0}) writer.Write(d);
            var matrix=GeometryExportMath.Identity; matrix[12]=0.1;
            var output=Path.Combine(dir,"mesh.jsonl");
            GeometryMeshWriter.Write(output,spool,"jsonl","fragment","world","Meters",
                new[] {new GeometryFragmentInfo {TriangleCount=1,FragmentToWorld=matrix,OutputToWorld=matrix,Path="a"}},()=>{});
            var text=File.ReadAllText(output);
            Assert.Contains("0.10000000000000001",text);
            Assert.DoesNotContain("\r",text);
            var records=File.ReadLines(output).Select(JObject.Parse).ToList();
            Assert.Equal(0.1,(double)records[1]["fragment"]["fragmentToWorld"][12]);
            Assert.Equal(0.1,(double)records[2]["vertices"][0][0]);
            Assert.Equal(JTokenType.Float,records[2]["vertices"][0][0].Type);
        }
        finally { Directory.Delete(dir,true); }
    }
}
