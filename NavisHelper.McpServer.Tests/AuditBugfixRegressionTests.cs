using System.Globalization;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AuditBugfixRegressionTests
{
    [Theory]
    [InlineData("1", ColorSchemeType.Grayscale)]
    [InlineData("10", ColorSchemeType.Random)]
    [InlineData("11", ColorSchemeType.Industrial)]
    [InlineData("12", ColorSchemeType.Infrastructure)]
    [InlineData("13", ColorSchemeType.OilGas)]
    [InlineData("14", ColorSchemeType.PastelRainbow)]
    public void ColorSchemes_TryParseScheme_AcceptsAllFourteenNumericSchemes(string input, ColorSchemeType expected)
    {
        Assert.True(ColorSchemes.TryParseScheme(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("   ")]
    public void ColorSchemes_TryParseScheme_RejectsOutOfRangeNumbers(string input)
    {
        Assert.False(ColorSchemes.TryParseScheme(input, out _));
    }

    [Fact]
    public void ColorSchemes_GetSchemesDescription_ListsFullRangeOneToFourteen()
    {
        var description = ColorSchemes.GetSchemesDescription();
        Assert.Contains("(1-14)", description, StringComparison.Ordinal);
        Assert.DoesNotContain("(1-10)", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorSchemes_RandomPalette_IsThreadSafeAndInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var names = Enumerable.Range(0, 64).Select(i => $"Item_{i}").ToList();
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

            Parallel.For(0, 16, _ =>
            {
                var colors = ColorSchemes.GenerateColorsForObjects(ColorSchemeType.Random, names);
                foreach (var rgb in colors.Values)
                {
                    var parts = rgb.Split(',');
                    if (parts.Length != 3 ||
                        !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
                        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ||
                        r < 50 || r > 255 || g < 50 || g > 255 || b < 50 || b > 255)
                    {
                        failures.Add(rgb);
                    }
                }
            });

            Assert.Empty(failures);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SectionBoxHelper_GuardsIndexOfBoundsAndUsesJObjectInDisable()
    {
        var source = ReadRepoFile("NavisHelper/Core/SectionBoxHelper.cs");

        Assert.Contains("if (rotStart >= 0)", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (rotStart >= 0)", StringComparison.Ordinal) <
            source.IndexOf("current.IndexOf(\"]\", rotStart", StringComparison.Ordinal));

        Assert.Contains("if (firstBracket < 0) return;", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (firstBracket < 0) return;", StringComparison.Ordinal) <
            source.IndexOf("json.IndexOf(\"]]\", firstBracket", StringComparison.Ordinal));

        Assert.Contains("JObject.Load(reader)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("json.Replace(\"\\\"Enabled\\\":true\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.ActiveDocument.ActiveView", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginLocaleAndErrorHandlingFixes_ArePreserved()
    {
        var docService = ReadRepoFile("NavisHelper/Agent/Services/DocumentCommandService.cs");
        Assert.Contains("Convert.ToDouble(value, CultureInfo.InvariantCulture)", docService, StringComparison.Ordinal);

        var clashExport = ReadRepoFile("NavisHelper/WPF/NavisHelperPanel.ClashExport.cs");
        Assert.Contains("FormattableString.Invariant($\"    <description>Point: {cx:F6}, {cy:F6}, {cz:F6}", clashExport, StringComparison.Ordinal);

        var clashLifecycle = ReadRepoFile("NavisHelper/WPF/NavisHelperPanel.Clash.Lifecycle.cs");
        Assert.Contains("NumberStyles.Float, CultureInfo.InvariantCulture, out radius", clashLifecycle, StringComparison.Ordinal);
        Assert.Contains("double.IsNaN(radius) || double.IsInfinity(radius)", clashLifecycle, StringComparison.Ordinal);

        var colorsPanel = ReadRepoFile("NavisHelper/WPF/NavisHelperPanel.Colors.cs");
        Assert.Contains("if (parts.Length < 3 ||", colorsPanel, StringComparison.Ordinal);
        Assert.Contains("double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture", colorsPanel, StringComparison.Ordinal);

        var clashOps = ReadRepoFile("NavisHelper/WPF/NavisHelperPanel.Clash.Operations.cs");
        Assert.Contains("ExceptionDispatchInfo.Capture(removeError).Throw();", clashOps, StringComparison.Ordinal);
        Assert.DoesNotContain("throw removeError;", clashOps, StringComparison.Ordinal);

        var saveViewpoints = ReadRepoFile("NavisHelper/SaveViewpiontList.cs");
        Assert.Contains("CollectViewpointNames(groupItem.Children, list);", saveViewpoints, StringComparison.Ordinal);
        Assert.Contains("doc?.ActiveView?.Document ?? doc", saveViewpoints, StringComparison.Ordinal);

        var exportImport = ReadRepoFile("NavisHelper/ExportImportColors.cs");
        Assert.Contains("using (var dlg = new FolderBrowserDialog", exportImport, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(path))", exportImport, StringComparison.Ordinal);
        Assert.Contains("if (doc == null || doc.CurrentSelection == null)", exportImport, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NavisHelper.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, relativePath));
    }
}
