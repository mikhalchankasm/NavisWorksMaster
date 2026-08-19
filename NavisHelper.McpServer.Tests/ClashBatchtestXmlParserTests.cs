using System.Text;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashBatchtestXmlParserTests
{
    [Fact]
    public void Parse_SelectionSetPathsPreserveNestedUnicodeSpacesAndXmlCharacters()
    {
        var plan = Parse(Wrap(Test("Тест A &amp; B", "Папка/Sub Folder/Набор &amp; трубы", "English/Set 2")));

        var test = Assert.Single(plan.Tests);
        Assert.Equal("Тест A & B", test.Name);
        Assert.Equal("Папка/Sub Folder/Набор & трубы", test.A.Path);
        Assert.Equal("English/Set 2", test.B.Path);
        Assert.True(test.Supported);
    }

    [Fact]
    public void Parse_DefaultNamespaceUsesLocalNamesAndDoesNotLoadExternalXsd()
    {
        var xml = "<exchange xmlns=\"urn:synthetic-navisworks\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:synthetic-navisworks https://invalid.example.test/nw-exchange-12.0.xsd\">" +
                  "<batchtest units=\"mm\"><clashtests>" + Test("Namespaced", "Folder/A", "Folder/B") + "</clashtests></batchtest></exchange>";

        var test = Assert.Single(Parse(xml).Tests);
        Assert.Equal("Folder/A", test.A.Path);
    }

    [Fact]
    public void Parse_MalformedXmlIsRejected()
    {
        var exception = Assert.Throws<ClashTransferParseException>(() => Parse("<exchange><batchtest>"));
        Assert.Equal(ClashTransferParseErrorCodes.MalformedXml, exception.Code);
    }

    [Fact]
    public void Parse_DtdAndExternalEntityAreRejected()
    {
        var xml = "<!DOCTYPE exchange [<!ENTITY xxe SYSTEM \"file:///C:/synthetic-secret.txt\">]><exchange><batchtest units=\"mm\"><clashtests>&xxe;</clashtests></batchtest></exchange>";
        var exception = Assert.Throws<ClashTransferParseException>(() => Parse(xml));
        Assert.Equal(ClashTransferParseErrorCodes.UnsafeXml, exception.Code);
    }

    [Fact]
    public void Parse_OversizedInputIsRejectedBeforeParsing()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 2048)));
        var exception = Assert.Throws<ClashTransferParseException>(() => ClashBatchtestXmlParser.Parse(stream, new ClashBatchtestParseOptions { MaximumCharactersInDocument = 1024 }));
        Assert.Equal(ClashTransferParseErrorCodes.InputTooLarge, exception.Code);
    }

    [Fact]
    public void Parse_TestLimitIsEnforced()
    {
        var xml = Wrap(Test("One", "A", "B") + Test("Two", "C", "D"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var exception = Assert.Throws<ClashTransferParseException>(() => ClashBatchtestXmlParser.Parse(stream, new ClashBatchtestParseOptions { MaximumTestCount = 1 }));
        Assert.Equal(ClashTransferParseErrorCodes.TestLimitExceeded, exception.Code);
    }

    [Fact]
    public void Parse_UnknownLocatorPrefixReportsExactSideAndLocator()
    {
        var xml = Wrap("<clashtest name=\"Unknown\" test_type=\"hard\" tolerance=\"1\"><linkage/><left><clashselection><locator>lcop_unknown/A</locator></clashselection></left><right><clashselection><locator>lcop_selection_set_tree/B</locator></clashselection></right></clashtest>");
        var test = Assert.Single(Parse(xml).Tests);

        Assert.False(test.Supported);
        Assert.Contains(test.A.Warnings, warning => warning.Contains("side A") && warning.Contains("lcop_unknown/A"));
    }

    [Fact]
    public void Parse_MissingSideIsUnsupported()
    {
        var xml = Wrap("<clashtest name=\"Missing\" test_type=\"hard\"><linkage/><left><clashselection><locator>lcop_selection_set_tree/A</locator></clashselection></left></clashtest>");
        var test = Assert.Single(Parse(xml).Tests);
        Assert.False(test.Supported);
        Assert.Contains(test.B.Warnings, warning => warning.Contains("side B"));
    }

    [Fact]
    public void Parse_DuplicateNamesAreRejectedPerDefinition()
    {
        var plan = Parse(Wrap(Test("Duplicate", "A", "B") + Test("Duplicate", "C", "D")));
        Assert.True(plan.Tests[0].Supported);
        Assert.False(plan.Tests[1].Supported);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Duplicate test name"));
    }

    [Fact]
    public void Parse_UnsupportedTypeAndSettingsAreReported()
    {
        var xml = Wrap("<clashtest name=\"Custom\" test_type=\"custom\" tolerance=\"1\" priority=\"5\"><linkage mode=\"animation\"/><left><clashselection><locator>lcop_selection_set_tree/A</locator></clashselection></left><right><clashselection><locator>lcop_selection_set_tree/B</locator></clashselection></right><rules/></clashtest>");
        var test = Assert.Single(Parse(xml).Tests);
        Assert.False(test.Supported);
        Assert.Contains("priority", test.UnsupportedSettings);
        Assert.Contains("linkage", test.UnsupportedSettings);
        Assert.Contains("rules", test.UnsupportedSettings);
    }

    [Fact]
    public void Parse_ToleranceUsesBatchtestUnits()
    {
        var test = Assert.Single(Parse(Wrap(Test("Feet", "A", "B", tolerance: "0.5"), units: "ft")).Tests);
        Assert.Equal(152.4, test.ToleranceMm.Value, 6);
        Assert.Equal("clearance", test.TestType);
        Assert.True(test.A.SelfIntersect);
    }

    [Fact]
    public void Parse_OmittedToleranceRetainsTargetDefaultAndWarns()
    {
        var xml = Wrap("<clashtest name=\"Default tolerance\" test_type=\"hard\"><linkage/>" +
                       "<left><clashselection><locator>lcop_selection_set_tree/A</locator></clashselection></left>" +
                       "<right><clashselection><locator>lcop_selection_set_tree/B</locator></clashselection></right></clashtest>");
        var test = Assert.Single(Parse(xml).Tests);
        Assert.Null(test.ToleranceMm);
        Assert.Contains(test.Warnings, warning => warning.Contains("target Navisworks default"));
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersionIsRejectedWithoutLoadingIt()
    {
        var xml = "<exchange xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"https://invalid.example.test/nw-exchange-11.0.xsd\"><batchtest units=\"mm\"><clashtests/></batchtest></exchange>";
        var exception = Assert.Throws<ClashTransferParseException>(() => Parse(xml));
        Assert.Equal(ClashTransferParseErrorCodes.UnsupportedSchema, exception.Code);
    }

    private static ClashTestTransferPlan Parse(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return ClashBatchtestXmlParser.Parse(stream, new ClashBatchtestParseOptions());
    }

    private static string Wrap(string tests, string units = "mm") =>
        "<exchange><batchtest units=\"" + units + "\"><clashtests>" + tests + "</clashtests></batchtest></exchange>";

    private static string Test(string name, string a, string b, string tolerance = "2.5") =>
        "<clashtest name=\"" + name + "\" test_type=\"clearance\" tolerance=\"" + tolerance + "\"><linkage/>" +
        "<left><clashselection selfintersect=\"1\"><locator>lcop_selection_set_tree/" + a + "</locator></clashselection></left>" +
        "<right><clashselection selfintersect=\"0\"><locator>lcop_selection_set_tree/" + b + "</locator></clashselection></right></clashtest>";
}
