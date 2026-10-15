using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for the shared multi-sheet XLSX writer (docs/CLASH_ZONES_PLAN.md, section 6, PR 4).
public sealed class XlsxWorkbookWriterTests
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void Write_ProducesAWorkbookWithEverySheetInOrder()
    {
        var parts = Write(Sheet("Run"), Sheet("Зоны"), Sheet("Clashes"));

        Assert.Contains("[Content_Types].xml", parts.Keys);
        Assert.Contains("_rels/.rels", parts.Keys);
        Assert.Contains("xl/styles.xml", parts.Keys);
        var workbook = parts["xl/workbook.xml"];
        var sheets = workbook.Descendants(Main + "sheet").ToList();
        Assert.Equal(new[] { "Run", "Зоны", "Clashes" }, sheets.Select(s => (string)s.Attribute("name")));
        Assert.Equal(new[] { "1", "2", "3" }, sheets.Select(s => (string)s.Attribute("sheetId")));

        var relationships = parts["xl/_rels/workbook.xml.rels"].Descendants(PackageRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target"));
        var overrides = parts["[Content_Types].xml"].Descendants(ContentTypes + "Override")
            .Select(o => (string)o.Attribute("PartName")).ToList();
        foreach (var sheet in sheets)
        {
            var target = relationships[(string)sheet.Attribute(Rel + "id")];
            var partName = "xl/" + target.TrimStart('/').Replace("xl/", string.Empty);
            Assert.Contains(partName, parts.Keys);
            Assert.Contains("/" + partName, overrides);
        }

        Assert.Contains(parts["_rels/.rels"].Descendants(PackageRel + "Relationship"), r => (string)r.Attribute("Target") == "xl/workbook.xml");
    }

    [Fact]
    public void Write_NumbersAreNumericCellsInInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var parts = Write(Sheet("Zones", new object[] { 1234.5, 42, 7L, -0.1 + 0.3, 1.5f, 12.25m }));

            var cells = Row(parts, 1, 2);
            Assert.Equal(new[] { "A2", "B2", "C2", "D2", "E2", "F2" }, cells.Select(c => (string)c.Attribute("r")));
            Assert.All(cells, c => Assert.True(c.Attribute("t") == null || (string)c.Attribute("t") == "n", (string)c.Attribute("r") + " is not numeric"));
            var values = cells.Select(c => (string)c.Element(Main + "v")).ToList();
            Assert.Equal("1234.5", values[0]);
            Assert.Equal("42", values[1]);
            Assert.Equal("7", values[2]);
            Assert.Equal(-0.1 + 0.3, double.Parse(values[3], CultureInfo.InvariantCulture));
            Assert.Equal("1.5", values[4]);
            Assert.Equal("12.25", values[5]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Write_TextIsInlineEscapedAndCleanedOfInvalidXmlCharacters()
    {
        var parts = Write(Sheet("Clashes", new object[] { "  <A & \"B\">  ", "Труба\u0001DN100\tx", string.Empty }));

        var cells = Row(parts, 1, 2);
        Assert.Equal("inlineStr", (string)cells[0].Attribute("t"));
        Assert.Equal("  <A & \"B\">  ", cells[0].Element(Main + "is").Element(Main + "t").Value);
        Assert.Equal("preserve", (string)cells[0].Element(Main + "is").Element(Main + "t").Attribute(XNamespace.Xml + "space"));
        Assert.Equal("ТрубаDN100\tx", cells[1].Element(Main + "is").Element(Main + "t").Value);
    }

    [Fact]
    public void Write_TextLongerThanExcelsCellLimitIsCut()
    {
        var parts = Write(Sheet("Clashes", new object[] { new string('x', 40000) }));

        Assert.Equal(32767, Row(parts, 1, 2)[0].Element(Main + "is").Element(Main + "t").Value.Length);
    }

    [Fact]
    public void Write_LiteralSpreadsheetEscapesAreEscaped()
    {
        var parts = Write(Sheet("Clashes", new object[] { "part_x000D_name _X12AB_ _x00g_" }));

        Assert.Equal("part_x005F_x000D_name _X12AB_ _x00g_", Row(parts, 1, 2)[0].Element(Main + "is").Element(Main + "t").Value);
    }

    [Fact]
    public void Write_RejectsSheetsBeyondExcelsGrid()
    {
        var tooWide = new XlsxSheet { Name = "Wide", Columns = new List<string>(), Rows = new List<IList<object>> { new object[16385] } };

        Assert.Throws<ArgumentException>(() => Bytes(tooWide));
    }

    [Fact]
    public void Write_BooleansNullsAndWideRowsKeepTheirColumns()
    {
        var wide = new object[30];
        wide[0] = true;
        wide[1] = false;
        wide[3] = "D";
        wide[27] = 28;

        var parts = Write(Sheet("Run", wide));

        var cells = Row(parts, 1, 2);
        Assert.Equal(new[] { "A2", "B2", "D2", "AB2" }, cells.Select(c => (string)c.Attribute("r")));
        Assert.Equal("b", (string)cells[0].Attribute("t"));
        Assert.Equal("1", (string)cells[0].Element(Main + "v"));
        Assert.Equal("0", (string)cells[1].Element(Main + "v"));
        Assert.Equal("28", (string)cells[3].Element(Main + "v"));
    }

    [Fact]
    public void Write_NonFiniteNumbersBecomeEmptyCells()
    {
        var parts = Write(Sheet("Zones", new object[] { double.NaN, double.PositiveInfinity, 3.0 }));

        Assert.Equal(new[] { "C2" }, Row(parts, 1, 2).Select(c => (string)c.Attribute("r")));
    }

    [Fact]
    public void Write_HeaderRowIsBoldAndFrozen()
    {
        var parts = Write(Sheet("Zones", new object[] { "Z-0001", 3 }));

        var header = Row(parts, 1, 1);
        Assert.Equal(new[] { "zoneId", "count" }, header.Select(c => c.Element(Main + "is").Element(Main + "t").Value));
        var styleIndex = int.Parse((string)header[0].Attribute("s"), CultureInfo.InvariantCulture);
        Assert.True(styleIndex > 0, "the header uses the default style");
        Assert.All(header, c => Assert.Equal((string)header[0].Attribute("s"), (string)c.Attribute("s")));

        var styles = parts["xl/styles.xml"].Root;
        var xf = styles.Element(Main + "cellXfs").Elements(Main + "xf").ElementAt(styleIndex);
        var fontId = int.Parse((string)xf.Attribute("fontId"), CultureInfo.InvariantCulture);
        var font = styles.Element(Main + "fonts").Elements(Main + "font").ElementAt(fontId);
        Assert.NotNull(font.Element(Main + "b"));

        var pane = Worksheet(parts, 1).Descendants(Main + "pane").Single();
        Assert.Equal("1", (string)pane.Attribute("ySplit"));
        Assert.Equal("frozen", (string)pane.Attribute("state"));
        Assert.Equal("A2", (string)pane.Attribute("topLeftCell"));
    }

    [Fact]
    public void Write_IsByteIdenticalForIdenticalInput()
    {
        var first = Bytes(Sheet("Run", new object[] { "a", 1.0 }), Sheet("Zones", new object[] { "b", 2 }));
        Thread.Sleep(2100);
        var second = Bytes(Sheet("Run", new object[] { "a", 1.0 }), Sheet("Zones", new object[] { "b", 2 }));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a[1]")]
    [InlineData("what?")]
    [InlineData("1234567890123456789012345678901x")]
    [InlineData("'Zones")]
    [InlineData("Zones'")]
    [InlineData("history")]
    public void Write_RejectsInvalidSheetNames(string name)
    {
        Assert.Throws<ArgumentException>(() => Bytes(Sheet(name)));
    }

    [Fact]
    public void Write_RejectsDuplicateSheetNamesIgnoringCaseAndEmptyOrNullInput()
    {
        Assert.Throws<ArgumentException>(() => Bytes(Sheet("Zones"), Sheet("ZONES")));
        Assert.Throws<ArgumentException>(() => Bytes());
        Assert.Throws<ArgumentNullException>(() => XlsxWorkbookWriter.Write(new MemoryStream(), null));
        Assert.Throws<ArgumentNullException>(() => XlsxWorkbookWriter.Write(null, new[] { Sheet("Run") }));
    }

    [Fact]
    public void Write_RejectsUnsupportedCellTypes()
    {
        Assert.Throws<ArgumentException>(() => Bytes(Sheet("Run", new object[] { new object() })));
    }

    [Fact]
    public void Write_LeavesTheOutputStreamOpen()
    {
        var stream = new MemoryStream();

        XlsxWorkbookWriter.Write(stream, new[] { Sheet("Run") });

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
    }

    private static XlsxSheet Sheet(string name, params object[][] rows)
    {
        return new XlsxSheet
        {
            Name = name,
            Columns = new List<string> { "zoneId", "count" },
            Rows = rows.Select(r => (IList<object>)r).ToList(),
        };
    }

    private static byte[] Bytes(params XlsxSheet[] sheets)
    {
        using (var stream = new MemoryStream())
        {
            XlsxWorkbookWriter.Write(stream, sheets);
            return stream.ToArray();
        }
    }

    private static Dictionary<string, XDocument> Write(params XlsxSheet[] sheets)
    {
        var parts = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        using (var archive = new ZipArchive(new MemoryStream(Bytes(sheets)), ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                using (var reader = entry.Open())
                    parts[entry.FullName] = XDocument.Load(reader);
            }
        }

        return parts;
    }

    private static XDocument Worksheet(Dictionary<string, XDocument> parts, int sheetNumber)
    {
        return parts["xl/worksheets/sheet" + sheetNumber + ".xml"];
    }

    private static List<XElement> Row(Dictionary<string, XDocument> parts, int sheetNumber, int rowNumber)
    {
        var row = Worksheet(parts, sheetNumber).Descendants(Main + "row")
            .Single(r => (string)r.Attribute("r") == rowNumber.ToString(CultureInfo.InvariantCulture));
        return row.Elements(Main + "c").ToList();
    }
}
