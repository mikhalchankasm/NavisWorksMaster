using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int DefaultSelectionPropertyItemLimit = 100;
        private const int MaxSelectionPropertyItemLimit = 10000;
        private const int DefaultSelectionPropertyLimitPerItem = 1000;
        private const int MaxSelectionPropertyLimitPerItem = 20000;
        private const int DefaultSelectionPropertyRowLimit = 10000;
        private const int MaxSelectionPropertyRowLimit = 200000;
        private const int DefaultSelectionDistinctValueLimit = 1000;
        private const int MaxSelectionDistinctValueLimit = 50000;

        public SelectionPropertyReportResponse SelectionPropertyReport(Document document, SelectionPropertyReportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionPropertyReportRequest();
            var itemLimit = ClampSelectionPropertyItemLimit(request.ItemLimit);
            var propertyLimitPerItem = ClampSelectionPropertyLimitPerItem(request.PropertyLimitPerItem);
            var rowLimit = ClampSelectionPropertyRowLimit(request.RowLimit);
            var includeInternalNames = request.IncludeInternalNames.GetValueOrDefault(false);
            var includeEmptyValues = request.IncludeEmptyValues.GetValueOrDefault(false);
            var categoryFilters = NormalizeReportFilters(request.CategoryFilters);
            var propertyFilters = NormalizeReportFilters(request.PropertyFilters);
            var selectedItems = document.CurrentSelection == null
                ? new List<ModelItem>()
                : document.CurrentSelection.SelectedItems.ToList();

            var response = new SelectionPropertyReportResponse
            {
                SelectedItemCount = selectedItems.Count,
                ReturnedItemCount = Math.Min(selectedItems.Count, itemLimit),
                ItemsTruncated = selectedItems.Count > itemLimit,
            };

            var itemIndex = 0;
            foreach (var item in selectedItems.Take(itemLimit))
            {
                if (item == null)
                    continue;

                itemIndex++;
                var displayName = SafeString(() => item.DisplayName);
                var path = BuildItemPath(item);
                var propertyCountForItem = 0;
                var categories = item.PropertyCategories;
                if (categories == null)
                    continue;

                foreach (var category in categories)
                {
                    if (category == null)
                        continue;

                    var categoryDisplayName = SafeString(() => category.DisplayName);
                    var categoryName = SafeString(() => category.Name);
                    var properties = category.Properties;
                    if (properties == null)
                        continue;

                    if (!MatchesReportFilters(categoryFilters, categoryDisplayName, categoryName))
                        continue;

                    foreach (var property in properties)
                    {
                        if (property == null)
                            continue;

                        var propertyDisplayName = SafeString(() => property.DisplayName);
                        var propertyName = SafeString(() => property.Name);
                        if (!MatchesReportFilters(propertyFilters, propertyDisplayName, propertyName))
                            continue;

                        var value = GetSelectionPropertyReportValue(property);
                        if (!includeEmptyValues && string.IsNullOrWhiteSpace(value))
                            continue;

                        propertyCountForItem++;
                        if (propertyCountForItem > propertyLimitPerItem)
                        {
                            response.PropertiesTruncated = true;
                            break;
                        }

                        if (response.Rows.Count >= rowLimit)
                        {
                            response.RowsTruncated = true;
                            response.RowCount = response.Rows.Count;
                            return response;
                        }

                        response.Rows.Add(new SelectionPropertyReportRow
                        {
                            ItemIndex = itemIndex,
                            Path = path,
                            DisplayName = displayName,
                            Category = categoryDisplayName,
                            CategoryInternalName = includeInternalNames ? categoryName : string.Empty,
                            Property = propertyDisplayName,
                            PropertyInternalName = includeInternalNames ? propertyName : string.Empty,
                            Value = value,
                            ValueType = property.Value == null ? string.Empty : property.Value.GetType().Name,
                        });
                    }

                    if (propertyCountForItem > propertyLimitPerItem)
                        break;
                }
            }

            response.RowCount = response.Rows.Count;
            return response;
        }

        public SelectionExportPropertiesResponse SelectionExportProperties(Document document, SelectionExportPropertiesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionExportPropertiesRequest();
            var outputPath = (request.OutputPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("output_path is required.", nameof(request));

            var format = string.IsNullOrWhiteSpace(request.Format) ? "csv" : request.Format.Trim().ToLowerInvariant();
            if (format != "csv" && format != "xlsx")
                throw new ArgumentException("Only csv and xlsx formats are currently supported.", nameof(request));

            var report = SelectionPropertyReport(document, new SelectionPropertyReportRequest
            {
                ItemLimit = request.ItemLimit,
                PropertyLimitPerItem = request.PropertyLimitPerItem,
                RowLimit = request.RowLimit,
                IncludeInternalNames = request.IncludeInternalNames,
                IncludeEmptyValues = request.IncludeEmptyValues,
                CategoryFilters = request.CategoryFilters ?? new List<string>(),
                PropertyFilters = request.PropertyFilters ?? new List<string>(),
            });

            var apply = request.Apply == true;
            var response = new SelectionExportPropertiesResponse
            {
                Applied = apply,
                OutputPath = outputPath,
                Format = format,
                SelectedItemCount = report.SelectedItemCount,
                ReturnedItemCount = report.ReturnedItemCount,
                RowCount = report.RowCount,
                ItemsTruncated = report.ItemsTruncated,
                PropertiesTruncated = report.PropertiesTruncated,
                RowsTruncated = report.RowsTruncated,
                Message = apply ? "Export written." : "Dry-run only. Pass apply=true to write the file.",
            };

            if (!apply)
                return response;

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(outputPath) && request.Overwrite != true)
                throw new IOException("Output file already exists. Pass overwrite=true to replace it.");

            if (format == "csv")
            {
                var csv = BuildSelectionPropertiesCsv(report, request.IncludeInternalNames == true);
                File.WriteAllText(outputPath, csv, new UTF8Encoding(true));
            }
            else
            {
                WriteSelectionPropertiesXlsx(outputPath, report, request.IncludeInternalNames == true);
            }

            response.FileSizeBytes = new FileInfo(outputPath).Length;
            return response;
        }

        public SelectionDistinctPropertyValuesResponse SelectionDistinctPropertyValues(Document document, SelectionDistinctPropertyValuesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionDistinctPropertyValuesRequest();
            var itemLimit = ClampSelectionPropertyItemLimit(request.ItemLimit);
            var valueLimit = ClampSelectionDistinctValueLimit(request.ValueLimit);
            var includeEmptyValues = request.IncludeEmptyValues.GetValueOrDefault(false);
            var categoryFilters = NormalizeReportFilters(request.CategoryFilters);
            var propertyFilters = NormalizeReportFilters(request.PropertyFilters);
            var selectedItems = document.CurrentSelection == null
                ? new List<ModelItem>()
                : document.CurrentSelection.SelectedItems.ToList();
            var response = new SelectionDistinctPropertyValuesResponse
            {
                SelectedItemCount = selectedItems.Count,
                ScannedItemCount = Math.Min(selectedItems.Count, itemLimit),
                ItemsTruncated = selectedItems.Count > itemLimit,
            };
            var valuesByKey = new Dictionary<string, SelectionDistinctPropertyValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in selectedItems.Take(itemLimit))
            {
                if (item == null || item.PropertyCategories == null)
                    continue;

                var itemName = SafeString(() => item.DisplayName);
                var itemPath = BuildItemPath(item);

                foreach (var category in item.PropertyCategories)
                {
                    if (category == null || category.Properties == null)
                        continue;

                    var categoryDisplayName = SafeString(() => category.DisplayName);
                    var categoryName = SafeString(() => category.Name);
                    if (!MatchesReportFilters(categoryFilters, categoryDisplayName, categoryName))
                        continue;

                    foreach (var property in category.Properties)
                    {
                        if (property == null)
                            continue;

                        var propertyDisplayName = SafeString(() => property.DisplayName);
                        var propertyName = SafeString(() => property.Name);
                        if (!MatchesReportFilters(propertyFilters, propertyDisplayName, propertyName))
                            continue;

                        var value = GetSelectionPropertyReportValue(property);
                        if (!includeEmptyValues && string.IsNullOrWhiteSpace(value))
                            continue;

                        response.MatchedPropertyCount++;
                        var key = categoryDisplayName + "\u001f" + propertyDisplayName + "\u001f" + value;
                        SelectionDistinctPropertyValue bucket;
                        if (!valuesByKey.TryGetValue(key, out bucket))
                        {
                            bucket = new SelectionDistinctPropertyValue
                            {
                                Value = value,
                                Count = 0,
                                Category = categoryDisplayName,
                                Property = propertyDisplayName,
                                SampleItemPath = itemPath,
                                SampleItemName = itemName,
                            };
                            valuesByKey[key] = bucket;
                        }

                        bucket.Count++;
                    }
                }
            }

            var ordered = valuesByKey.Values
                .OrderByDescending(value => value.Count)
                .ThenBy(value => value.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Property, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            response.DistinctValueCount = ordered.Count;
            response.ValuesTruncated = ordered.Count > valueLimit;
            response.Values = ordered.Take(valueLimit).ToList();
            response.ReturnedValueCount = response.Values.Count;
            return response;
        }

        private static int ClampSelectionPropertyItemLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionPropertyItemLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionPropertyItemLimit)
                return MaxSelectionPropertyItemLimit;
            return value;
        }

        private static int ClampSelectionPropertyLimitPerItem(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionPropertyLimitPerItem);
            if (value < 1)
                return 1;
            if (value > MaxSelectionPropertyLimitPerItem)
                return MaxSelectionPropertyLimitPerItem;
            return value;
        }

        private static int ClampSelectionPropertyRowLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionPropertyRowLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionPropertyRowLimit)
                return MaxSelectionPropertyRowLimit;
            return value;
        }

        private static int ClampSelectionDistinctValueLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionDistinctValueLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionDistinctValueLimit)
                return MaxSelectionDistinctValueLimit;
            return value;
        }

        private static List<string> NormalizeReportFilters(IEnumerable<string> filters)
        {
            if (filters == null)
                return new List<string>();

            return filters
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesReportFilters(List<string> filters, params string[] values)
        {
            if (filters == null || filters.Count == 0)
                return true;

            foreach (var filter in filters)
            {
                foreach (var value in values)
                {
                    if (!string.IsNullOrEmpty(value) &&
                        value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        private static string SafeString(Func<string> read)
        {
            if (read == null)
                return string.Empty;

            try
            {
                return read() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSelectionPropertyReportValue(DataProperty property)
        {
            if (property == null || property.Value == null)
                return string.Empty;

            try
            {
                return property.Value.ToDisplayString() ?? string.Empty;
            }
            catch
            {
                return property.Value.ToString() ?? string.Empty;
            }
        }

        private static string BuildSelectionPropertiesCsv(SelectionPropertyReportResponse report, bool includeInternalNames)
        {
            var builder = new StringBuilder();
            if (includeInternalNames)
                builder.AppendLine("Path;DisplayName;Category;CategoryInternalName;Property;PropertyInternalName;Value;ValueType");
            else
                builder.AppendLine("Path;DisplayName;Category;Property;Value;ValueType");

            if (report == null || report.Rows == null)
                return builder.ToString();

            foreach (var row in report.Rows)
            {
                if (includeInternalNames)
                {
                    builder
                        .Append(EscapeCsv(row.Path)).Append(';')
                        .Append(EscapeCsv(row.DisplayName)).Append(';')
                        .Append(EscapeCsv(row.Category)).Append(';')
                        .Append(EscapeCsv(row.CategoryInternalName)).Append(';')
                        .Append(EscapeCsv(row.Property)).Append(';')
                        .Append(EscapeCsv(row.PropertyInternalName)).Append(';')
                        .Append(EscapeCsv(row.Value)).Append(';')
                        .Append(EscapeCsv(row.ValueType)).AppendLine();
                }
                else
                {
                    builder
                        .Append(EscapeCsv(row.Path)).Append(';')
                        .Append(EscapeCsv(row.DisplayName)).Append(';')
                        .Append(EscapeCsv(row.Category)).Append(';')
                        .Append(EscapeCsv(row.Property)).Append(';')
                        .Append(EscapeCsv(row.Value)).Append(';')
                        .Append(EscapeCsv(row.ValueType)).AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void WriteSelectionPropertiesXlsx(string outputPath, SelectionPropertyReportResponse report, bool includeInternalNames)
        {
            using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteZipEntry(archive, "[Content_Types].xml", BuildXlsxContentTypesXml());
                WriteZipEntry(archive, "_rels/.rels", BuildXlsxRootRelsXml());
                WriteZipEntry(archive, "docProps/app.xml", BuildXlsxAppPropsXml());
                WriteZipEntry(archive, "docProps/core.xml", BuildXlsxCorePropsXml());
                WriteZipEntry(archive, "xl/workbook.xml", BuildXlsxWorkbookXml());
                WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", BuildXlsxWorkbookRelsXml());
                WriteZipEntry(archive, "xl/styles.xml", BuildXlsxStylesXml());
                WriteZipEntry(archive, "xl/worksheets/sheet1.xml", BuildXlsxWorksheetXml(report, includeInternalNames));
            }
        }

        private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string BuildXlsxContentTypesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                   "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                   "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                   "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
                   "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                   "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                   "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                   "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                   "</Types>";
        }

        private static string BuildXlsxRootRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                   "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildXlsxWorkbookXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets><sheet name=\"Selection Properties\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                   "</workbook>";
        }

        private static string BuildXlsxWorkbookRelsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                   "</Relationships>";
        }

        private static string BuildXlsxStylesXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                   "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                   "</styleSheet>";
        }

        private static string BuildXlsxAppPropsXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                   "<Application>NavisHelper</Application>" +
                   "</Properties>";
        }

        private static string BuildXlsxCorePropsXml()
        {
            var timestamp = XmlEscape(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   "<dc:creator>NavisHelper</dc:creator>" +
                   "<cp:lastModifiedBy>NavisHelper</cp:lastModifiedBy>" +
                   "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + timestamp + "</dcterms:created>" +
                   "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + timestamp + "</dcterms:modified>" +
                   "</cp:coreProperties>";
        }

        private static string BuildXlsxWorksheetXml(SelectionPropertyReportResponse report, bool includeInternalNames)
        {
            var headers = includeInternalNames
                ? new[] { "Path", "DisplayName", "Category", "CategoryInternalName", "Property", "PropertyInternalName", "Value", "ValueType" }
                : new[] { "Path", "DisplayName", "Category", "Property", "Value", "ValueType" };
            var rowCount = report == null || report.Rows == null ? 1 : report.Rows.Count + 1;
            var dimension = "A1:" + GetXlsxColumnName(headers.Length) + rowCount;
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            builder.Append("<dimension ref=\"").Append(dimension).Append("\"/>");
            builder.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
            builder.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            builder.Append("<sheetData>");
            AppendXlsxRow(builder, 1, headers, true);

            if (report != null && report.Rows != null)
            {
                var rowNumber = 2;
                foreach (var row in report.Rows)
                {
                    if (includeInternalNames)
                    {
                        AppendXlsxRow(builder, rowNumber, new[]
                        {
                            row.Path,
                            row.DisplayName,
                            row.Category,
                            row.CategoryInternalName,
                            row.Property,
                            row.PropertyInternalName,
                            row.Value,
                            row.ValueType,
                        }, false);
                    }
                    else
                    {
                        AppendXlsxRow(builder, rowNumber, new[]
                        {
                            row.Path,
                            row.DisplayName,
                            row.Category,
                            row.Property,
                            row.Value,
                            row.ValueType,
                        }, false);
                    }

                    rowNumber++;
                }
            }

            builder.Append("</sheetData>");
            builder.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            builder.Append("</worksheet>");
            return builder.ToString();
        }

        private static void AppendXlsxRow(StringBuilder builder, int rowNumber, IList<string> values, bool header)
        {
            builder.Append("<row r=\"").Append(rowNumber).Append("\">");
            for (var i = 0; i < values.Count; i++)
            {
                var cellRef = GetXlsxColumnName(i + 1) + rowNumber;
                builder.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\"");
                if (header)
                    builder.Append(" s=\"1\"");
                builder.Append("><is><t>").Append(XmlEscape(values[i])).Append("</t></is></c>");
            }

            builder.Append("</row>");
        }

        private static string GetXlsxColumnName(int columnNumber)
        {
            var name = string.Empty;
            while (columnNumber > 0)
            {
                var modulo = (columnNumber - 1) % 26;
                name = Convert.ToChar('A' + modulo) + name;
                columnNumber = (columnNumber - modulo) / 26;
            }

            return name;
        }

        private static string XmlEscape(string value)
        {
            value = SanitizeXmlString(value);
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string SanitizeXmlString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (XmlConvert.IsXmlChar(ch))
                    builder.Append(ch);
            }

            return builder.ToString();
        }
    }
}
