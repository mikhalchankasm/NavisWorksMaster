using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        private const int MaxQueries = 1;
        private const int MaxRootNameQueries = 1000;
        private const int DefaultItemChildrenLimit = 200;
        private const int MaxItemChildrenLimit = 2000;
        private const int DefaultPreviewLimit = 10;
        private const int MaxPreviewLimit = 20;
        private const int MaxManualTraversalMilliseconds = 10000;
        private const int MaxDisplayNameTraversalMilliseconds = 10000;
        private const int MaxBroadExistenceMatches = 1000;
        private const int MaxInheritedBroadPositiveExpandedMatches = 50000;
        private const int MaxNativeAndFastPathVariants = 16;
        private const int MaxNativeAndFastPathIntermediateMatches = 50000;
        private const int MinBroadPositiveLiteralLength = 3;
        private const int RequestTimeoutSafetyMarginMilliseconds = 8000;
        private const string DefaultCategory = "Item";
        private const string DefaultProperty = "Name";
        private const string ItemInternalCategory = "LcOaNode";
        private const string ItemUserNameInternalProperty = "LcOaSceneBaseUserName";
        private const string SourceFileInternalProperty = "LcOaNodeSourceFile";
        private readonly object _rootSearchIndexLock = new object();
        private RootSearchIndex _rootSearchIndex;

        private static readonly (string Category, string Name)[] DisplayNameProperties =
        {
            ("Item", "Name"),
            ("", "Name"),
            ("", "Имя"),
            ("Элемент", "Имя"),
        };



        private static readonly (string Category, string Property)[] SourceFileDisplayProperties =
        {
            ("Item", "Source File"),
            ("Элемент", "Файл источника"),
            ("", "Source File"),
            ("", "Файл источника"),
        };

        private static readonly KnownPropertyDefinition[] KnownProperties =
        {
            new KnownPropertyDefinition(
                ItemInternalCategory,
                SourceFileInternalProperty,
                true,
                new[] { "Item", "Элемент" },
                new[] { "Source File", "Файл источника" }),
            new KnownPropertyDefinition(
                ItemInternalCategory,
                "LcOaNodeFileName",
                true,
                new[] { "Item", "Элемент" },
                new[] { "File Name", "Имя файла" }),
            new KnownPropertyDefinition(
                ItemInternalCategory,
                "LcOaNodeFilePath",
                true,
                new[] { "Item", "Элемент" },
                new[] { "File Path", "Путь к файлу" }),
            new KnownPropertyDefinition(
                ItemInternalCategory,
                "LcOaNodeCreationDate",
                true,
                new[] { "Item", "Элемент" },
                new[] { "Creation Date", "Дата создания" }),
            new KnownPropertyDefinition(
                ItemInternalCategory,
                "LcOaSceneBaseClassUserName",
                true,
                new[] { "Item", "Элемент" },
                new[] { "Type", "Тип" }),
            new KnownPropertyDefinition(
                ItemInternalCategory,
                "LcOaSceneBaseClassName",
                false,
                new[] { "Item", "Элемент" },
                new[] { "Class", "Класс" }),
        };

        private sealed class KnownPropertyDefinition
        {
            public KnownPropertyDefinition(
                string internalCategory,
                string internalProperty,
                bool inheritFromAncestor,
                string[] categoryAliases,
                string[] propertyAliases)
            {
                InternalCategory = internalCategory;
                InternalProperty = internalProperty;
                InheritFromAncestor = inheritFromAncestor;
                CategoryAliases = categoryAliases ?? Array.Empty<string>();
                PropertyAliases = propertyAliases ?? Array.Empty<string>();
            }

            public string InternalCategory { get; private set; }

            public string InternalProperty { get; private set; }

            public bool InheritFromAncestor { get; private set; }

            public string[] CategoryAliases { get; private set; }

            public string[] PropertyAliases { get; private set; }

            public bool MatchesDisplayAlias(string category, string property)
            {
                if (!MatchesAlias(property, PropertyAliases))
                    return false;

                return string.IsNullOrEmpty(category) || MatchesAlias(category, CategoryAliases);
            }

            public bool MatchesInternalAlias(string categoryInternal, string propertyInternal)
            {
                if (string.IsNullOrEmpty(propertyInternal))
                    return false;

                if (!string.Equals(propertyInternal, NormalizeComparableText(InternalProperty), StringComparison.Ordinal))
                    return false;

                return string.IsNullOrEmpty(categoryInternal) ||
                       string.Equals(categoryInternal, NormalizeComparableText(InternalCategory), StringComparison.Ordinal);
            }

            private static bool MatchesAlias(string value, IEnumerable<string> aliases)
            {
                return aliases.Any(alias =>
                    string.Equals(value, NormalizeComparableText(alias), StringComparison.Ordinal));
            }
        }

        private sealed class ResolvedProperty
        {
            public bool IsDefaultItemNameTarget { get; set; }

            public bool InheritFromAncestor { get; set; }

            public string InternalCategory { get; set; }

            public string InternalProperty { get; set; }

            public List<(string Category, string Property)> DisplayCandidates { get; } =
                new List<(string Category, string Property)>();
        }

        private sealed class TooAmbiguousSearchException : Exception
        {
            public TooAmbiguousSearchException(string message)
                : base(message)
            {
            }
        }



















































































































































































































































        private sealed class RootSearchCandidate
        {
            public RootSearchCandidate(
                ModelItem item,
                string displayName,
                string sourceFile,
                string fileName,
                string path,
                List<string> aliases)
            {
                Item = item;
                DisplayName = displayName ?? string.Empty;
                SourceFile = sourceFile ?? string.Empty;
                FileName = fileName ?? string.Empty;
                Path = path ?? string.Empty;
                Aliases = aliases ?? new List<string>();
            }

            public ModelItem Item { get; private set; }

            public string DisplayName { get; private set; }

            public string SourceFile { get; private set; }

            public string FileName { get; private set; }

            public string Path { get; private set; }

            public List<string> Aliases { get; private set; }
        }

        private sealed class RootSearchIndex
        {
            public RootSearchIndex(string cacheKey, int modelCount, List<RootSearchCandidate> candidates)
            {
                CacheKey = cacheKey ?? string.Empty;
                ModelCount = modelCount;
                Candidates = candidates ?? new List<RootSearchCandidate>();
            }

            public string CacheKey { get; private set; }

            public int ModelCount { get; private set; }

            public List<RootSearchCandidate> Candidates { get; private set; }
        }


























    }
}
