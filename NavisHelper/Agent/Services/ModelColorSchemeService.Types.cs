using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class ModelColorSchemeService
    {
        private sealed class ModelColorSchemePreparedRule
        {
            public ModelColorSchemeRule Rule;
            public string Name;
            public string ColorHex;
            public Autodesk.Navisworks.Api.Color Color;
        }

        private sealed class ModelColorSchemeRuleBucket
        {
            public ModelColorSchemePreparedRule Rule;
            public ModelItemCollection Items = new ModelItemCollection();
            public string SampleItemName;
            public ModelColorSchemeItemPathHolder SampleItemPathHolder;
            public string SampleSourceFile;
        }

        // The path is built from the ModelItem only when it is emitted; the holder never
        // references the facts object, so an unused path keeps no property list alive.
        private sealed class ModelColorSchemeItemPathHolder
        {
            private readonly ModelItem _item;
            private string _path;
            private bool _pathBuilt;

            public ModelColorSchemeItemPathHolder(ModelItem item)
            {
                _item = item;
            }

            public string GetPath()
            {
                if (!_pathBuilt)
                {
                    _path = BuildItemPath(_item);
                    _pathBuilt = true;
                }
                return _path;
            }
        }

        private sealed class ModelColorSchemeCollectionResult
        {
            public int TraversedItemCount;
            public bool Truncated;
            public bool WorkBudgetReached;
            public List<ModelItem> Items = new List<ModelItem>();
        }

        private sealed class ModelColorSchemeOriginalState
        {
            public ModelItem Item;
            public Autodesk.Navisworks.Api.Color Color;
            public double Transparency;
            public bool HadMaterialOverride;
        }

        private sealed class ModelColorSchemeCachedPropertyFacts
        {
            public bool Truncated;
            // Null until a findSourceFile pass has searched this item; string.Empty means searched and not found.
            public string SourceFile;
            public List<ModelColorSchemePropertyFact> Properties =
                new List<ModelColorSchemePropertyFact>();
        }

        private sealed class ModelColorSchemeCandidateAccumulator
        {
            public string Kind;
            public string Category;
            public string Property;
            public string Value;
            public int Count;
            public string SampleItemName;
            public ModelColorSchemeItemPathHolder SampleItemPathHolder;
            public string SampleSourceFile;

            public ModelColorSchemeCandidate ToResponse(string verbosity)
            {
                var compact = string.Equals(
                    verbosity,
                    "compact",
                    StringComparison.OrdinalIgnoreCase);
                return new ModelColorSchemeCandidate
                {
                    Kind = Kind,
                    Category = compact ? TruncateText(Category, 200) : Category,
                    Property = compact ? TruncateText(Property, 200) : Property,
                    Value = compact ? TruncateText(Value, 500) : Value,
                    Count = Count,
                    SampleItemName = compact ? TruncateText(SampleItemName, 200) : SampleItemName,
                    SampleItemPath = compact
                        ? string.Empty
                        : SampleItemPathHolder == null
                            ? string.Empty
                            : SampleItemPathHolder.GetPath() ?? string.Empty,
                    SampleSourceFile = compact ? TruncateText(SampleSourceFile, 300) : SampleSourceFile,
                };
            }

            private static string TruncateText(string value, int maximumLength)
            {
                value = value ?? string.Empty;
                return value.Length <= maximumLength
                    ? value
                    : value.Substring(0, maximumLength) + "…";
            }
        }
    }

    internal sealed class ModelColorSchemeDocumentIdentity
    {
        private readonly Document _document;
        private readonly string _fileName;
        private readonly HashSet<string> _modelIdentities;

        private ModelColorSchemeDocumentIdentity(
            Document document,
            string fileName,
            HashSet<string> modelIdentities)
        {
            _document = document;
            _fileName = fileName;
            _modelIdentities = modelIdentities;
        }

        public static ModelColorSchemeDocumentIdentity Capture(Document document)
        {
            return document == null
                ? null
                : new ModelColorSchemeDocumentIdentity(
                    document,
                    NormalizePath(SafeRead(() => document.FileName)),
                    ReadModelIdentities(document));
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
            var currentIdentities = ReadModelIdentities(document);
            return document != null &&
                   ReferenceEquals(_document, document) &&
                   _modelIdentities != null &&
                   _modelIdentities.Count > 0 &&
                   currentIdentities != null &&
                   _modelIdentities.IsSubsetOf(currentIdentities);
        }

        private static HashSet<string> ReadModelIdentities(Document document)
        {
            if (document == null || document.Models == null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return identities;
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try { return Path.GetFullPath(value.Trim()); }
            catch { return value.Trim(); }
        }

        private static string SafeRead(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
