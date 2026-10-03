using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace NavisHelper.Core.Import
{
    internal static class CsvAttributeReader
    {
        internal static CsvAttributeTable Read(string path)
        {
            return Parse(File.ReadAllBytes(path));
        }

        // Decode and validate the complete file before the caller can mutate a model.
        internal static CsvAttributeTable Parse(byte[] bytes)
        {
            string text;
            try
            {
                int offset = 0;
                Encoding encoding = new UTF8Encoding(false, true);
                if (StartsWith(bytes, 0xFF, 0xFE, 0, 0))
                { encoding = new UTF32Encoding(false, false, true); offset = 4; }
                else if (StartsWith(bytes, 0, 0, 0xFE, 0xFF))
                { encoding = new UTF32Encoding(true, false, true); offset = 4; }
                else if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
                { offset = 3; }
                else if (StartsWith(bytes, 0xFF, 0xFE))
                { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
                else if (StartsWith(bytes, 0xFE, 0xFF))
                { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
                text = encoding.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                throw new CsvAttributeReadException("CsvAttributeEncodingInvalid");
            }
            // BOM-less UTF-16 can look like valid UTF-8 interspersed with NULs.
            if (text.IndexOf('\0') >= 0)
                throw new CsvAttributeReadException("CsvAttributeEncodingInvalid");

            using (var parser = new TextFieldParser(new StringReader(text)))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(";");
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;
                try
                {
                    if (parser.EndOfData)
                        throw new CsvAttributeReadException("CsvAttributeEmpty");
                    var headers = Trim(parser.ReadFields());
                    if (headers.Length < 2)
                        throw new CsvAttributeReadException("CsvAttributeColumnsRequired");
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < headers.Length; i++)
                        if (headers[i].Length == 0 || !names.Add(headers[i]))
                            throw new CsvAttributeReadException("CsvAttributeHeaderInvalid", i + 1);

                    var rows = new List<string[]>();
                    while (!parser.EndOfData)
                    {
                        var values = Trim(parser.ReadFields());
                        int record = rows.Count + 2;
                        if (values.Length != headers.Length)
                            throw new CsvAttributeReadException("CsvAttributeRowWidth", record, values.Length, headers.Length);
                        if (values[0].Length == 0)
                            throw new CsvAttributeReadException("CsvAttributeItemNameRequired", record);
                        rows.Add(values);
                    }
                    if (rows.Count == 0)
                        throw new CsvAttributeReadException("CsvAttributeEmpty");
                    return new CsvAttributeTable(headers, rows);
                }
                catch (MalformedLineException ex)
                {
                    throw new CsvAttributeReadException("CsvAttributeMalformedLine", ex.LineNumber);
                }
            }
        }

        private static string[] Trim(string[] fields)
        {
            return fields.Select(value => value.Trim()).ToArray();
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }
    }

    internal sealed class CsvAttributeTable
    {
        internal CsvAttributeTable(string[] headers, List<string[]> rows)
        {
            Headers = headers;
            Rows = rows;
        }
        internal string[] Headers { get; }
        internal IReadOnlyList<string[]> Rows { get; }
    }

    internal sealed class CsvAttributeReadException : Exception
    {
        internal CsvAttributeReadException(string resourceKey, params object[] arguments)
            : base(resourceKey)
        {
            ResourceKey = resourceKey;
            Arguments = arguments;
        }
        internal string ResourceKey { get; }
        internal object[] Arguments { get; }
    }
}
