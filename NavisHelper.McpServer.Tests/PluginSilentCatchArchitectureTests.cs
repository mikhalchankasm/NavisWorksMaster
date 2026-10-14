using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// A catch whose body holds nothing but whitespace or comments swallows a failure.
// Across NavisHelper/ that is allowed only when the catch narrows to the failure it
// expects (a specific exception type, or a when-filter) and a comment says why
// ignoring it is correct. Empty bodies, and comment-only bodies on catch or
// catch (Exception), hide failures (#291). Files that still need one are listed in
// AllowedSilentCatches with their exact count and reason; the count is exact, so
// fixing a catch forces the entry down with it.
public sealed class PluginSilentCatchArchitectureTests
{
    private const string NotYetTriaged = "#291 baseline: not yet triaged";

    private static readonly IReadOnlyDictionary<string, (int Count, string Reason)> AllowedSilentCatches =
        new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
        {
            ["AIConfig.cs"] = (1, NotYetTriaged),
            ["AI/AISettingsAsyncBoundary.cs"] = (2, NotYetTriaged),
            ["AI/AISettingsInfrastructureExecutor.cs"] = (2, NotYetTriaged),
            ["AI/AISettingsOperationLifetime.cs"] = (1, NotYetTriaged),
            ["Agent/AgentRuntime.cs"] = (4, NotYetTriaged),
            ["Agent/Host/AgentHostService.Discovery.cs"] = (4, NotYetTriaged),
            ["Agent/Host/AgentHostService.Lifecycle.cs"] = (1, NotYetTriaged),
            ["Agent/Host/AgentHostService.OperationHistory.cs"] = (2, NotYetTriaged),
            ["Agent/Host/AgentHostService.Response.cs"] = (1, NotYetTriaged),
            ["Agent/Host/AgentHostService.Transport.cs"] = (2, NotYetTriaged),
            ["Agent/Services/ClashItemNameService.cs"] = (2, NotYetTriaged),
            ["Agent/Services/ClashReportScreenshotCaptureService.cs"] = (1, NotYetTriaged),
            ["Agent/Services/ClashRunPreservationService.cs"] = (1, NotYetTriaged),
            ["Agent/Services/DocumentCommandService.SubtreeDump.cs"] = (1, NotYetTriaged),
            ["Agent/Services/ModelColorSchemeService.cs"] = (6, NotYetTriaged),
            ["Agent/Services/SelectionMarkupViewpointService.cs"] = (5, NotYetTriaged),
            ["ClashMarkerTool.cs"] = (3, NotYetTriaged),
            ["CopySelectedNames.cs"] = (1, NotYetTriaged),
            ["Core/Localization/UiLocalizationBindingRegistry.cs"] = (1, NotYetTriaged),
            ["Core/Logger.cs"] = (4, NotYetTriaged),
            ["ElevationGraphicsMarkerTool.cs"] = (3, NotYetTriaged),
            ["MarkupViewpoint.cs"] = (1, NotYetTriaged),
            ["SaveHierarhy.cs"] = (1, NotYetTriaged),
            ["SelectionCenterDotMarker.cs"] = (1, NotYetTriaged),
            ["SelectionHatchMarker.cs"] = (1, NotYetTriaged),
            ["WPF/NavisHelperPanel.Clash.cs"] = (1, NotYetTriaged),
            ["WPF/NavisHelperPanel.Clash.Operations.cs"] = (3, NotYetTriaged),
            ["WPF/NavisHelperSettingsTabBuilder.cs"] = (5, NotYetTriaged),
        };

    private static readonly Regex CatchHeader = new(
        @"\bcatch\b\s*(?:\(\s*(?<type>[\w.]+)(?:\s+(?<variable>\w+))?\s*\))?\s*(?:when\s*\((?<filter>[^)]*)\)\s*)?\{");

    [Fact]
    public void EverySilentCatchIsNarrowedAndJustifiedOrAllowlisted()
    {
        var pluginRoot = Path.Combine(RepositoryPaths.Root, "NavisHelper");
        var silentByFile = new Dictionary<string, List<SilentCatch>>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(pluginRoot, path).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
                continue;

            var silent = FindSilentCatches(File.ReadAllText(path));
            if (silent.Count > 0)
                silentByFile[relative] = silent;
        }

        var violations = new List<string>();
        foreach (var (file, silent) in silentByFile.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!AllowedSilentCatches.TryGetValue(file, out var allowed))
            {
                violations.AddRange(silent.Select(c => file + ":" + c.Line + " " + c.Describe()));
                continue;
            }

            if (silent.Count != allowed.Count)
                violations.Add(file + ": " + silent.Count + " silent catches, allowlist says " + allowed.Count +
                    " (lines " + string.Join(", ", silent.Select(c => c.Line)) + ")");
            if (allowed.Reason != NotYetTriaged)
                violations.AddRange(silent.Where(c => c.Empty).Select(c =>
                    file + ":" + c.Line + " allowlisted catch must still say why in a comment"));
        }

        foreach (var file in AllowedSilentCatches.Keys.Where(file => !silentByFile.ContainsKey(file)))
            violations.Add(file + ": allowlisted but has no silent catch left; remove the entry");

        Assert.True(violations.Count == 0, violations.Count + " silent catch problems:\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("try { } catch { }", true)]
    [InlineData("try { } catch\r\n{\r\n}", true)]
    [InlineData("try { } catch (Exception) { }", true)]
    [InlineData("try { } catch (System.Exception) { /* fine */ }", true)]
    [InlineData("try { } catch { // never mind\n }", true)]
    [InlineData("try { } catch (IOException) { }", true)]
    [InlineData("try { } catch (IOException) { /* Expected when the pipe closes. */ }", false)]
    [InlineData("try { } catch (Exception ex) when (ex is IOException) { // Expected when the pipe closes.\n }", false)]
    [InlineData("try { } catch (Exception ex) { Logger.Warn(\"x: \" + ex.Message, \"Ctx\"); }", false)]
    [InlineData("try { } catch { return false; }", false)]
    [InlineData("try { } catch (Exception ex) { failures.Record(ex); }", false)]
    [InlineData("try { } catch (IOException) { // Don't care: the \"pipe\" closed.\n }", false)]
    [InlineData("try { } catch { var url = \"http://x\"; }", false)]
    public void ClassifierRecognizesSilentCatches(string source, bool silent)
    {
        Assert.Equal(silent, FindSilentCatches(source).Count == 1);
    }

    private static List<SilentCatch> FindSilentCatches(string source)
    {
        var result = new List<SilentCatch>();
        foreach (Match match in CatchHeader.Matches(source))
        {
            var body = BlockBody(source, match.Index + match.Length - 1);
            if (StripComments(body).Trim().Length > 0)
                continue;

            var type = match.Groups["type"].Success ? match.Groups["type"].Value : null;
            var narrowed = match.Groups["filter"].Success ||
                (type != null && type != "Exception" && type != "System.Exception");
            var empty = body.Trim().Length == 0;
            if (narrowed && !empty)
                continue;

            var line = source.Take(match.Index).Count(ch => ch == '\n') + 1;
            result.Add(new SilentCatch(line, empty, narrowed));
        }

        return result;
    }

    private static string StripComments(string body)
    {
        var withoutBlock = Regex.Replace(body, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", "");
    }

    // Returns the text between the brace at openIndex and its matching close brace,
    // skipping braces inside comments and string and character literals. Comments
    // must be skipped too: an apostrophe in "// don't" would otherwise open a
    // character literal and swallow the closing brace.
    private static string BlockBody(string source, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                var end = source.IndexOf('\n', index);
                index = end < 0 ? source.Length : end;
                continue;
            }

            if (ch == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 1;
                continue;
            }

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

    private sealed record SilentCatch(int Line, bool Empty, bool Narrowed)
    {
        public string Describe() => Empty
            ? (Narrowed ? "narrowed catch has an empty body; say why ignoring it is correct" : "empty catch")
            : "comment-only catch of every exception; log it or narrow it to the expected type";
    }
}
