using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class XlsxSheet
    {
        public string Name { get; set; }
        public IList<string> Columns { get; set; }
        public IList<IList<object>> Rows { get; set; }
    }

    public static class XlsxWorkbookWriter
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        private const int MaxCellText = 32767;
        private const int MaxRows = 1048576;
        private const int MaxColumns = 16384;
        // SpreadsheetML reads "_xHHHH_" in cell text as an escaped character; a literal one is
        // written with its underscore escaped as "_x005F_".
        private static readonly Regex EscapeLike = new Regex("_(x[0-9A-Fa-f]{4}_)", RegexOptions.CultureInvariant);
        private static readonly DateTimeOffset EntryTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static void Write(Stream output, IEnumerable<XlsxSheet> sheets)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sheets == null) throw new ArgumentNullException(nameof(sheets));
            var source = sheets.ToList();
            if (source.Count == 0) throw new ArgumentException("At least one sheet is required.", nameof(sheets));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in source)
            {
                if (sheet == null || string.IsNullOrEmpty(sheet.Name) || sheet.Name.Length > 31 ||
                    sheet.Name.IndexOfAny(new[] { '[', ']', ':', '*', '?', '/', '\\' }) >= 0 ||
                    sheet.Name[0] == '\'' || sheet.Name[sheet.Name.Length - 1] == '\'' ||
                    string.Equals(sheet.Name, "History", StringComparison.OrdinalIgnoreCase) ||
                    CleanXml(sheet.Name) != sheet.Name || !names.Add(sheet.Name))
                    throw new ArgumentException("Sheet names must be valid and unique ignoring case.", nameof(sheets));
                var header = sheet.Columns != null && sheet.Columns.Count > 0 ? 1 : 0;
                var rows = sheet.Rows == null ? 0 : sheet.Rows.Count;
                var width = Math.Max(sheet.Columns == null ? 0 : sheet.Columns.Count,
                    sheet.Rows == null ? 0 : sheet.Rows.Select(r => r == null ? 0 : r.Count).DefaultIfEmpty(0).Max());
                if (header + rows > MaxRows || width > MaxColumns)
                    throw new ArgumentException("Sheet " + sheet.Name + " exceeds Excel's 1048576 rows or 16384 columns.", nameof(sheets));
            }

            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var types = new XElement(ContentTypes + "Types",
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"),
                        new XAttribute("ContentType", "application/xml")),
                    Override("/xl/workbook.xml", "sheet.main"), Override("/xl/styles.xml", "styles"));
                var workbook = new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel.NamespaceName));
                var sheetList = new XElement(Main + "sheets");
                workbook.Add(sheetList);
                var relationships = new XElement(PackageRel + "Relationships");
                for (var i = 0; i < source.Count; i++)
                {
                    var number = Invariant(i + 1);
                    var path = "worksheets/sheet" + number + ".xml";
                    types.Add(Override("/xl/" + path, "worksheet"));
                    sheetList.Add(new XElement(Main + "sheet", new XAttribute("name", source[i].Name),
                        new XAttribute("sheetId", number), new XAttribute(Rel + "id", "rId" + number)));
                    relationships.Add(Relationship("rId" + number, "worksheet", path));
                }
                relationships.Add(Relationship("rId" + Invariant(source.Count + 1), "styles", "styles.xml"));
                WritePart(archive, "[Content_Types].xml", types);
                WritePart(archive, "_rels/.rels", new XElement(PackageRel + "Relationships",
                    Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                WritePart(archive, "xl/workbook.xml", workbook);
                WritePart(archive, "xl/_rels/workbook.xml.rels", relationships);
                WritePart(archive, "xl/styles.xml", Styles());
                for (var i = 0; i < source.Count; i++)
                {
                    var sheet = source[i];
                    WriteEntry(archive, "xl/worksheets/sheet" + Invariant(i + 1) + ".xml",
                        writer => WriteSheet(writer, sheet));
                }
            }
        }

        private static XElement Override(string path, string type)
        {
            return new XElement(ContentTypes + "Override", new XAttribute("PartName", path),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + type + "+xml"));
        }

        private static XElement Relationship(string id, string type, string target)
        {
            return new XElement(PackageRel + "Relationship", new XAttribute("Id", id),
                new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        }

        private static XElement Styles()
        {
            return XElement.Parse("<styleSheet xmlns=\"" + Main.NamespaceName + "\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
                "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>" +
                "<fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        }

        private static void WritePart(ZipArchive archive, string path, XElement root)
        {
            WriteEntry(archive, path, writer => root.WriteTo(writer));
        }

        private static void WriteEntry(ZipArchive archive, string path, Action<XmlWriter> write)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using (var stream = entry.Open())
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                NewLineHandling = NewLineHandling.Entitize
            }))
            {
                writer.WriteStartDocument(true);
                write(writer);
                writer.WriteEndDocument();
            }
        }

        private static void WriteSheet(XmlWriter writer, XlsxSheet sheet)
        {
            writer.WriteStartElement("worksheet", Main.NamespaceName);
            var hasHeader = sheet.Columns != null && sheet.Columns.Count > 0;
            if (hasHeader)
            {
                writer.WriteStartElement("sheetViews", Main.NamespaceName);
                writer.WriteStartElement("sheetView", Main.NamespaceName);
                writer.WriteAttributeString("workbookViewId", "0");
                writer.WriteStartElement("pane", Main.NamespaceName);
                writer.WriteAttributeString("ySplit", "1");
                writer.WriteAttributeString("topLeftCell", "A2");
                writer.WriteAttributeString("activePane", "bottomLeft");
                writer.WriteAttributeString("state", "frozen");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteStartElement("sheetData", Main.NamespaceName);
            var rowNumber = 1;
            if (hasHeader)
            {
                StartRow(writer, rowNumber);
                for (var i = 0; i < sheet.Columns.Count; i++)
                    WriteCell(writer, i + 1, rowNumber, sheet.Columns[i] ?? string.Empty, true);
                writer.WriteEndElement();
                rowNumber++;
            }
            if (sheet.Rows != null)
            {
                foreach (var row in sheet.Rows)
                {
                    StartRow(writer, rowNumber);
                    if (row != null)
                        for (var i = 0; i < row.Count; i++)
                            WriteCell(writer, i + 1, rowNumber, row[i], false);
                    writer.WriteEndElement();
                    rowNumber++;
                }
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static void StartRow(XmlWriter writer, int number)
        {
            writer.WriteStartElement("row", Main.NamespaceName);
            writer.WriteAttributeString("r", Invariant(number));
        }

        private static void WriteCell(XmlWriter writer, int column, int row, object value, bool header)
        {
            if (value == null) return;
            string text;
            var type = string.Empty;
            if (value is string)
            {
                text = EscapeLike.Replace(CleanXml((string)value), "_x005F_$1");
                // Excel refuses a cell longer than 32767 characters and offers to repair the file.
                if (text.Length > MaxCellText)
                    text = text.Substring(0, char.IsHighSurrogate(text[MaxCellText - 1]) ? MaxCellText - 1 : MaxCellText);
                type = "inlineStr";
            }
            else if (value is bool)
            {
                text = (bool)value ? "1" : "0";
                type = "b";
            }
            else if (value is double)
            {
                var number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number)) return;
                text = number.ToString("R", CultureInfo.InvariantCulture);
            }
            else if (value is float)
            {
                var number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number)) return;
                text = number.ToString("R", CultureInfo.InvariantCulture);
            }
            else if (value is int || value is long || value is short || value is byte || value is decimal)
                text = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            else
                throw new ArgumentException("Unsupported XLSX cell type: " + value.GetType().FullName, nameof(value));

            writer.WriteStartElement("c", Main.NamespaceName);
            writer.WriteAttributeString("r", ColumnName(column) + Invariant(row));
            if (header) writer.WriteAttributeString("s", "1");
            if (type.Length > 0) writer.WriteAttributeString("t", type);
            if (type == "inlineStr")
            {
                writer.WriteStartElement("is", Main.NamespaceName);
                writer.WriteStartElement("t", Main.NamespaceName);
                writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
                writer.WriteString(text);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            else
                writer.WriteElementString("v", Main.NamespaceName, text);
            writer.WriteEndElement();
        }

        private static string CleanXml(string value)
        {
            var clean = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (XmlConvert.IsXmlChar(character))
                    clean.Append(character);
                else if (char.IsHighSurrogate(character) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    clean.Append(character).Append(value[i + 1]);
                    i++;
                }
            }
            return clean.ToString();
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

        private static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
