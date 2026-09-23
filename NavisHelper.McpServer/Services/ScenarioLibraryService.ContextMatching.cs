using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    private static ContextMatch MatchContext(
        ScenarioContext context,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel)
    {
        if (context == null ||
            ((context.NavisworksVersions?.Count ?? 0) == 0 &&
             (context.RootFilePatterns?.Count ?? 0) == 0 &&
             string.IsNullOrWhiteSpace(context.ProjectLabel)))
        {
            return new ContextMatch("weak", new List<string> { "Сценарий не содержит контекстных подсказок." });
        }

        var reasons = new List<string>();
        var conflicts = false;
        var matches = 0;
        var expected = 0;

        if ((context.NavisworksVersions?.Count ?? 0) > 0)
        {
            expected++;
            if (string.IsNullOrWhiteSpace(navisworksVersion))
                reasons.Add("Версия Navisworks не указана.");
            else if (context.NavisworksVersions.Contains(navisworksVersion, StringComparer.OrdinalIgnoreCase))
            {
                matches++;
                reasons.Add("Версия Navisworks совпадает.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Версия Navisworks не совпадает.");
            }
        }

        if ((context.RootFilePatterns?.Count ?? 0) > 0)
        {
            expected++;
            if (rootFileNames == null || rootFileNames.Count == 0)
                reasons.Add("Корневые файлы модели не указаны.");
            else if (context.RootFilePatterns.All(pattern => rootFileNames.Any(file => WildcardMatch(file, pattern))))
            {
                matches++;
                reasons.Add("Корневые файлы модели совпадают.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Корневые файлы модели не совпадают.");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.ProjectLabel))
        {
            expected++;
            if (string.IsNullOrWhiteSpace(projectLabel))
                reasons.Add("Метка проекта не указана.");
            else if (string.Equals(context.ProjectLabel.Trim(), projectLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches++;
                reasons.Add("Метка проекта совпадает.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Метка проекта не совпадает.");
            }
        }

        if (conflicts)
            return new ContextMatch("mismatch", reasons);
        if (expected > 0 && matches == expected)
            return new ContextMatch("strong", reasons);
        if (matches > 0)
            return new ContextMatch("partial", reasons);
        return new ContextMatch("weak", reasons);
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        try
        {
            var regex = "^" + Regex.Escape(pattern ?? string.Empty).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static int MatchRank(string grade)
    {
        return grade switch
        {
            "strong" => 0,
            "partial" => 1,
            "weak" => 2,
            _ => 3,
        };
    }

    private sealed record ContextMatch(string Grade, List<string> Reasons);
}
