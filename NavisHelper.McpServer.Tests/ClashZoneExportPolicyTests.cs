using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for the pure part of clash_zones_export (docs/CLASH_ZONES_PLAN.md, sections 6-7, PR 6b).
public sealed class ClashZoneExportPolicyTests
{
    private static readonly DateTime Now = new DateTime(2026, 10, 15, 21, 5, 9);

    [Fact]
    public void ResolveRunId_UsesTheRequestOrTheLocalTimestamp()
    {
        Assert.Equal("20261015-210509", ClashZoneExportPolicy.ResolveRunId(null, Now));
        Assert.Equal("20261015-210509", ClashZoneExportPolicy.ResolveRunId("  ", Now));
        Assert.Equal("iter-07_b", ClashZoneExportPolicy.ResolveRunId(" iter-07_b ", Now));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("run 1")]
    [InlineData("ж")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void ResolveRunId_RejectsAnythingThatIsNotAPlainFolderName(string runId)
    {
        Assert.Throws<ArgumentException>(() => ClashZoneExportPolicy.ResolveRunId(runId, Now));
    }

    [Fact]
    public void ResolveRunDirectory_DefaultsToClashZonesBesideTheDocument()
    {
        var documentPath = Path.Combine(Path.GetTempPath(), "models", "site.nwd");

        var directory = ClashZoneExportPolicy.ResolveRunDirectory(documentPath, null, "run-1");

        Assert.Equal(Path.Combine(Path.GetTempPath(), "models", "clash_zones", "run-1"), directory);
    }

    [Fact]
    public void ResolveRunDirectory_UsesTheOutputDirectoryWhenGiven()
    {
        var output = Path.Combine(Path.GetTempPath(), "zones-out");

        Assert.Equal(Path.Combine(output, "run-1"), ClashZoneExportPolicy.ResolveRunDirectory(null, output, "run-1"));
    }

    [Fact]
    public void ResolveRunDirectory_NeedsADocumentPathOrAnOutputDirectory()
    {
        var error = Assert.Throws<ArgumentException>(() => ClashZoneExportPolicy.ResolveRunDirectory("", "", "run-1"));
        Assert.Contains("outputDirectory", error.Message);
    }

    [Fact]
    public void ResolvePreviousRecordPath_AcceptsARunFolderOrItsJson()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clash_zones", "run-1");

        Assert.Null(ClashZoneExportPolicy.ResolvePreviousRecordPath(null));
        Assert.Null(ClashZoneExportPolicy.ResolvePreviousRecordPath(" "));
        Assert.Equal(Path.Combine(folder, "zones.json"), ClashZoneExportPolicy.ResolvePreviousRecordPath(folder));
        Assert.Equal(Path.Combine(folder, "zones.json"), ClashZoneExportPolicy.ResolvePreviousRecordPath(Path.Combine(folder, "zones.json")));
    }

    [Fact]
    public void FileNames_AreFixedAndTheComparisonNamesBothRuns()
    {
        Assert.Equal("zones.json", ClashZoneExportPolicy.RecordFileName);
        Assert.Equal("zones.xlsx", ClashZoneExportPolicy.WorkbookFileName);
        Assert.Equal("zones.ifc", ClashZoneExportPolicy.MarkerFileName);
        Assert.Equal("clash_zones_compare_run-1_run-2.xlsx", ClashZoneExportPolicy.ComparisonFileName("run-1", "run-2"));
    }

    [Fact]
    public void ToRunOptions_MapsTheRequestAndKeepsDefaults()
    {
        var options = ClashZoneExportPolicy.ToRunOptions(new ClashZoneExportRequest { Mode = "POINTS", EpsMm = 250, Statuses = new List<string> { "new" } });

        Assert.Equal("points", options.Mode);
        Assert.Equal(250, options.EpsMm);
        Assert.Equal(3000, options.MaxZoneRadiusMm);
        Assert.Equal(150, options.MinRadiusMm);
        Assert.Equal(100, options.PadMm);
        Assert.Equal(50, options.PointRadiusMm);
        Assert.Equal(new[] { "new" }, options.Statuses);
        Assert.False(options.AllowParameterMismatch);

        var defaults = ClashZoneExportPolicy.ToRunOptions(new ClashZoneExportRequest());
        Assert.Equal("zones", defaults.Mode);
        Assert.Equal(300, defaults.EpsMm);
        Assert.Null(defaults.Statuses);
    }

    [Theory]
    [InlineData("cubes", null, null)]
    [InlineData(null, 0.0, null)]
    [InlineData(null, double.NaN, null)]
    [InlineData(null, null, -5.0)]
    public void ToRunOptions_RejectsInvalidValues(string mode, double? eps, double? pad)
    {
        Assert.Throws<ArgumentException>(() => ClashZoneExportPolicy.ToRunOptions(new ClashZoneExportRequest { Mode = mode, EpsMm = eps, PadMm = pad }));
    }

    [Theory]
    [InlineData(null, 50000)]
    [InlineData(0, 50000)]
    [InlineData(120, 120)]
    [InlineData(900000, 50000)]
    public void ClampMaxResults_CapsAt50000(int? requested, int expected)
    {
        Assert.Equal(expected, ClashZoneExportPolicy.ClampMaxResults(requested));
    }

    [Fact]
    public void Summarize_CountsStatesAndListsTheLargestZones()
    {
        var plan = ClashZoneRunPlanner.Plan(
            Enumerable.Range(0, 14).Select(i => Clash("g" + i.ToString("D2"), "P" + i, "B", i * 10000.0, 0, 0)).ToList(),
            new ClashZoneRunInfo { RunId = "run-1", TimeStamp = "2026-10-15T21:05:09" },
            null,
            null);

        var response = new ClashZoneExportResponse();
        ClashZoneExportPolicy.Summarize(plan, response, 10);

        Assert.Equal("run-1", response.RunId);
        Assert.Equal(14, response.ZoneCount);
        Assert.Equal(14, response.DrawnClashCount);
        Assert.Equal(14, response.StateCounts["new"]);
        Assert.Equal(10, response.LargestZones.Count);
        Assert.All(response.LargestZones, z => Assert.False(string.IsNullOrEmpty(z.TrackId)));
        Assert.Equal(response.LargestZones.OrderByDescending(z => z.RadiusMm).ThenBy(z => z.ZoneId, StringComparer.Ordinal).Select(z => z.ZoneId),
            response.LargestZones.Select(z => z.ZoneId));
    }

    private static ClashZoneRawClash Clash(string guid, string a, string b, double x, double y, double z)
    {
        return new ClashZoneRawClash
        {
            ResultGuid = guid,
            ClashName = guid,
            TestName = "T",
            Status = "New",
            X = x,
            Y = y,
            Z = z,
            AssociationKeyA = a,
            AssociationKeyB = b,
            AssociationNameA = a,
            AssociationNameB = b,
        };
    }
}
