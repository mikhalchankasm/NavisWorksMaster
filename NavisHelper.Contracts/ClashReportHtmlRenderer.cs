using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashReportHtmlRenderer
    {
        public static string Render(ClashGenerateReportResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\"><title>NavisHelper Clash Report</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#202124;background:#f7f8fa}h1{margin:0 0 8px}.summary{margin:0 0 20px;color:#4b5563}.item{background:#fff;border:1px solid #d9dee7;border-radius:6px;margin:0 0 18px;padding:16px}.grid{display:grid;grid-template-columns:minmax(300px,520px) 1fr;gap:16px}.shots{display:grid;grid-template-columns:1fr;gap:10px}.shot-block{margin:0}.shot-title{font-size:12px;font-weight:600;color:#475467;margin:0 0 4px}.shot{max-width:100%;border:1px solid #d9dee7;background:#f1f3f4;cursor:zoom-in}.placeholder{height:180px;display:flex;align-items:center;justify-content:center;background:#eef1f5;color:#667085;border:1px solid #d9dee7}.meta{border-collapse:collapse;width:100%}.meta th{text-align:left;width:180px;color:#475467;font-weight:600}.meta th,.meta td{padding:4px 8px;border-bottom:1px solid #edf0f4;vertical-align:top}.warn{color:#9a3412}.lightbox{position:fixed;inset:0;z-index:1000;background:rgba(15,23,42,.92);display:none;align-items:center;justify-content:center;padding:24px;cursor:zoom-out}.lightbox.open{display:flex}.lightbox img{max-width:100%;max-height:100%;object-fit:contain;background:#111827;box-shadow:0 12px 40px rgba(0,0,0,.5)}@media(min-width:1200px){.shots.two{grid-template-columns:1fr 1fr}}</style>");
            html.AppendLine("</head><body>");
            html.AppendLine("<h1>NavisHelper Clash Report</h1>");
            html.Append("<p class=\"summary\">");
            html.Append(HtmlEncode(response.ReturnedResultCount.ToString(CultureInfo.InvariantCulture)));
            html.Append(" clashes, ");
            html.Append(HtmlEncode(response.CreatedViewpointCount.ToString(CultureInfo.InvariantCulture)));
            html.Append(" viewpoints, ");
            html.Append(HtmlEncode(response.ScreenshotCount.ToString(CultureInfo.InvariantCulture)));
            html.Append(" screenshots");
            if (response.ClusterCount > 0)
            {
                html.Append(", ");
                html.Append(HtmlEncode(response.ClusterCount.ToString(CultureInfo.InvariantCulture)));
                html.Append(" clusters");
            }
            if (response.FullBoxTransparencyItemCount > 0)
            {
                html.Append(", ");
                html.Append(HtmlEncode(response.FullBoxTransparencyItemCount.ToString(CultureInfo.InvariantCulture)));
                html.Append(" context transparency applications");
            }
            if (response.ExcludedByItemNameCount > 0)
            {
                html.Append(", ");
                html.Append(HtmlEncode(response.ExcludedByItemNameCount.ToString(CultureInfo.InvariantCulture)));
                html.Append(" excluded by item-name filter");
            }
            if (response.Truncated)
                html.Append(" <span class=\"warn\">(truncated)</span>");
            html.AppendLine("</p>");

            foreach (var warning in response.Warnings)
                html.AppendLine("<p class=\"warn\">" + HtmlEncode(warning) + "</p>");

            AppendStatusSummary(html, "Status counts in selected test scope", response.TotalStatusCounts);
            AppendStatusSummary(html, "Status counts in report", response.ReturnedStatusCounts);
            AppendStatusSummary(html, "Excluded by item-name filters", response.ExcludedByItemNameCounts);
            var clusterArtifacts = string.Equals(
                response.ArtifactGranularity,
                ClashReportOptionHelper.ArtifactGranularityCluster,
                StringComparison.OrdinalIgnoreCase);
            AppendClusterSummary(html, response.Clusters, clusterArtifacts);

            foreach (var item in response.Items)
            {
                html.AppendLine("<section class=\"item\">");
                html.AppendLine("<h2>" + HtmlEncode(item.Index.ToString("0000", CultureInfo.InvariantCulture) + " " + item.ResultName) + "</h2>");
                html.AppendLine(clusterArtifacts ? "<div>" : "<div class=\"grid\"><div>");
                if (!clusterArtifacts)
                {
                    html.AppendLine("<div class=\"shots" + (item.TopViewScreenshotCaptured ? " two" : string.Empty) + "\">");
                    html.AppendLine("<figure class=\"shot-block\"><figcaption class=\"shot-title\">Default view</figcaption>");
                    if (item.ScreenshotCaptured && !string.IsNullOrWhiteSpace(item.ScreenshotPath))
                        html.AppendLine("<img class=\"shot\" src=\"" + HtmlAttributeEncode(item.ScreenshotPath) + "\" alt=\"" + HtmlAttributeEncode(item.ResultName) + "\" loading=\"lazy\" title=\"Click to enlarge\">");
                    else
                        html.AppendLine("<div class=\"placeholder\">Screenshot unavailable</div>");
                    html.AppendLine("</figure>");
                    if (item.TopViewScreenshotCaptured && !string.IsNullOrWhiteSpace(item.TopViewScreenshotPath))
                    {
                        html.AppendLine("<figure class=\"shot-block\"><figcaption class=\"shot-title\">Top view</figcaption>");
                        html.AppendLine("<img class=\"shot\" src=\"" + HtmlAttributeEncode(item.TopViewScreenshotPath) + "\" alt=\"" + HtmlAttributeEncode(item.ResultName + " top view") + "\" loading=\"lazy\" title=\"Click to enlarge\">");
                        html.AppendLine("</figure>");
                    }
                    html.AppendLine("</div>");
                    html.AppendLine("</div><div><table class=\"meta\">");
                }
                else
                {
                    html.AppendLine("<table class=\"meta\">");
                }
                AppendHtmlRow(html, "Test", item.TestName);
                AppendHtmlRow(html, "Group", item.GroupPath);
                AppendHtmlRow(html, "Cluster", item.ClusterIndex > 0 ? item.ClusterName : string.Empty);
                AppendHtmlRow(html, "Cluster id", item.ClusterId);
                AppendHtmlRow(html, "Status", item.Status);
                AppendHtmlRow(html, "Assigned to", item.AssignedTo);
                AppendHtmlRow(html, "Item A", item.Item1Name);
                AppendHtmlRow(html, "Item B", item.Item2Name);
                AppendHtmlRow(html, "Item A path", item.Item1Path);
                AppendHtmlRow(html, "Item B path", item.Item2Path);
                AppendHtmlRow(html, "Viewpoint", item.ViewpointPath);
                AppendHtmlRow(html, "Screenshot", item.ScreenshotPath);
                AppendHtmlRow(html, "Top screenshot", item.TopViewScreenshotPath);
                if (item.FullBoxTransparencyItemCount > 0)
                    AppendHtmlRow(html, "Context transparency", item.FullBoxTransparencyItemCount.ToString(CultureInfo.InvariantCulture) + " item applications");
                AppendHtmlRow(html, "Distance", item.Distance.HasValue ? item.Distance.Value.ToString("G6", CultureInfo.InvariantCulture) : string.Empty);
                AppendHtmlRow(html, "Box mode", item.BoxMode);
                AppendHtmlRow(html, "Box offset mm", item.BoxOffsetMm.ToString("G6", CultureInfo.InvariantCulture));
                AppendHtmlRow(html, "Description", item.Description);
                if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                    AppendHtmlRow(html, "Error", item.ErrorMessage);
                html.AppendLine(clusterArtifacts ? "</table></div></section>" : "</table></div></div></section>");
            }

            html.AppendLine("<div id=\"lightbox\" class=\"lightbox\" aria-hidden=\"true\"><img alt=\"\"></div>");
            html.AppendLine("<script>(function(){var box=document.getElementById('lightbox');if(!box)return;var img=box.querySelector('img');function close(){box.classList.remove('open');box.setAttribute('aria-hidden','true');img.removeAttribute('src');img.removeAttribute('alt');}document.querySelectorAll('img.shot').forEach(function(shot){shot.addEventListener('click',function(){img.src=shot.currentSrc||shot.src;img.alt=shot.alt||'';box.classList.add('open');box.setAttribute('aria-hidden','false');});});box.addEventListener('click',close);document.addEventListener('keydown',function(e){if(e.key==='Escape')close();});})();</script>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private static void AppendClusterSummary(StringBuilder html, IEnumerable<ClashClusterSummary> clusters, bool showArtifacts)
        {
            var list = clusters == null ? new List<ClashClusterSummary>() : clusters.Where(cluster => cluster != null).ToList();
            if (list.Count == 0)
                return;

            html.AppendLine("<section class=\"item\">");
            html.AppendLine("<h2>Clusters</h2>");
            foreach (var cluster in list.OrderBy(cluster => cluster.Index))
            {
                var title = "#" + cluster.Index.ToString(CultureInfo.InvariantCulture) + " " +
                    (string.IsNullOrWhiteSpace(cluster.DisplayNameA) ? "A" : cluster.DisplayNameA) +
                    " / " +
                    (string.IsNullOrWhiteSpace(cluster.DisplayNameB) ? "B" : cluster.DisplayNameB);
                html.AppendLine("<details open>");
                html.AppendLine("<summary><strong>" + HtmlEncode(title) + "</strong> - " + HtmlEncode(cluster.ClashCount.ToString(CultureInfo.InvariantCulture)) + " clashes" + (cluster.WeakAssociation ? " <span class=\"warn\">weak association</span>" : string.Empty) + "</summary>");
                html.AppendLine("<table class=\"meta\">");
                AppendHtmlRow(html, "Cluster id", cluster.ClusterId);
                AppendHtmlRow(html, "Mode", cluster.GroupMode);
                AppendHtmlRow(html, "Association A", cluster.AssociationLevelA + " | " + cluster.AssociationKeyA);
                AppendHtmlRow(html, "Association B", cluster.AssociationLevelB + " | " + cluster.AssociationKeyB);
                AppendHtmlRow(html, "Source A", cluster.SourceFileA);
                AppendHtmlRow(html, "Source B", cluster.SourceFileB);
                AppendHtmlRow(html, "Tags", cluster.Tags == null ? string.Empty : string.Join(", ", cluster.Tags));
                AppendHtmlRow(html, "Statuses", FormatStatusCounts(cluster.StatusCounts));
                if (showArtifacts)
                {
                    AppendHtmlRow(html, "Viewpoint", cluster.ViewpointPath);
                    AppendHtmlRow(html, "Screenshot", cluster.ScreenshotPath);
                    AppendHtmlRow(html, "Top screenshot", cluster.TopViewScreenshotPath);
                    if (!string.IsNullOrWhiteSpace(cluster.ArtifactErrorMessage))
                        AppendHtmlRow(html, "Artifact error", cluster.ArtifactErrorMessage);
                }
                html.AppendLine("</table>");

                if (showArtifacts)
                {
                    html.AppendLine("<div class=\"shots" + (cluster.TopViewScreenshotCaptured ? " two" : string.Empty) + "\">");
                    if (cluster.ScreenshotCaptured && !string.IsNullOrWhiteSpace(cluster.ScreenshotPath))
                        html.AppendLine("<img class=\"shot\" src=\"" + HtmlAttributeEncode(cluster.ScreenshotPath) + "\" alt=\"" + HtmlAttributeEncode(title) + "\" loading=\"lazy\" title=\"Click to enlarge\">");
                    else
                        html.AppendLine("<div class=\"placeholder\">Cluster screenshot unavailable</div>");
                    if (cluster.TopViewScreenshotCaptured && !string.IsNullOrWhiteSpace(cluster.TopViewScreenshotPath))
                        html.AppendLine("<img class=\"shot\" src=\"" + HtmlAttributeEncode(cluster.TopViewScreenshotPath) + "\" alt=\"" + HtmlAttributeEncode(title + " top view") + "\" loading=\"lazy\" title=\"Click to enlarge\">");
                    html.AppendLine("</div>");
                }

                if (cluster.PreviewRows != null && cluster.PreviewRows.Count > 0)
                {
                    html.AppendLine("<table class=\"meta\">");
                    html.AppendLine("<tr><th>Member</th><th>Result</th></tr>");
                    foreach (var row in cluster.PreviewRows)
                    {
                        var member = row.TestName + " / " + row.ResultName;
                        if (!string.IsNullOrWhiteSpace(row.GroupPath))
                            member = row.TestName + " / " + row.GroupPath + " / " + row.ResultName;
                        html.AppendLine("<tr><th>" + HtmlEncode(row.ResultHandle) + "</th><td>" + HtmlEncode(member) + "</td></tr>");
                    }
                    if (cluster.PreviewRowsTruncated)
                        html.AppendLine("<tr><th>More</th><td>Member list truncated in HTML/manifest preview.</td></tr>");
                    html.AppendLine("</table>");
                }

                html.AppendLine("</details>");
            }
            html.AppendLine("</section>");
        }

        private static void AppendStatusSummary(StringBuilder html, string title, IDictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return;

            html.AppendLine("<section class=\"item\">");
            html.AppendLine("<h2>" + HtmlEncode(title) + "</h2>");
            html.AppendLine("<table class=\"meta\">");
            foreach (var pair in counts.OrderBy(pair => GetClashStatusSortOrder(pair.Key)).ThenBy(pair => pair.Key))
                AppendHtmlRow(html, pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
            html.AppendLine("</table></section>");
        }

        private static string FormatStatusCounts(IDictionary<string, int> counts)
        {
            return ClashReportHtmlFormatHelper.FormatStatusCounts(counts);
        }

        private static int GetClashStatusSortOrder(string status)
        {
            return ClashReportHtmlFormatHelper.GetStatusSortOrder(status);
        }

        private static void AppendHtmlRow(StringBuilder html, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "";
            html.Append("<tr><th>");
            html.Append(HtmlEncode(name));
            html.Append("</th><td>");
            html.Append(HtmlEncode(value));
            html.AppendLine("</td></tr>");
        }

        private static string HtmlEncode(string value)
        {
            return ClashReportHtmlFormatHelper.HtmlEncode(value);
        }

        private static string HtmlAttributeEncode(string value)
        {
            return ClashReportHtmlFormatHelper.HtmlAttributeEncode(value);
        }
    }
}
