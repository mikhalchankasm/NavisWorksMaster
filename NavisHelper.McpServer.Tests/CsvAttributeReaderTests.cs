using System.Text;
using NavisHelper.Core.Import;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CsvAttributeReaderTests
{
    public static IEnumerable<object[]> UnicodeEncodings()
    {
        yield return new object[] { new UTF8Encoding(false, true) };
        yield return new object[] { new UTF8Encoding(true, true) };
        yield return new object[] { new UnicodeEncoding(false, true, true) };
        yield return new object[] { new UnicodeEncoding(true, true, true) };
        yield return new object[] { new UTF32Encoding(false, true, true) };
        yield return new object[] { new UTF32Encoding(true, true, true) };
    }

    [Theory]
    [MemberData(nameof(UnicodeEncodings))]
    public void SupportedUnicodePreservesCyrillicAndSupplementaryCharacters(Encoding encoding)
    {
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Имя;Система\r\nНасос-01;Вентиляция 🔧\r\n")).ToArray();
        var table = CsvAttributeReader.Parse(bytes);
        Assert.Equal(new[] { "Имя", "Система" }, table.Headers);
        Assert.Equal(new[] { "Насос-01", "Вентиляция 🔧" }, Assert.Single(table.Rows));
    }

    [Fact]
    public void QuotedFieldsPreserveDelimitersEscapedQuotesAndLineBreaks()
    {
        var table = Parse(" Name ; \"Detail; note\" ;Empty\r\n\"Pump; 01\";\"line 1\r\nline \"\"2\"\"\";\r\n\r\n");
        Assert.Equal(new[] { "Name", "Detail; note", "Empty" }, table.Headers);
        Assert.Equal(new[] { "Pump; 01", "line 1\r\nline \"2\"", "" }, Assert.Single(table.Rows));
    }

    [Fact]
    public void TrimsFieldsAndIgnoresBlankLines()
    {
        var table = Parse("\r\n Name ; Value \r\n\r\n Pump ; \" value \" \r\n\r\n");
        Assert.Equal(new[] { "Pump", "value" }, Assert.Single(table.Rows));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    [InlineData("Name;Value\r\n\r\n")]
    public void RequiresHeaderAndData(string input)
    {
        AssertError(input, "CsvAttributeEmpty");
    }

    [Theory]
    [InlineData("Name\nPump", "CsvAttributeColumnsRequired", 0)]
    [InlineData("Name;\nPump;x", "CsvAttributeHeaderInvalid", 2)]
    [InlineData(";Value\nPump;x", "CsvAttributeHeaderInvalid", 1)]
    [InlineData("Name;Value;value\nPump;x;y", "CsvAttributeHeaderInvalid", 3)]
    [InlineData("Name;Value\n;x", "CsvAttributeItemNameRequired", 2)]
    [InlineData("Name;Value\n\" \";x", "CsvAttributeItemNameRequired", 2)]
    public void RejectsAmbiguousHeadersAndMissingItemNames(string input, string key, int position)
    {
        var error = AssertError(input, key);
        if (position != 0) Assert.Equal(position, error.Arguments[0]);
    }

    [Theory]
    [InlineData("Pump", 1)]
    [InlineData("Pump;x;y", 3)]
    public void InvalidTailRejectsTheEntireInputAfterAValidPrefix(string tail, int actualWidth)
    {
        var error = AssertError("Name;Value\nGood;x\n" + tail, "CsvAttributeRowWidth");
        Assert.Equal(new object[] { 3, actualWidth, 2 }, error.Arguments);
    }

    [Theory]
    [InlineData("Name;Value\nPump;\"unterminated", 2L)]
    [InlineData("Name;Value\nPump;\"closed\"invalid", 2L)]
    [InlineData("Name;Value\nGood;x\nPump;\"unterminated", 3L)]
    public void MalformedQuotesReportPhysicalLine(string input, long line)
    {
        var error = AssertError(input, "CsvAttributeMalformedLine");
        Assert.Equal(line, error.Arguments[0]);
    }

    public static IEnumerable<object[]> InvalidEncodings()
    {
        yield return new object[] { new byte[] { 0xEF, 0xBB, 0xBF, 0xC0, 0xAF } };
        yield return new object[] { new byte[] { 0xFF, 0xFE, 0x00, 0xD8 } }; // Unpaired UTF-16 surrogate.
        yield return new object[] { new byte[] { 0xFE, 0xFF, 0x00 } }; // Truncated UTF-16 code unit.
        yield return new object[] { new byte[] { 0xFF, 0xFE, 0, 0, 0, 0, 0x11, 0 } }; // Above U+10FFFF.
        yield return new object[] { new byte[] { 0, 0, 0xFE, 0xFF, 0 } }; // Truncated UTF-32 code point.
        yield return new object[] { Encoding.Unicode.GetBytes("Name;Value\nPump;x") }; // No BOM.
        yield return new object[] { Encoding.UTF8.GetBytes("Name;Value\nPump;\0x") };
        yield return new object[] { Encoding.ASCII.GetBytes("Name;Value\nGood;x\n").Concat(new byte[] { 0xCF, 0xF0, 0x3B, 0x78 }).ToArray() }; // Legacy Cyrillic, not UTF-8.
    }

    [Theory]
    [MemberData(nameof(InvalidEncodings))]
    public void InvalidOrUnsupportedBytesAreNeverReplacedSilently(byte[] bytes)
    {
        var error = Assert.Throws<CsvAttributeReadException>(() => CsvAttributeReader.Parse(bytes));
        Assert.Equal("CsvAttributeEncodingInvalid", error.ResourceKey);
    }

    [Fact]
    public void ReadsRealUtf8BomFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Name;Value\nНасос;Система", new UTF8Encoding(true));
            Assert.Equal(new[] { "Насос", "Система" }, Assert.Single(CsvAttributeReader.Read(path).Rows));
        }
        finally { File.Delete(path); }
    }

    private static CsvAttributeTable Parse(string input) => CsvAttributeReader.Parse(Encoding.UTF8.GetBytes(input));

    private static CsvAttributeReadException AssertError(string input, string key)
    {
        var error = Assert.Throws<CsvAttributeReadException>(() => Parse(input));
        Assert.Equal(key, error.ResourceKey);
        return error;
    }
}
