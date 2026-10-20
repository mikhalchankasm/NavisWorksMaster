using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// ModelColorSchemeService.cs reached 72 KB of the 80 KB per-file limit. It is split
// into partial files by concern without changing behaviour: every member declaration
// survives exactly once, and the code itself only moves.
public sealed class ModelColorSchemeServiceSplitTests
{
    private static readonly string ServicesFolder = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");

    // Non-whitespace characters of the single file before the split.
    private const int OriginalNonWhitespace = 41919;

    // Every method and constructor declaration line of the class before the split.
    private static readonly string[] Declarations =
    [
        "public ModelColorSchemeResponse Execute(",
        "public void DiscardForDocumentChange(",
        "public void HandleDocumentFileNameChanged(",
        "private ModelColorSchemeResponse Reset(",
        "private static void LogPhaseTimings(",
        "private void Analyze(",
        "private void Apply(",
        "private static List<ModelColorSchemePreparedRule> PrepareRules(",
        "private static ModelColorSchemeCollectionResult CollectItems(",
        "private static ModelColorSchemeItemFacts BuildItemFacts(",
        "private static void AddCandidate(",
        "private static ModelColorSchemeCachedPropertyFacts ReadCachedProperties(",
        "private static List<ModelColorSchemeCandidateAccumulator> SelectCandidatePreview(",
        "private static ModelColorSchemeOriginalState CaptureOriginalState(",
        "private void RestoreOriginalStates(",
        "private static bool ColorsEqual(",
        "private static ModelColorSchemeRgb ToRgb(",
        "private static string ColorKey(",
        "private static ModelItemCollection ToModelItemCollection(",
        "private static void RequestRedraw(",
        "private bool HasActiveSchemeFor(",
        "private void DiscardStaleSession(",
        "private void ClearSession(",
        "private static ModelItemCollection SnapshotSelection(",
        "private void EnsureSelectionAvailableForScope(",
        "private void RestoreSelectionIfSafe(",
        "private static void VerifyAppliedColors(",
        "private static ModelColorSchemeRgb? ToRgbOrNull(",
        "private static string NormalizeOperation(",
        "private static string NormalizeScope(",
        "private static string NormalizeVerbosity(",
        "private static int Clamp(",
        "private static bool MatchesOptionalFilters(",
        "private static bool HasPropertyMatchers(",
        "private static List<ModelItem> SafeChildren(",
        "private static string GetSourceFile(",
        "private static string GetSourceFileFromProperties(",
        "private static bool IsSourceFileProperty(",
        "private static string BuildItemPath(",
        "private static string GetPropertyValue(",
        "private static string SafeString(",
        "private static bool SafeBool(",
        "private ModelColorSchemeDocumentIdentity(",
        "public static ModelColorSchemeDocumentIdentity Capture(",
        "public bool Matches(",
        "public bool HasSameModelContent(",
        "private static HashSet<string> ReadModelIdentities(",
        "private static string NormalizePath(",
        "private static string SafeRead("
    ];

    private static string[] Parts() => Directory.GetFiles(ServicesFolder, "ModelColorSchemeService*.cs");

    [Fact]
    public void TheServiceIsSplitIntoSmallPartialFiles()
    {
        var parts = Parts();
        Assert.True(parts.Length >= 4, "expected at least 4 ModelColorSchemeService*.cs files, found " + parts.Length);
        foreach (var part in parts)
        {
            var text = File.ReadAllText(part);
            Assert.True(new FileInfo(part).Length <= 32 * 1024, Path.GetFileName(part) + " is over 32 KB");
            Assert.Matches(@"\bsealed\s+partial\s+class\s+ModelColorSchemeService\b", text);
        }
    }

    [Fact]
    public void EveryDeclarationSurvivesExactlyOnce()
    {
        var found = Parts()
            .SelectMany(part => Regex.Matches(File.ReadAllText(part), @"(?m)^ {8}(?:private|public|internal)[^=;{]*?\(")
                .Select(match => Regex.Replace(match.Value, @"\s+", " ").Trim()))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(Declarations.OrderBy(text => text, StringComparer.Ordinal), found);
    }

    // SHA-256 of the sorted, trimmed code lines of the single file before the split
    // (2c49c02), leaving out blank lines, lone braces, usings, the namespace line and the
    // class header, which every part repeats. Equal hashes mean every line moved and
    // none changed: a flipped operator changes the hash even when lengths match.
    private const string OriginalLinesSha256 = "EEFA8C3C9FBBAA1314C3C6081D280BB1BD6DAD19B9EFBF6D459742EFD3FFBAD7";

    [Fact]
    public void TheCodeOnlyMoves()
    {
        var total = Parts().Sum(part => Regex.Replace(File.ReadAllText(part), @"\s", "").Length);
        // Each extra file repeats its usings, namespace and class header.
        Assert.InRange(total, OriginalNonWhitespace, OriginalNonWhitespace + 3000);

        var lines = Parts()
            .SelectMany(part => File.ReadAllText(part).Split('\n'))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line != "{" && line != "}" &&
                           !line.StartsWith("using ", StringComparison.Ordinal) &&
                           !line.StartsWith("namespace ", StringComparison.Ordinal) &&
                           !Regex.IsMatch(line, @"^internal sealed (partial )?class ModelColorSchemeService$"))
            .OrderBy(line => line, StringComparer.Ordinal);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines))));
        Assert.Equal(OriginalLinesSha256, hash);
    }
}
