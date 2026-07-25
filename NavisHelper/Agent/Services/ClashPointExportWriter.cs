using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace NavisHelper.Agent.Services
{
    internal static class ClashPointExportWriter
    {
        private static readonly string[] Headers =
        {
            "Test", "Group", "Status", "DisciplinePair", "ResultHandle",
            "GlobalX", "GlobalY", "GlobalZ", "LocalX_m", "LocalY_m", "Elevation_m",
            "Level", "GridCell", "ItemA", "ItemB"
        };

        public static void WriteCsv(string path, IList<ClashPointExportRow> rows)
        {
            var lines = new List<string> { string.Join(";", Headers.Select(EscapeCsv)) };
            lines.AddRange((rows ?? new List<ClashPointExportRow>()).Select(row => string.Join(";", ToValues(row).Select(EscapeCsv))));
            File.WriteAllLines(path, lines, new UTF8Encoding(true));
        }

        public static void WriteXlsx(string path, IList<ClashPointExportRow> rows)
        {
            var source = rows ?? new List<ClashPointExportRow>();
            var byLevel = source.GroupBy(row => row.Level ?? string.Empty)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new[] { string.IsNullOrWhiteSpace(group.Key) ? "(без этажа)" : group.Key, group.Count().ToString(CultureInfo.InvariantCulture) })
                .ToList();
            var levels = source.Select(row => row.Level ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            var gridGroups = source.GroupBy(row => row.GridCell ?? string.Empty).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var matrix = new List<string[]>();
            matrix.Add(new[] { "GridCell" }.Concat(levels.Select(level => string.IsNullOrWhiteSpace(level) ? "(без этажа)" : level)).ToArray());
            foreach (var grid in gridGroups)
            {
                matrix.Add(new[] { string.IsNullOrWhiteSpace(grid.Key) ? "(без ячейки)" : grid.Key }
                    .Concat(levels.Select(level => grid.Count(row => string.Equals(row.Level ?? string.Empty, level, StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture)))
                    .ToArray());
            }

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", ContentTypesXml());
                WriteEntry(archive, "_rels/.rels", RootRelationshipsXml());
                WriteEntry(archive, "xl/workbook.xml", WorkbookXml());
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
                WriteEntry(archive, "xl/styles.xml", StylesXml());
                WriteEntry(archive, "xl/worksheets/sheet1.xml", WorksheetXml(new[] { Headers }.Concat(source.Select(ToValues))));
                WriteEntry(archive, "xl/worksheets/sheet2.xml", WorksheetXml(new[] { new[] { "Level", "ClashCount" } }.Concat(byLevel)));
                WriteEntry(archive, "xl/worksheets/sheet3.xml", WorksheetXml(matrix));
            }
        }

        private static string[] ToValues(ClashPointExportRow row)
        {
            return new[]
            {
                row.TestName, row.GroupPath, row.Status, row.DisciplinePair, row.ResultHandle,
                F(row.GlobalX), F(row.GlobalY), F(row.GlobalZ), F(row.LocalXM), F(row.LocalYM), F(row.ElevationM),
                row.Level, row.GridCell, row.ItemA, row.ItemB,
            };
        }

        private static string WorksheetXml(IEnumerable<string[]> rows)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            var rowIndex = 0;
            foreach (var row in rows ?? Enumerable.Empty<string[]>())
            {
                rowIndex++;
                xml.Append("<row r=\"").Append(rowIndex).Append("\">");
                var columnIndex = 0;
                foreach (var value in row ?? new string[0])
                {
                    columnIndex++;
                    xml.Append("<c r=\"").Append(ColumnName(columnIndex)).Append(rowIndex).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                        .Append(SecurityElement.Escape(value ?? string.Empty)).Append("</t></is></c>");
                }
                xml.Append("</row>");
            }
            xml.Append("</sheetData></worksheet>");
            return xml.ToString();
        }

        private static string ColumnName(int index)
        {
            var name = string.Empty;
            while (index > 0)
            {
                index--;
                name = (char)('A' + index % 26) + name;
                index /= 26;
            }
            return name;
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static string EscapeCsv(string value)
        {
            var text = value ?? string.Empty;
            return text.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string F(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }

        private static string ContentTypesXml() { return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>"; }
        private static string RootRelationshipsXml() { return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"; }
        private static string WorkbookXml() { return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Коллизии\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"По этажам\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"Сетка x этаж\" sheetId=\"3\" r:id=\"rId3\"/></sheets></workbook>"; }
        private static string WorkbookRelationshipsXml() { return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/><Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>"; }
        private static string StylesXml() { return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>"; }
    }

    internal sealed class ClashPointExportRow
    {
        public string TestName;
        public string GroupPath;
        public string Status;
        public string DisciplinePair;
        public string ResultHandle;
        public double GlobalX;
        public double GlobalY;
        public double GlobalZ;
        public double LocalXM;
        public double LocalYM;
        public double ElevationM;
        public string Level;
        public string GridCell;
        public string ItemA;
        public string ItemB;
    }
}
