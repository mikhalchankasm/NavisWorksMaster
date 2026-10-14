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
        @"\bcatch\b\s*(?:\(\s*(?<type>[\w.:]+)(?:\s+(?<variable>\w+))?\s*\))?\s*(?:when\s*\((?<filter>(?>[^()]+|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!)))\)\s*)?\{");

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
    [InlineData("try { } catch (Exception ex) when (ShouldIgnore(ex)) { }", true)]
    [InlineData("try { } catch (Exception ex) when (ShouldIgnore(ex, Kind(ex))) { // Expected while closing.\n }", false)]
    [InlineData("var example = \"catch { }\"; // catch { }\n/* catch { } */", false)]
    [InlineData("try { } catch (Exception ex) { var s = $\"{ex.Message} }}\"; }", false)]
    [InlineData("var text = $\"{Format(\"catch { }\")}\"; var other = @$\"{Format(@\"catch { }\")}\";", false)]
    [InlineData("try { } catch (global::System.Exception) { }", true)]
    [InlineData("try { } catch (global::System.IO.IOException) { // Expected when the pipe closes.\n }", false)]
    public void ClassifierRecognizesSilentCatches(string source, bool silent)
    {
        Assert.Equal(silent, FindSilentCatches(source).Count == 1);
    }

    private static List<SilentCatch> FindSilentCatches(string source)
    {
        // Headers and braces are matched on a copy with comments and literals blanked,
        // so "catch { }" inside a string or comment is not a handler and a brace in
        // "// don't }" does not close one. Bodies are cut from the original to read
        // their comments.
        var code = MaskCommentsAndLiterals(source);
        var result = new List<SilentCatch>();
        foreach (Match match in CatchHeader.Matches(code))
        {
            var open = match.Index + match.Length - 1;
            var close = MatchingBrace(code, open);
            if (code.Substring(open + 1, close - open - 1).Trim().Length > 0)
                continue;

            var type = match.Groups["type"].Success ? match.Groups["type"].Value : null;
            if (type != null && type.StartsWith("global::", StringComparison.Ordinal))
                type = type.Substring("global::".Length);
            var narrowed = match.Groups["filter"].Success ||
                (type != null && type != "Exception" && type != "System.Exception");
            var empty = source.Substring(open + 1, close - open - 1).Trim().Length == 0;
            if (narrowed && !empty)
                continue;

            var line = source.Take(match.Index).Count(ch => ch == '\n') + 1;
            result.Add(new SilentCatch(line, empty, narrowed));
        }

        return result;
    }

    // Returns source with every comment and string or character literal replaced by
    // spaces, keeping line breaks so indexes and line numbers still match.
    private static string MaskCommentsAndLiterals(string source)
    {
        var masked = source.ToCharArray();
        void Blank(int from, int to)
        {
            for (var i = from; i < to && i < masked.Length; i++)
                if (masked[i] != '\n' && masked[i] != '\r')
                    masked[i] = ' ';
        }

        for (var index = 0; index < source.Length; index++)
        {
            var ch = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (ch == '/' && next == '/')
            {
                var end = source.IndexOf('\n', index);
                end = end < 0 ? source.Length : end;
                Blank(index, end);
                index = end;
            }
            else if (ch == '/' && next == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(index, end);
                index = end - 1;
            }
            else if (ch == '"' || ch == '\'')
            {
                var end = LiteralEnd(source, index);
                Blank(index, end + 1);
                index = end;
            }
        }

        return new string(masked);
    }

    // Returns the index of the quote that closes the literal opening at quoteIndex.
    // An interpolation hole is code, so a string nested inside one is skipped whole
    // instead of ending the outer literal.
    private static int LiteralEnd(string source, int quoteIndex)
    {
        var quote = source[quoteIndex];
        var prefixStart = quoteIndex;
        while (quote == '"' && prefixStart > 0 && quoteIndex - prefixStart < 2 &&
               (source[prefixStart - 1] == '@' || source[prefixStart - 1] == '$'))
            prefixStart--;
        var prefix = source.Substring(prefixStart, quoteIndex - prefixStart);
        var verbatim = prefix.Contains('@');
        var interpolated = prefix.Contains('$');

        for (var index = quoteIndex + 1; index < source.Length; index++)
        {
            var ch = source[index];
            if (!verbatim && ch == '\\') { index++; continue; }
            if (interpolated && ch == '{')
            {
                if (index + 1 < source.Length && source[index + 1] == '{') { index++; continue; }
                index = HoleEnd(source, index);
                continue;
            }
            if (ch == quote)
            {
                if (verbatim && index + 1 < source.Length && source[index + 1] == '"') { index++; continue; }
                return index;
            }
        }

        return source.Length - 1;
    }

    private static int HoleEnd(string source, int braceIndex)
    {
        var depth = 0;
        for (var index = braceIndex; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == '"' || ch == '\'') { index = LiteralEnd(source, index); continue; }
            if (ch == '{') depth++;
            if (ch == '}' && --depth == 0) return index;
        }

        return source.Length - 1;
    }

    private static int MatchingBrace(string code, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < code.Length; index++)
        {
            if (code[index] == '{') depth++;
            if (code[index] == '}' && --depth == 0)
                return index;
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
