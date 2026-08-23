using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;

namespace NavisHelper.WPF
{
    internal sealed class UiThreadModelScanDocumentIdentity
    {
        private readonly Document _document;
        private readonly string _fileName;
        private readonly string _modelFingerprint;

        private UiThreadModelScanDocumentIdentity(
            Document document,
            string fileName,
            string modelFingerprint)
        {
            _document = document;
            _fileName = fileName;
            _modelFingerprint = modelFingerprint;
        }

        internal static UiThreadModelScanDocumentIdentity Capture(
            Document document)
        {
            if (document == null)
                return null;
            return new UiThreadModelScanDocumentIdentity(
                document,
                NormalizePath(SafeRead(() => document.FileName)),
                BuildModelFingerprint(document));
        }

        internal bool Matches(Document document)
        {
            if (document == null || !ReferenceEquals(_document, document))
                return false;
            var currentFingerprint = BuildModelFingerprint(document);
            return !string.IsNullOrEmpty(_modelFingerprint) &&
                   !string.IsNullOrEmpty(currentFingerprint) &&
                   string.Equals(
                       _modelFingerprint,
                       currentFingerprint,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       _fileName,
                       NormalizePath(SafeRead(() => document.FileName)),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildModelFingerprint(Document document)
        {
            if (document?.Models == null)
                return string.Empty;
            var identities = new List<string>();
            try
            {
                foreach (var model in document.Models)
                {
                    if (model == null)
                        continue;
                    identities.Add(
                        SafeRead(() => model.Guid.ToString("D")) + "|" +
                        NormalizePath(SafeRead(() => model.SourceFileName)) + "|" +
                        NormalizePath(SafeRead(() => model.FileName)));
                }
            }
            catch
            {
                // Identity capture fails closed; the scan will not commit.
                return string.Empty;
            }

            identities.Sort(StringComparer.OrdinalIgnoreCase);
            return identities.Count + ":" + string.Join("\n", identities);
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try
            {
                return Path.GetFullPath(value.Trim());
            }
            catch
            {
                // Preserve the stable raw value when path normalization is unavailable.
                return value.Trim();
            }
        }

        private static string SafeRead(Func<string> read)
        {
            try
            {
                return read() ?? string.Empty;
            }
            catch
            {
                // An unreadable identity field remains empty and weakens the match safely.
                return string.Empty;
            }
        }
    }
}
