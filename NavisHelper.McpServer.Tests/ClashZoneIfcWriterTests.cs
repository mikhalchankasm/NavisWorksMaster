using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for the IFC4 clash-zone marker writer (docs/CLASH_ZONES_PLAN.md, section 3, PR 3).
// The entity layout follows the file that passed the L3 format check in Navisworks 2027 on 2026-10-14.
public sealed class ClashZoneIfcWriterTests
{
    [Fact]
    public void Write_ProducesAnAsciiIfc4FileWithClosedReferences()
    {
        var text = ClashZoneIfcWriter.Write(Document(Marker("Z-0001", 1200.5, -300, 45, 300, "new")));

        Assert.StartsWith("ISO-10303-21;", text);
        Assert.Contains("FILE_SCHEMA(('IFC4'));", text);
        Assert.EndsWith("END-ISO-10303-21;", text.TrimEnd());
        Assert.All(text, ch => Assert.True(ch < 128, "non-ASCII character U+" + ((int)ch).ToString("X4")));

        var model = StepModel.Parse(text);
        Assert.NotEmpty(model.Entities);
        foreach (var entity in model.Entities.Values)
            foreach (var reference in Regex.Matches(entity.Arguments, @"#(\d+)").Cast<Match>())
                Assert.True(model.Entities.ContainsKey(int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture)),
                    "#" + entity.Id + " refers to undefined " + reference.Value);
    }

    [Fact]
    public void Write_UsesMillimetresOneSharedSphereAndOneProxyPerMarker()
    {
        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document(
            Marker("Z-0001", 0, 0, 0, 150, "new"),
            Marker("Z-0002", 1000, 0, 0, 200, "persisting"),
            Marker("Z-0003", 2000, 0, 0, 250, "new"))));

        Assert.Single(model.OfType("IFCPROJECT"));
        Assert.Single(model.OfType("IFCSITE"));
        Assert.Equal("*,.LENGTHUNIT.,.MILLI.,.METRE.", Assert.Single(model.OfType("IFCSIUNIT")).Arguments);
        Assert.Single(model.OfType("IFCTRIANGULATEDFACESET"));
        Assert.Single(model.OfType("IFCREPRESENTATIONMAP"));
        Assert.Equal(3, model.OfType("IFCMAPPEDITEM").Count);
        Assert.Equal(3, model.OfType("IFCBUILDINGELEMENTPROXY").Count);
        Assert.Equal(3, model.OfType("IFCSTYLEDITEM").Count);
        Assert.Equal(3, model.OfType("IFCPROPERTYSET").Count);
        Assert.Equal(3, model.OfType("IFCRELDEFINESBYPROPERTIES").Count);

        var aggregate = Assert.Single(model.OfType("IFCRELAGGREGATES"));
        Assert.Equal(model.OfType("IFCPROJECT")[0].Ref, aggregate.Args[4]);
        Assert.Equal("(" + model.OfType("IFCSITE")[0].Ref + ")", aggregate.Args[5]);

        var containment = Assert.Single(model.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"));
        Assert.Equal(model.OfType("IFCSITE")[0].Ref, containment.Args[5]);
        Assert.Equal(
            model.OfType("IFCBUILDINGELEMENTPROXY").Select(p => p.Ref).OrderBy(r => r),
            StepModel.Items(containment.Args[4]).OrderBy(r => r));
    }

    [Fact]
    public void Write_PlacesEachMarkerAtItsCentreScaledByItsRadius()
    {
        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document(
            Marker("Z-0001", 1200.5, -300, 45, 300, "new"),
            Marker("Z-0002", 2087654.25, 0.125, -7, 150, "new"))));

        var placed = model.OfType("IFCBUILDINGELEMENTPROXY").Select(proxy =>
        {
            var shape = model.Deref(proxy.Args[6]);
            Assert.Equal("IFCPRODUCTDEFINITIONSHAPE", shape.Type);
            var representation = model.Deref(StepModel.Items(shape.Args[2]).Single());
            Assert.Equal("IFCSHAPEREPRESENTATION", representation.Type);
            Assert.Equal("'MappedRepresentation'", representation.Args[2]);
            var mapped = model.Deref(StepModel.Items(representation.Args[3]).Single());
            Assert.Equal("IFCMAPPEDITEM", mapped.Type);
            Assert.Equal(model.OfType("IFCREPRESENTATIONMAP")[0].Ref, mapped.Args[0]);
            var op = model.Deref(mapped.Args[1]);
            Assert.Equal("IFCCARTESIANTRANSFORMATIONOPERATOR3D", op.Type);
            var origin = model.Deref(op.Args[2]);
            return new { Name = proxy.Args[2], Origin = origin.Args[0], Scale = op.Args[3] };
        }).ToList();

        Assert.Equal(new[] { "'Z-0001'", "'Z-0002'" }, placed.Select(p => p.Name));
        Assert.Equal("(1200.5,-300.,45.)", placed[0].Origin);
        Assert.Equal("300.", placed[0].Scale);
        Assert.Equal("(2087654.25,0.125,-7.)", placed[1].Origin);
        Assert.Equal("150.", placed[1].Scale);
    }

    [Theory]
    [InlineData(10, 20, 360, 182)]
    [InlineData(5, 10, 80, 42)]
    public void Write_TheSharedSphereIsAClosedUnitUvSphere(int latitudeBands, int longitudeBands, int triangles, int vertices)
    {
        var document = Document(Marker("Z-0001", 0, 0, 0, 150, "new"));
        document.LatitudeBands = latitudeBands;
        document.LongitudeBands = longitudeBands;

        var model = StepModel.Parse(ClashZoneIfcWriter.Write(document));

        var faceSet = Assert.Single(model.OfType("IFCTRIANGULATEDFACESET"));
        Assert.Equal(".T.", faceSet.Args[2]);
        var pointList = model.Deref(faceSet.Args[0]);
        Assert.Single(pointList.Args); // IFC4 IfcCartesianPointList3D has only CoordList; TagList is IFC4.3.
        var points = StepModel.Tuples(pointList.Args[0]);
        var faces = StepModel.Tuples(faceSet.Args[3]);
        Assert.Equal(vertices, points.Count);
        Assert.Equal(triangles, faces.Count);
        Assert.All(points, p => Assert.Equal(1.0, Math.Sqrt(p.Sum(c => c * c)), 6));
        Assert.All(faces, f => Assert.All(f, index => Assert.InRange(index, 1, vertices)));

        // Closed: every undirected edge is shared by exactly two triangles.
        var edges = faces
            .SelectMany(f => new[] { (f[0], f[1]), (f[1], f[2]), (f[2], f[0]) })
            .Select(e => e.Item1 < e.Item2 ? (e.Item1, e.Item2) : (e.Item2, e.Item1))
            .GroupBy(e => e)
            .Select(g => g.Count())
            .ToList();
        Assert.All(edges, count => Assert.Equal(2, count));
    }

    [Fact]
    public void Write_StylesOnlyTheStatesInUseWithColourAndTransparency()
    {
        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document(
            Marker("Z-0001", 0, 0, 0, 150, "new"),
            Marker("Z-0002", 900, 0, 0, 150, "new"),
            Marker("Z-0003", 1800, 0, 0, 150, "disappeared"))));

        var styles = model.OfType("IFCSURFACESTYLE");
        Assert.Equal(new[] { "'new'", "'disappeared'" }.OrderBy(s => s), styles.Select(s => s.Args[0]).OrderBy(s => s));
        var shading = model.OfType("IFCSURFACESTYLESHADING");
        Assert.Equal(2, shading.Count);
        var grey = shading.Single(s => model.Deref(s.Args[0]).Arguments == "$,0.5,0.5,0.5");
        Assert.Equal("0.8", grey.Args[1]);
        var red = shading.Single(s => model.Deref(s.Args[0]).Arguments == "$,0.9,0.1,0.1");
        Assert.Equal("0.6", red.Args[1]);

        foreach (var styled in model.OfType("IFCSTYLEDITEM"))
            Assert.Equal("IFCMAPPEDITEM", model.Deref(styled.Args[0]).Type);
    }

    [Fact]
    public void Write_AttachesTypedPropertiesInTheNamedPropertySet()
    {
        var marker = Marker("Z-0001", 0, 0, 0, 150, "new");
        marker.Properties = new List<ClashZoneMarkerProperty>
        {
            new ClashZoneMarkerProperty { Name = "ZoneId", Value = "Z-0001" },
            new ClashZoneMarkerProperty { Name = "ClashCount", Value = 3 },
            new ClashZoneMarkerProperty { Name = "TrackCount", Value = 12L },
            new ClashZoneMarkerProperty { Name = "RadiusMm", Value = 150.0 },
            new ClashZoneMarkerProperty { Name = "Weak", Value = true },
            new ClashZoneMarkerProperty { Name = "Skipped", Value = null },
            new ClashZoneMarkerProperty { Name = "SideA", Value = "Труба 'A' \\1" },
            new ClashZoneMarkerProperty { Name = "Long", Value = new string('x', 300) },
        };

        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document(marker)));

        var set = Assert.Single(model.OfType("IFCPROPERTYSET"));
        Assert.Equal("'NH_ClashZone'", set.Args[2]);
        var values = StepModel.Items(set.Args[4]).Select(model.Deref).ToDictionary(p => p.Args[0], p => p.Args[2]);
        Assert.Equal(7, values.Count);
        Assert.Equal("IFCLABEL('Z-0001')", values["'ZoneId'"]);
        Assert.Equal("IFCINTEGER(3)", values["'ClashCount'"]);
        Assert.Equal("IFCINTEGER(12)", values["'TrackCount'"]);
        Assert.Equal("IFCREAL(150.)", values["'RadiusMm'"]);
        Assert.Equal("IFCBOOLEAN(.T.)", values["'Weak'"]);
        Assert.Equal("IFCLABEL('\\X2\\04220440044304310430\\X0\\ ''A'' \\\\1')", values["'SideA'"]);
        Assert.Equal("IFCLABEL('" + new string('x', 255) + "')", values["'Long'"]);

        var relation = Assert.Single(model.OfType("IFCRELDEFINESBYPROPERTIES"));
        Assert.Equal("(" + model.OfType("IFCBUILDINGELEMENTPROXY")[0].Ref + ")", relation.Args[4]);
        Assert.Equal(set.Ref, relation.Args[5]);
    }

    [Fact]
    public void Write_GlobalIdsAreValidUniqueAndTheOutputIsDeterministicInAnyCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        string first;
        string second;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            first = ClashZoneIfcWriter.Write(Document(Marker("Z-0001", 0.5, 1.25, -2, 150, "new"), Marker("Z-0002", 9, 9, 9, 160, "new")));
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            second = ClashZoneIfcWriter.Write(Document(Marker("Z-0001", 0.5, 1.25, -2, 150, "new"), Marker("Z-0002", 9, 9, 9, 160, "new")));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }

        Assert.Equal(first, second);
        var model = StepModel.Parse(first);
        var rooted = new[] { "IFCPROJECT", "IFCSITE", "IFCBUILDINGELEMENTPROXY", "IFCPROPERTYSET", "IFCRELDEFINESBYPROPERTIES", "IFCRELAGGREGATES", "IFCRELCONTAINEDINSPATIALSTRUCTURE" };
        var ids = rooted.SelectMany(model.OfType).Select(e => e.Args[0]).ToList();
        Assert.All(ids, id => Assert.Matches("^'[0-3][0-9A-Za-z_$]{21}'$", id));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var otherRun = Document(Marker("Z-0001", 0.5, 1.25, -2, 150, "new"), Marker("Z-0002", 9, 9, 9, 160, "new"));
        otherRun.RunId = "run-2";
        var otherIds = rooted.SelectMany(StepModel.Parse(ClashZoneIfcWriter.Write(otherRun)).OfType).Select(e => e.Args[0]);
        Assert.Empty(ids.Intersect(otherIds));
    }

    [Fact]
    public void Write_NoMarkersStillGivesAValidFileWithoutContainment()
    {
        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document()));

        Assert.Single(model.OfType("IFCSITE"));
        Assert.Empty(model.OfType("IFCBUILDINGELEMENTPROXY"));
        Assert.Empty(model.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"));
    }

    [Fact]
    public void Write_MarkerWithoutPropertyValuesGetsNoEmptyPropertySet()
    {
        var bare = Marker("Z-0001", 0, 0, 0, 150, "new");
        bare.Properties = new List<ClashZoneMarkerProperty> { new ClashZoneMarkerProperty { Name = "Skipped", Value = null } };

        var model = StepModel.Parse(ClashZoneIfcWriter.Write(Document(bare, Marker("Z-0002", 900, 0, 0, 150, "new"))));

        Assert.Equal(2, model.OfType("IFCBUILDINGELEMENTPROXY").Count);
        var set = Assert.Single(model.OfType("IFCPROPERTYSET"));
        Assert.Single(StepModel.Items(set.Args[4]));
        Assert.Single(model.OfType("IFCRELDEFINESBYPROPERTIES"));
    }

    [Fact]
    public void Write_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentNullException>(() => ClashZoneIfcWriter.Write(null));
        Assert.Throws<ArgumentException>(() => ClashZoneIfcWriter.Write(Document(Marker("Z-1", 0, 0, 0, 0, "new"))));
        Assert.Throws<ArgumentException>(() => ClashZoneIfcWriter.Write(Document(Marker("Z-1", double.NaN, 0, 0, 150, "new"))));
        Assert.Throws<ArgumentException>(() => ClashZoneIfcWriter.Write(Document(Marker("Z-1", 0, 0, 0, 150, "unknown"))));

        var bands = Document(Marker("Z-1", 0, 0, 0, 150, "new"));
        bands.LatitudeBands = 2;
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneIfcWriter.Write(bands));

        var colour = Document(Marker("Z-1", 0, 0, 0, 150, "new"));
        colour.Styles[0].Transparency = 1.5;
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneIfcWriter.Write(colour));
    }

    private static ClashZoneIfcDocument Document(params ClashZoneMarker[] markers)
    {
        return new ClashZoneIfcDocument
        {
            ProjectName = "Clash zones",
            SiteName = "Clash zones run",
            RunId = "run-1",
            TimeStamp = "2026-10-15T12:00:00",
            Styles = new List<ClashZoneMarkerStyle>
            {
                new ClashZoneMarkerStyle { Key = "new", Red = 0.9, Green = 0.1, Blue = 0.1, Transparency = 0.6 },
                new ClashZoneMarkerStyle { Key = "persisting", Red = 1.0, Green = 0.55, Blue = 0.0, Transparency = 0.6 },
                new ClashZoneMarkerStyle { Key = "disappeared", Red = 0.5, Green = 0.5, Blue = 0.5, Transparency = 0.8 },
            },
            Markers = markers.ToList(),
        };
    }

    private static ClashZoneMarker Marker(string name, double x, double y, double z, double radius, string style)
    {
        return new ClashZoneMarker
        {
            Name = name,
            X = x,
            Y = y,
            Z = z,
            RadiusMm = radius,
            StyleKey = style,
            Properties = new List<ClashZoneMarkerProperty> { new ClashZoneMarkerProperty { Name = "ZoneId", Value = name } },
        };
    }

    // A minimal ISO 10303-21 reader: enough to follow references and split top-level arguments.
    private sealed class StepModel
    {
        public Dictionary<int, StepEntity> Entities { get; } = new Dictionary<int, StepEntity>();

        public static StepModel Parse(string text)
        {
            var data = text.Substring(text.IndexOf("DATA;", StringComparison.Ordinal) + 5);
            data = data.Substring(0, data.IndexOf("ENDSEC;", StringComparison.Ordinal));
            var model = new StepModel();
            foreach (var statement in SplitStatements(data))
            {
                var match = Regex.Match(statement, @"^#(\d+)=([A-Z0-9]+)\((.*)\)$", RegexOptions.Singleline);
                Assert.True(match.Success, "unparsed statement: " + statement);
                var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.False(model.Entities.ContainsKey(id), "duplicate #" + id);
                model.Entities[id] = new StepEntity(id, match.Groups[2].Value, match.Groups[3].Value);
            }

            return model;
        }

        public List<StepEntity> OfType(string type)
        {
            return Entities.Values.Where(e => e.Type == type).OrderBy(e => e.Id).ToList();
        }

        public StepEntity Deref(string reference)
        {
            Assert.Matches(@"^#\d+$", reference);
            return Entities[int.Parse(reference.Substring(1), CultureInfo.InvariantCulture)];
        }

        public static List<string> Items(string aggregate)
        {
            Assert.StartsWith("(", aggregate);
            return SplitTopLevel(aggregate.Substring(1, aggregate.Length - 2));
        }

        public static List<double[]> Tuples(string aggregate)
        {
            return Items(aggregate)
                .Select(t => Items(t).Select(v => double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray())
                .ToList();
        }

        private static IEnumerable<string> SplitStatements(string data)
        {
            var current = new StringBuilder();
            var inString = false;
            foreach (var ch in data)
            {
                if (ch == '\'')
                    inString = !inString;
                if (!inString && ch == ';')
                {
                    var statement = current.ToString().Trim();
                    if (statement.Length > 0)
                        yield return statement;
                    current.Clear();
                    continue;
                }

                if (inString || !char.IsWhiteSpace(ch))
                    current.Append(ch);
            }
        }

        public static List<string> SplitTopLevel(string arguments)
        {
            var parts = new List<string>();
            var depth = 0;
            var inString = false;
            var current = new StringBuilder();
            foreach (var ch in arguments)
            {
                if (ch == '\'')
                    inString = !inString;
                else if (!inString && ch == '(')
                    depth++;
                else if (!inString && ch == ')')
                    depth--;

                if (!inString && depth == 0 && ch == ',')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0 || parts.Count > 0)
                parts.Add(current.ToString());
            return parts;
        }
    }

    private sealed class StepEntity
    {
        public StepEntity(int id, string type, string arguments)
        {
            Id = id;
            Type = type;
            Arguments = arguments;
            Args = StepModel.SplitTopLevel(arguments);
        }

        public int Id { get; }

        public string Type { get; }

        public string Arguments { get; }

        public List<string> Args { get; }

        public string Ref => "#" + Id.ToString(CultureInfo.InvariantCulture);
    }
}
