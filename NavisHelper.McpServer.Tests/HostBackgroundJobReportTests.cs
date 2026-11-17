using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #508 slice b: clash_generate_report (5 000 results, about 3 min) and clash_bbox_pair_plan with
// refineDepth 2 over 426 roots outran the MCP call in the Port run. Both get background=true on the
// server-side job store from slice a (#518): the call returns backgroundOperationId at once and
// background_job_status returns the tool's own response when the host finishes.
public sealed class HostBackgroundJobReportTests
{
    [Theory]
    [InlineData(typeof(NavisworksClashReportTools), nameof(NavisworksClashReportTools.ClashGenerateReport), typeof(ClashGenerateReportResponse))]
    [InlineData(typeof(NavisworksClashTestTools), nameof(NavisworksClashTestTools.ClashBboxPairPlan), typeof(ClashBboxPairPlanResponse))]
    public void TheToolTakesBackgroundAndItsResponseCarriesTheOperationId(Type tools, string method, Type response)
    {
        var background = tools.GetMethod(method)!.GetParameters().Single(parameter => parameter.Name == "background");
        Assert.Equal(typeof(bool), background.ParameterType);
        Assert.Equal(false, background.DefaultValue);
        Assert.Equal(typeof(string), response.GetProperty("BackgroundOperationId")?.PropertyType);
    }

    [Theory]
    [InlineData("NavisworksClashReportTools.cs", "_jobs.Start(\"clash_generate_report\"")]
    [InlineData("NavisworksClashTestTools.cs", "_jobs.Start(\"clash_bbox_pair_plan\"")]
    public void TheBackgroundCallUsesTheJobStoreAndTheLongTimeout(string file, string start)
    {
        var source = File.ReadAllText(Path.Combine(Root(), "NavisHelper.McpServer", "Tools", file));
        Assert.Contains(start, source, StringComparison.Ordinal);
        Assert.Contains("BackgroundHostTimeoutMs", source, StringComparison.Ordinal);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
