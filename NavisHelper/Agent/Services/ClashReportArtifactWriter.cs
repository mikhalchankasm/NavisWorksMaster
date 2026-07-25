using System.IO;
using System.Linq;
using System.Text;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportArtifactWriter
    {
        public static void WriteArtifacts(
            ClashGenerateReportResponse response,
            ClashGenerateReportResponse fileResponse,
            string reportHtml,
            JsonSerializerSettings jsonSettings)
        {
            if (response == null || fileResponse == null)
                return;

            File.WriteAllText(response.ManifestPath, JsonConvert.SerializeObject(fileResponse, jsonSettings), Encoding.UTF8);
            File.WriteAllText(response.ClashBoxesPath, JsonConvert.SerializeObject(fileResponse.Items.Select(item => new
            {
                item.Index,
                item.TestName,
                item.ResultName,
                item.ClashPoint,
                item.ClashBox,
                item.BoxOffsetMm,
                item.BoxMode,
                item.ClusterIndex,
                item.ClusterId,
                item.ClusterName,
                item.ViewpointPath,
                item.TopViewScreenshotPath,
                item.FullBoxTransparencyItemCount,
            }), jsonSettings), Encoding.UTF8);
            File.WriteAllText(response.ReportPath, reportHtml ?? string.Empty, Encoding.UTF8);
        }
    }
}
