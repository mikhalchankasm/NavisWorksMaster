using System.Globalization;
using NavisHelper.Agent.Services;
using NavisHelper.Core.Localization;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #264: two sessions of one client looked identical in the protected mode panel, so
// the owner could not tell which session a Revoke would hit. Every row now carries a
// short session tag, the first six characters of the session id in lower case.
// #265: a request without a client name keeps that name empty; the panel renders it
// through Settings_ProtectedMode_UnknownClient in the panel language.
public sealed class ProtectedModeSessionTagTests : IDisposable
{
    private const string SessionA = "3F9A1C77-0B2D-4E5F-8A9B-1C2D3E4F5A6B";
    private const string SessionB = "7d04e2aa-5c1b-4f3e-9a8d-6b5c4d3e2f1a";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelper-SessionTag-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static ProtectedModeApprovals Gate(params int[] codes)
    {
        var queue = new Queue<int>(codes);
        return new ProtectedModeApprovals(() => new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc), () => queue.Dequeue())
        {
            Enabled = true
        };
    }

    private UiLocalizationService Localization(string culture) =>
        new(new UiLanguageSettingsStore(Path.Combine(_directory, culture + ".ini")), new CultureInfo(culture));

    [Theory]
    [InlineData(SessionA, "3f9a1c")]
    [InlineData("ab", "ab")]
    [InlineData("AbCdEf", "abcdef")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void SessionTagIsTheFirstSixCharactersInLowerCase(string sessionId, string expected)
    {
        Assert.Equal(expected, ProtectedModeLabels.SessionTag(sessionId));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void PendingRowsOfOneClientDifferBySessionTag(string culture)
    {
        var gate = Gate(1, 2);
        gate.Check("save_document", "C:\\models\\a.nwd", SessionA, "claude-code");
        gate.Check("save_document", "C:\\models\\a.nwd", SessionB, "claude-code");
        var localization = Localization(culture);

        var rows = gate.Pending.Select(request => ProtectedModeLabels.RequestRow(localization, request, "a.nwd")).ToList();

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0], rows[1]);
        Assert.Contains("claude-code [3f9a1c]", rows[0]);
        Assert.Contains("claude-code [7d04e2]", rows[1]);
        Assert.All(rows, row => Assert.Contains("a.nwd", row));
        Assert.Contains("0001", rows[0]);
        Assert.Contains("0002", rows[1]);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void GrantRowsOfOneClientDifferBySessionTag(string culture)
    {
        var gate = Gate(1, 2);
        gate.Check("save_document", "C:\\models\\a.nwd", SessionA, "claude-code");
        gate.Check("save_document", "C:\\models\\a.nwd", SessionB, "claude-code");
        gate.Approve("0001");
        gate.Approve("0002");
        var localization = Localization(culture);

        var rows = gate.Approved.Select(grant => ProtectedModeLabels.GrantRow(localization, grant, "a.nwd")).ToList();

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0], rows[1]);
        Assert.Contains("claude-code [3f9a1c]", rows[0]);
        Assert.Contains("claude-code [7d04e2]", rows[1]);
    }

    [Theory]
    [InlineData("en-US", "unknown client")]
    [InlineData("ru-RU", "неизвестный клиент")]
    public void MissingClientNameIsRenderedFromTheResource(string culture, string expected)
    {
        var gate = Gate(1);
        gate.Check("save_document", "C:\\models\\a.nwd", SessionA, null);
        var localization = Localization(culture);
        Assert.Equal(expected, localization.GetString("Settings_ProtectedMode_UnknownClient"));

        var pending = Assert.Single(gate.Pending);
        Assert.Contains(expected + " [3f9a1c]", ProtectedModeLabels.RequestRow(localization, pending, "a.nwd"));

        gate.Approve("0001");
        var grant = Assert.Single(gate.Approved);
        Assert.Contains(expected + " [3f9a1c]", ProtectedModeLabels.GrantRow(localization, grant, "a.nwd"));
    }

    [Fact]
    public void PanelSectionBuildsRowsThroughTheLabels()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "WPF", "ProtectedModeSection.cs"));
        Assert.Contains("ProtectedModeLabels.RequestRow(", source);
        Assert.Contains("ProtectedModeLabels.GrantRow(", source);
        Assert.DoesNotContain("unknown client", source, StringComparison.OrdinalIgnoreCase);
    }
}
