using System;
using System.Globalization;
using System.IO;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SelectionExportBoundingBoxesRequest
    {
        public string OutputPath { get; set; }
        public bool? Apply { get; set; }
        public bool? Overwrite { get; set; }
        public int? MaxItems { get; set; }
        public int? Offset { get; set; }
    }

    /// <summary>
    /// A summary of fixed size: the boxes go to the CSV file, never into the answer (#427).
    /// </summary>
    public sealed class SelectionExportBoundingBoxesResponse
    {
        public bool Applied { get; set; }
        public string OutputPath { get; set; }
        public int SelectedItemCount { get; set; }
        public int Offset { get; set; }
        public int? NextOffset { get; set; }
        public int ExportedItemCount { get; set; }
        public int ItemsWithoutBoxCount { get; set; }
        public bool ItemsTruncated { get; set; }
        public bool DeadlineTruncated { get; set; }
        public long FileSizeBytes { get; set; }
        public string Sha256 { get; set; }
        public string Units { get; set; }
        public string Message { get; set; }
    }

    public static class SelectionBoundingBoxExportRules
    {
        public const int DefaultMaxItems = 10000;
        public const int MaxMaxItems = 100000;
        public const string Header = "selection_index,name,path,source_file,min_x,min_y,min_z,max_x,max_y,max_z";

        public static string Describe(SelectionExportBoundingBoxesRequest request)
        {
            var outputPath = request == null ? null : request.OutputPath;
            if (string.IsNullOrWhiteSpace(outputPath))
                return "outputPath is required.";
            outputPath = outputPath.Trim();
            // Not Path.IsPathRooted: it accepts "C:boxes.csv" and "\out\boxes.csv", which resolve
            // against the host's current drive or directory (#427 review).
            if (!ViewpointAnimationExportRules.IsFullyQualified(outputPath))
                return "outputPath must be absolute.";
            if (!string.Equals(Path.GetExtension(outputPath), ".csv", StringComparison.OrdinalIgnoreCase))
                return "outputPath must end in .csv.";

            return SelectedItemsTreePaging.DescribeOffsetError(request.Offset);
        }

        public static int ClampMaxItems(int? requested)
        {
            var value = requested.GetValueOrDefault(DefaultMaxItems);
            if (value < 1)
                return 1;
            return value > MaxMaxItems ? MaxMaxItems : value;
        }

        /// <summary>
        /// One CSV line without its line break. An item without a box keeps its row with six
        /// empty coordinates, so row count always equals the exported item count.
        /// </summary>
        public static string Row(int selectionIndex, string name, string path, string sourceFile, BoundingBoxInfo box)
        {
            var hasBox = box != null && box.Min != null && box.Max != null;
            return string.Join(",", new[]
            {
                selectionIndex.ToString(CultureInfo.InvariantCulture),
                ClashBboxPlanHelper.EscapeCsv(name),
                ClashBboxPlanHelper.EscapeCsv(path),
                ClashBboxPlanHelper.EscapeCsv(sourceFile),
                hasBox ? Number(box.Min.X) : string.Empty,
                hasBox ? Number(box.Min.Y) : string.Empty,
                hasBox ? Number(box.Min.Z) : string.Empty,
                hasBox ? Number(box.Max.X) : string.Empty,
                hasBox ? Number(box.Max.Y) : string.Empty,
                hasBox ? Number(box.Max.Z) : string.Empty,
            });
        }

        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
