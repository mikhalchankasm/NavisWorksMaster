using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;

namespace NavisHelper.AI
{
    internal sealed class AIColorDocumentIdentity
    {
        private readonly Document _document;
        private readonly string _fileName;
        private readonly string _modelFingerprint;

        private AIColorDocumentIdentity(
            Document document,
            string fileName,
            string modelFingerprint)
        {
            _document = document;
            _fileName = fileName;
            _modelFingerprint = modelFingerprint;
        }

        internal static AIColorDocumentIdentity Capture(Document document)
        {
            if (document == null)
                return null;
            return new AIColorDocumentIdentity(
                document,
                NormalizePath(SafeRead(() => document.FileName)),
                BuildModelFingerprint(document));
        }

        // Must be called on the Navisworks UI thread.
        internal bool Matches(Document document)
        {
            if (document == null ||
                !ReferenceEquals(_document, document))
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
                        NormalizePath(SafeRead(() => model.SourceFileName)) +
                        "|" +
                        NormalizePath(SafeRead(() => model.FileName)));
                }
            }
            catch
            {
                return string.Empty;
            }

            identities.Sort(StringComparer.OrdinalIgnoreCase);
            return identities.Count + ":" + string.Join(
                "\n",
                identities);
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
                return string.Empty;
            }
        }
    }
}
