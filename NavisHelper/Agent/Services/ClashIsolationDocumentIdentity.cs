using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashIsolationDocumentIdentity
    {
        private readonly Document _document;
        private readonly string _fileName;
        private readonly string _modelFingerprint;

        private ClashIsolationDocumentIdentity(
            Document document,
            string fileName,
            string modelFingerprint)
        {
            _document = document;
            _fileName = fileName;
            _modelFingerprint = modelFingerprint;
        }

        public static ClashIsolationDocumentIdentity Capture(Document document)
        {
            if (document == null)
                return null;

            return new ClashIsolationDocumentIdentity(
                document,
                NormalizePath(SafeRead(() => document.FileName)),
                BuildModelFingerprint(document));
        }

        public bool Matches(Document document)
        {
            return HasSameModelContent(document) &&
                   string.Equals(
                       _fileName,
                       NormalizePath(SafeRead(() => document.FileName)),
                       StringComparison.OrdinalIgnoreCase);
        }

        public bool HasSameModelContent(Document document)
        {
            var currentFingerprint = BuildModelFingerprint(document);
            return document != null &&
                   ReferenceEquals(_document, document) &&
                   !string.IsNullOrEmpty(_modelFingerprint) &&
                   !string.IsNullOrEmpty(currentFingerprint) &&
                   string.Equals(
                       _modelFingerprint,
                       currentFingerprint,
                       StringComparison.Ordinal);
        }

        private static string BuildModelFingerprint(Document document)
        {
            if (document == null || document.Models == null)
                return string.Empty;

            var identities = new List<string>();
            try
            {
                foreach (var model in document.Models)
                {
                    if (model == null)
                        continue;

                    var guid = SafeRead(() => model.Guid.ToString("D"));
                    var sourceFile = NormalizePath(SafeRead(() => model.SourceFileName));
                    var fileName = NormalizePath(SafeRead(() => model.FileName));
                    identities.Add(guid + "|" + sourceFile + "|" + fileName);
                }
            }
            catch
            {
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
