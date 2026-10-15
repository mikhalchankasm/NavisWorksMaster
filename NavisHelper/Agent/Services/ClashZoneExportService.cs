using System;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashZoneExportService
    {
        private readonly DocumentCommandService _commandService;

        public ClashZoneExportService(DocumentCommandService commandService)
        {
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        }

        public ClashZoneExportResponse Execute(Document document, ClashZoneExportRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashZoneExportRequest();
            try
            {
                var options = ClashZoneExportPolicy.ToRunOptions(request);
                var now = DateTime.Now;
                var runId = ClashZoneExportPolicy.ResolveRunId(request.RunId, now);
                var directory = ClashZoneExportPolicy.ResolveRunDirectory(document.FileName, request.OutputDirectory, runId);
                var previousPath = ClashZoneExportPolicy.ResolvePreviousRecordPath(request.PreviousRunPath);
                ClashZoneRunRecord previous = null;
                if (previousPath != null)
                {
                    if (!File.Exists(previousPath))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Previous run record does not exist: " + previousPath);
                    try
                    {
                        previous = JsonConvert.DeserializeObject<ClashZoneRunRecord>(File.ReadAllText(previousPath));
                    }
                    catch (JsonException ex)
                    {
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid previous run record: " + ex.Message);
                    }
                    if (previous == null || previous.Zones == null || string.IsNullOrWhiteSpace(previous.RunId))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Previous run record must contain runId and zones.");
                    // The previous ID becomes part of the comparison filename too.
                    if (ClashZoneExportPolicy.ResolveRunId(previous.RunId, now) != previous.RunId)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Previous runId must be a plain run identifier.");
                }
                var extraction = _commandService.CollectClashZoneRows(document, request);
                var plan = ClashZoneRunPlanner.Plan(extraction.Rows, new ClashZoneRunInfo {
                    RunId = runId, TimeStamp = now.ToString("o", CultureInfo.InvariantCulture),
                    DocumentPath = document.FileName, DocumentUnits = document.Units.ToString(),
                    PluginVersion = typeof(ClashZoneExportService).Assembly.GetName().Version.ToString(), Tests = extraction.TestNames
                }, options, previous);
                var response = new ClashZoneExportResponse {
                    RunDirectory = directory, Mode = options.Mode, MatchedTestCount = extraction.MatchedTestCount,
                    ResultsTruncated = extraction.ResultsTruncated };
                response.Warnings.AddRange(extraction.Warnings);
                ClashZoneExportPolicy.Summarize(plan, response, 10);
                response.Files.Add(Path.Combine(directory, ClashZoneExportPolicy.RecordFileName));
                response.Files.Add(Path.Combine(directory, ClashZoneExportPolicy.WorkbookFileName));
                response.Files.Add(Path.Combine(directory, ClashZoneExportPolicy.MarkerFileName));
                if (previous != null)
                    response.Files.Add(Path.Combine(directory, ClashZoneExportPolicy.ComparisonFileName(previous.RunId, runId)));
                if (request.Apply != true)
                {
                    response.Message = "Dry-run only. Pass apply=true to write the run folder.";
                    return response;
                }
                if (File.Exists(response.Files[0]))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Run folder already contains zones.json: " + directory);
                var json = JsonConvert.SerializeObject(plan.Record, Formatting.Indented);
                var ifc = ClashZoneIfcWriter.Write(plan.Markers);
                Directory.CreateDirectory(directory);
                using (var stream = File.Create(response.Files[1])) XlsxWorkbookWriter.Write(stream, plan.Workbook);
                File.WriteAllText(response.Files[2], ifc, Encoding.ASCII);
                if (previous != null)
                    using (var stream = File.Create(response.Files[3])) XlsxWorkbookWriter.Write(stream, plan.ComparisonWorkbook);
                // zones.json goes last: it marks a complete run, so a failed write above leaves the
                // run id free to retry. CreateNew also protects a record created after the check.
                using (var stream = new FileStream(response.Files[0], FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
                response.Applied = true;
                response.Message = "Clash zone run folder written.";
                return response;
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }
    }
}
