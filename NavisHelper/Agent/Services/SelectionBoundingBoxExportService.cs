using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// selection_export_bounding_boxes (#427): bulk boxes through selected_items_tree took a dozen
    /// ~80 KB pages of context for 10 000 items. This writes them to a CSV and answers with a
    /// summary of fixed size.
    /// </summary>
    internal sealed class SelectionBoundingBoxExportService
    {
        public SelectionExportBoundingBoxesResponse Execute(Document document, SelectionExportBoundingBoxesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionExportBoundingBoxesRequest();
            var error = SelectionBoundingBoxExportRules.Describe(request);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);

            var outputPath = Path.GetFullPath(request.OutputPath.Trim());
            var offset = request.Offset ?? 0;
            var maxItems = SelectionBoundingBoxExportRules.ClampMaxItems(request.MaxItems);
            var overwrite = request.Overwrite == true;
            var selectedItems = document.CurrentSelection == null ? null : document.CurrentSelection.SelectedItems;
            var selectedCount = selectedItems == null ? 0 : selectedItems.Count;
            var pageCount = (int)Math.Max(0L, Math.Min((long)maxItems, (long)selectedCount - offset));

            var response = new SelectionExportBoundingBoxesResponse
            {
                Applied = request.Apply == true,
                OutputPath = outputPath,
                SelectedItemCount = selectedCount,
                Offset = offset,
                Units = document.Units.ToString(),
            };

            if (!response.Applied)
            {
                response.ExportedItemCount = pageCount;
                response.NextOffset = SelectedItemsTreePaging.NextOffset(offset, pageCount, selectedCount);
                response.ItemsTruncated = response.NextOffset.HasValue;
                response.Message = File.Exists(outputPath) && !overwrite
                    ? "Dry-run only. The file exists; pass overwrite=true with apply=true to replace it."
                    : "Dry-run only. Pass apply=true to write the file.";
                return response;
            }

            // Refused before reading up to 100 000 boxes; the writer checks again at the replace.
            if (File.Exists(outputPath) && !overwrite)
                throw new IOException("Output file already exists: " + outputPath + ". Pass overwrite=true to replace it.");

            var csv = new StringBuilder();
            // A byte order mark, as selection_export_properties writes: without it Excel reads
            // the file in the ANSI code page and garbles Cyrillic item names.
            csv.Append('\uFEFF');
            csv.Append(SelectionBoundingBoxExportRules.Header).Append("\r\n");
            // Keyed by the root item itself, not its display name as selected_items_tree does: two
            // models whose roots share a name would otherwise share a source file (#427 review).
            var sourceFilesByRoot = new Dictionary<ModelItem, string>();
            var consumed = 0;
            if (pageCount > 0)
            {
                foreach (ModelItem item in selectedItems.Skip(offset).Take(maxItems))
                {
                    var deadline = HostRequestDeadline.Current;
                    if (deadline != null && deadline.Expired)
                    {
                        response.DeadlineTruncated = true;
                        break;
                    }

                    var selectionIndex = offset + consumed;
                    consumed++;

                    var root = item;
                    while (root.Parent != null)
                        root = root.Parent;
                    string sourceFile;
                    if (!sourceFilesByRoot.TryGetValue(root, out sourceFile))
                    {
                        sourceFile = DocumentCommandService.TryGetSourceFile(root) ?? string.Empty;
                        sourceFilesByRoot[root] = sourceFile;
                    }

                    var box = DocumentCommandService.TryBuildBoundingBoxInfo(item);
                    if (box == null)
                        response.ItemsWithoutBoxCount++;

                    csv.Append(SelectionBoundingBoxExportRules.Row(
                        selectionIndex,
                        DocumentCommandService.GetItemDisplayName(item),
                        SearchService.PrintItemPath(item),
                        sourceFile,
                        box)).Append("\r\n");
                }
            }

            response.ExportedItemCount = consumed;
            response.NextOffset = SelectedItemsTreePaging.NextOffset(offset, consumed, selectedCount);
            response.ItemsTruncated = response.NextOffset.HasValue;

            var artifact = VerifiedFileArtifactWriter.WriteUtf8(outputPath, csv.ToString(), overwrite);
            response.OutputPath = artifact.OutputPath;
            response.FileSizeBytes = artifact.BytesWritten;
            response.Sha256 = artifact.Sha256;
            response.Message = "Export written.";
            return response;
        }
    }
}
