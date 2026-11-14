using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksDatasmithExportTools
{
    private readonly DatasmithExportJobService _jobs;

    public NavisworksDatasmithExportTools(DatasmithExportJobService jobs) => _jobs = jobs;

    [McpServerTool]
    [Description("Exports a SAVED .nwd/.nwf/.nwc file to Datasmith in a separate hidden Navisworks process and returns an operationId immediately. The live session is not touched. Hidden items and the section box are taken as saved in the file. Poll datasmith_export_status for the result. Supports Navisworks 2024, 2025, 2026; Navisworks 2027 has no exporter. Only one export runs at a time.")]
    [ToolCapabilities(ToolEffects.Files | ToolEffects.Host, RequiresHost = false, RequiresDocument = false)]
    public StartDatasmithExportResponse StartDatasmithExport(
        [Description("Absolute path to the saved source .nwd/.nwf/.nwc model.")] string sourcePath,
        [Description("Absolute path to the output .udatasmith file.")] string outputPath,
        [Description("Navisworks version: 2024, 2025, 2026. Empty defaults to 2026.")] string navisworksVersion = "",
        [Description("Optional nonnegative merge level; -1 omits the runner option.")] int mergeLevel = -1,
        [Description("Optional origin as three comma-separated finite numbers x,y,z, using a decimal point; empty omits the option.")] string origin = "",
        [Description("Include model metadata in the export.")] bool includeMetadata = false,
        [Description("Allow replacing an existing output file.")] bool overwrite = false,
        [Description("Export deadline in seconds: 60..14400; 0 defaults to 3600.")] int timeoutSeconds = 0)
    {
        if (mergeLevel < -1)
            throw new ArgumentException("mergeLevel must be -1 or nonnegative.", nameof(mergeLevel));
        double[] coordinates = null;
        if (!string.IsNullOrWhiteSpace(origin))
        {
            var parts = origin.Split(',');
            coordinates = new double[3];
            if (parts.Length != 3)
                throw new ArgumentException("origin requires three finite numbers: x,y,z.", nameof(origin));
            for (var index = 0; index < 3; index++)
                if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out coordinates[index]) || !double.IsFinite(coordinates[index]))
                    throw new ArgumentException("origin requires three finite numbers: x,y,z.", nameof(origin));
        }
        return _jobs.Start(new StartDatasmithExportRequest
        {
            SourcePath = sourcePath, OutputPath = outputPath, NavisworksVersion = navisworksVersion,
            MergeLevel = mergeLevel == -1 ? null : mergeLevel, Origin = coordinates,
            IncludeMetadata = includeMetadata, Overwrite = overwrite, TimeoutSeconds = timeoutSeconds,
        });
    }

    [McpServerTool]
    [Description("Reports the running, completed, or failed state of a server-owned Datasmith export, including elapsed time, runner and exporter exit codes, output size, and errors. Does not wait for the process. The server retains the last 16 jobs; ids from a previous server session are unknown.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public DatasmithExportStatusResponse DatasmithExportStatus(
        [Description("Operation id returned by start_datasmith_export.")] string operationId) => _jobs.Status(operationId);
}
