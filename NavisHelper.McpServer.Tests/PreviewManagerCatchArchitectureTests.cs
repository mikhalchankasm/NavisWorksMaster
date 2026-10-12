using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The preview managers remember and restore permanent colors, transparency and
// visibility. A silently swallowed failure there leaves the model recolored with
// nothing in the log. Every catch block must therefore name its exception and
// either log, rethrow, or use the exception (to aggregate per-item failures that
// are logged once after the loop, or in a when-filter that picks expected types). A catch of one specific, expected exception
// type may instead handle it without the variable, as long as its body is not
// empty.
public sealed class PreviewManagerCatchArchitectureTests
{
    private static readonly Regex CatchHeader = new(@"\bcatch\b\s*(?:\(\s*([\w.]+)(?:\s+(\w+))?\s*\))?\s*(?:when\s*\((?<filter>[^)]*)\)\s*)?\{");

    [Theory]
    [InlineData("ClashPreviewManager.cs")]
    [InlineData("SelectionPreviewManager.cs")]
    [InlineData("WPF/NavisHelperPanel.ClashOrbitExport.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.Lifecycle.cs")]
    [InlineData("WPF/NavisHelperPanel.ClashDiagnostics.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.Settings.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.PreviewState.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.GroupingEngine.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.Shell.cs")]
    [InlineData("WPF/NavisHelperPanel.Clash.SelectionGrid.cs")]
    [InlineData("WPF/NavisHelperPanel.Colors.cs")]
    [InlineData("WPF/NavisHelperPanel.Selection.cs")]
    [InlineData("SelectionPlaneTool.cs")]
    [InlineData("TopViewBoundingRect.cs")]
    [InlineData("ExportImportColors.cs")]
    [InlineData("Core/ExceptionObservation.cs")]
    [InlineData("AI/AiWorkerProcessRunner.cs")]
    [InlineData("Agent/Services/SearchService.Scoped.cs")]
    [InlineData("Agent/Services/SearchService.Spatial.cs")]
    [InlineData("Agent/Services/ClashIsolationService.cs")]
    [InlineData("Agent/Services/MatchHandleItemsService.cs")]
    public void EveryCatchNamesAndUsesItsException(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", fileName));
        var violations = new List<string>();

        foreach (Match match in CatchHeader.Matches(source))
        {
            var line = source.Take(match.Index).Count(ch => ch == '\n') + 1;
            var variable = match.Groups[2].Success ? match.Groups[2].Value : null;
            var body = BlockBody(source, match.Index + match.Length - 1);

            var logsOrRethrows = body.Contains("Logger.") || Regex.IsMatch(body, @"\bthrow\b");
            var filter = match.Groups["filter"].Success ? match.Groups["filter"].Value : "";
            var usesException = variable != null && body.Trim().Length > 0 &&
                Regex.IsMatch(body + " " + filter, @"\b" + Regex.Escape(variable) + @"\b");
            var type = match.Groups[1].Success ? match.Groups[1].Value : null;
            var expectedSpecificType = type != null && type != "Exception" && type != "System.Exception";
            if (expectedSpecificType && variable == null && body.Trim().Length > 0)
                continue;
            if (variable == null && !Regex.IsMatch(body, @"\bthrow\b"))
                violations.Add(fileName + ":" + line + " catch does not name its exception");
            else if (!logsOrRethrows && !usesException)
                violations.Add(fileName + ":" + line + " catch neither logs, rethrows nor uses its exception");
        }

        Assert.True(violations.Count == 0, violations.Count + " silent catch blocks:\n" + string.Join("\n", violations));
    }

    // Returns the text between the brace at openIndex and its matching close brace,
    // skipping braces inside string and character literals.
    private static string BlockBody(string source, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == '"' || ch == '\'')
            {
                var verbatim = ch == '"' && index > 0 && source[index - 1] == '@';
                for (index++; index < source.Length; index++)
                {
                    if (!verbatim && source[index] == '\\') { index++; continue; }
                    if (source[index] == ch)
                    {
                        if (verbatim && index + 1 < source.Length && source[index + 1] == '"') { index++; continue; }
                        break;
                    }
                }
                continue;
            }

            if (ch == '{') depth++;
            if (ch == '}' && --depth == 0)
                return source.Substring(openIndex + 1, index - openIndex - 1);
        }

        throw new InvalidOperationException("Unbalanced braces after index " + openIndex);
    }
}
