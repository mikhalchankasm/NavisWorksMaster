using NavisHelper.Agent.Contracts;
using System.Globalization;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SubtreeDumpOutputFormatterTests
{
    [Theory]
    [InlineData(null, "csv")]
    [InlineData("", "csv")]
    [InlineData("   ", "csv")]
    [InlineData(" CSV ", "csv")]
    [InlineData("jsonl", "jsonl")]
    [InlineData(" JSONL ", "jsonl")]
    public void NormalizeFormat_PreservesAliasesAndDefault(string value, string expected)
    {
        Assert.Equal(expected, SubtreeDumpOutputFormatter.NormalizeFormat(value));
    }

    [Fact]
    public void NormalizeFormat_InvalidValue_PreservesMessage()
    {
        var error = Assert.Throws<ArgumentException>(() => SubtreeDumpOutputFormatter.NormalizeFormat("json"));

        Assert.StartsWith(SubtreeDumpOutputFormatter.InvalidFormatMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeFormat_InteriorWhitespaceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => SubtreeDumpOutputFormatter.NormalizeFormat("c sv"));
    }

    [Theory]
    [InlineData(false, false, "Name;DisplayName;Depth;IsHidden")]
    [InlineData(true, false, "Name;DisplayName;Depth;IsHidden;Path")]
    [InlineData(false, true, "Name;DisplayName;Depth;IsHidden;SourceFile")]
    [InlineData(true, true, "Name;DisplayName;Depth;IsHidden;Path;SourceFile")]
    public void BuildCsvHeader_PreservesOptionalColumnOrder(bool includePath, bool includeSourceFile, string expected)
    {
        Assert.Equal(expected, SubtreeDumpOutputFormatter.BuildCsvHeader(includePath, includeSourceFile));
    }

    [Theory]
    [InlineData(false, false, "\"A;B\";\"Display \"\"quoted\"\"\";7;true")]
    [InlineData(true, false, "\"A;B\";\"Display \"\"quoted\"\"\";7;true;\"Root / A\"\"B\"")]
    [InlineData(false, true, "\"A;B\";\"Display \"\"quoted\"\"\";7;true;\"model.rvm\"")]
    [InlineData(true, true, "\"A;B\";\"Display \"\"quoted\"\"\";7;true;\"Root / A\"\"B\";\"model.rvm\"")]
    public void BuildCsvRow_PreservesQuotingAndOptionalColumnOrder(bool includePath, bool includeSourceFile, string expected)
    {
        var row = CreateRow();

        Assert.Equal(expected, SubtreeDumpOutputFormatter.BuildCsvRow(row, includePath, includeSourceFile));
    }

    [Fact]
    public void BuildCsvRow_NullStringsBecomeQuotedEmptyValues()
    {
        var row = new SubtreeDumpOutputRow { Depth = -2, IsHidden = false };

        Assert.Equal("\"\";\"\";-2;false;\"\";\"\"", SubtreeDumpOutputFormatter.BuildCsvRow(row, true, true));
    }

    [Fact]
    public void BuildCsvRow_EmbeddedNewlineRemainsInsideQuotedField()
    {
        var row = new SubtreeDumpOutputRow { Name = "Line 1\nLine 2", DisplayName = "D" };

        Assert.Equal("\"Line 1\nLine 2\";\"D\";0;false", SubtreeDumpOutputFormatter.BuildCsvRow(row, false, false));
    }

    [Fact]
    public void BuildCsvRow_DepthUsesInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var row = new SubtreeDumpOutputRow { Depth = -1234 };

            Assert.Equal("\"\";\"\";-1234;false", SubtreeDumpOutputFormatter.BuildCsvRow(row, false, false));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(false, false, new[] { "name", "displayName", "depth", "isHidden" })]
    [InlineData(true, false, new[] { "name", "displayName", "depth", "isHidden", "path" })]
    [InlineData(false, true, new[] { "name", "displayName", "depth", "isHidden", "sourceFile" })]
    [InlineData(true, true, new[] { "name", "displayName", "depth", "isHidden", "path", "sourceFile" })]
    public void BuildJsonRow_PreservesKeyOrderAndOptionalFields(bool includePath, bool includeSourceFile, string[] expectedKeys)
    {
        var result = SubtreeDumpOutputFormatter.BuildJsonRow(CreateRow(), includePath, includeSourceFile);

        Assert.Equal(expectedKeys, result.Keys);
        Assert.Equal("A;B", result["name"]);
        Assert.Equal("Display \"quoted\"", result["displayName"]);
        Assert.Equal(7, result["depth"]);
        Assert.True((bool)result["isHidden"]);
        if (includePath)
            Assert.Equal("Root / A\"B", result["path"]);
        if (includeSourceFile)
            Assert.Equal("model.rvm", result["sourceFile"]);
    }

    [Fact]
    public void BuildJsonRow_NullStringsBecomeEmptyValues()
    {
        var result = SubtreeDumpOutputFormatter.BuildJsonRow(new SubtreeDumpOutputRow(), true, true);

        Assert.Equal(string.Empty, result["name"]);
        Assert.Equal(string.Empty, result["displayName"]);
        Assert.Equal(string.Empty, result["path"]);
        Assert.Equal(string.Empty, result["sourceFile"]);
    }

    [Fact]
    public void Builders_RejectNullRow()
    {
        Assert.Throws<ArgumentNullException>(() => SubtreeDumpOutputFormatter.BuildCsvRow(null, false, false));
        Assert.Throws<ArgumentNullException>(() => SubtreeDumpOutputFormatter.BuildJsonRow(null, false, false));
    }

    private static SubtreeDumpOutputRow CreateRow()
    {
        return new SubtreeDumpOutputRow
        {
            Name = "A;B",
            DisplayName = "Display \"quoted\"",
            Depth = 7,
            IsHidden = true,
            Path = "Root / A\"B",
            SourceFile = "model.rvm",
        };
    }
}
