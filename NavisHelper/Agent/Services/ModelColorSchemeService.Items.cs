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
        private static ModelColorSchemeCollectionResult CollectItems(
            Document document,
            string scope,
            int maxItems,
            bool includeContainers,
            Stopwatch operationTimer,
            int workBudgetSeconds)
        {
            var result = new ModelColorSchemeCollectionResult();
            var stack = new Stack<ModelItem>();
            if (scope == "selection")
            {
                var selected = document.CurrentSelection == null
                    ? Enumerable.Empty<ModelItem>()
                    : document.CurrentSelection.SelectedItems;
                foreach (var item in selected.Reverse())
                    if (item != null)
                        stack.Push(item);
            }
            else
            {
                var roots = document.Models == null
                    ? new List<ModelItem>()
                    : document.Models
                        .Where(model => model != null && model.RootItem != null)
                        .Select(model => model.RootItem)
                        .ToList();
                for (var index = roots.Count - 1; index >= 0; index--)
                    stack.Push(roots[index]);
            }

            var visited = new HashSet<ModelItem>();
            while (stack.Count > 0 &&
                   result.TraversedItemCount < maxItems &&
                   operationTimer.Elapsed.TotalSeconds < workBudgetSeconds)
            {
                var item = stack.Pop();
                if (item == null || !visited.Add(item))
                    continue;

                result.TraversedItemCount++;
                var children = SafeChildren(item);
                var hasChildren = children.Count > 0;
                // The geometry probe is a native read per traversed item; containers the
                // eligibility filter discards would throw its result away, so test the filter first.
                if ((includeContainers || !hasChildren) && SafeBool(() => item.HasGeometry))
                    result.Items.Add(item);

                for (var index = children.Count - 1; index >= 0; index--)
                    stack.Push(children[index]);
            }

            result.WorkBudgetReached =
                stack.Count > 0 &&
                operationTimer.Elapsed.TotalSeconds >= workBudgetSeconds;
            result.Truncated = stack.Count > 0;
            return result;
        }

        private static int _ancestorPropertiesFailureLogged;

        private static ModelColorSchemeItemFacts BuildItemFacts(
            ModelItem item,
            out ModelColorSchemeItemPathHolder pathHolder,
            int maxPropertiesPerItem,
            List<string> categoryFilters,
            List<string> propertyFilters,
            bool includeAncestors = false,
            bool collectProperties = true,
            Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts> propertyCache = null,
            Dictionary<ModelItem, string> sourceFileCache = null,
            PhaseTimings phases = null)
        {
            // The item's own property pass also resolves its source file, so the ancestor walk
            // below can take that result from the cache instead of enumerating the tree twice.
            ModelColorSchemeCachedPropertyFacts ownProperties = null;
            if (item != null && collectProperties)
            {
                var ownStarted = Stopwatch.GetTimestamp();
                try
                {
                    ownProperties = ReadCachedProperties(
                        item,
                        maxPropertiesPerItem,
                        categoryFilters,
                        propertyFilters,
                        propertyCache,
                        findSourceFile: true);
                    if (sourceFileCache != null && ownProperties.SourceFile != null)
                        sourceFileCache[item] = ownProperties.SourceFile;
                }
                catch
                {
                    ownProperties = null;
                }
                if (phases != null)
                    phases.Add("own_properties", Stopwatch.GetTimestamp() - ownStarted);
            }

            var namePathStarted = Stopwatch.GetTimestamp();
            pathHolder = new ModelColorSchemeItemPathHolder(item);
            var facts = new ModelColorSchemeItemFacts
            {
                Name = SafeString(() => item.DisplayName),
                PathFactory = pathHolder.GetPath,
            };
            if (phases != null)
                phases.Add("name_path", Stopwatch.GetTimestamp() - namePathStarted);
            var sourceFileStarted = Stopwatch.GetTimestamp();
            facts.SourceFile = GetSourceFileFromProperties(item, sourceFileCache);
            if (string.IsNullOrWhiteSpace(facts.SourceFile))
                facts.SourceFile = GetSourceFile(item);
            if (phases != null)
                phases.Add("source_file", Stopwatch.GetTimestamp() - sourceFileStarted);
            if (item == null || !collectProperties)
                return facts;

            var propertyCount = 0;
            var ancestorsStarted = Stopwatch.GetTimestamp();
            try
            {
                try
                {
                    IEnumerable<ModelItem> propertyItems;
                    if (includeAncestors)
                        propertyItems = item.AncestorsAndSelf;
                    else
                        propertyItems = new[] { item };
                    var isSelf = true;
                    foreach (var propertyItem in propertyItems)
                    {
                        ModelColorSchemeCachedPropertyFacts cached;
                        if (isSelf && ownProperties != null)
                        {
                            // Reuse the pass made above instead of reading the item's tree again.
                            cached = ownProperties;
                        }
                        else
                        {
                            cached = ReadCachedProperties(
                                propertyItem,
                                maxPropertiesPerItem,
                                categoryFilters,
                                propertyFilters,
                                propertyCache,
                                findSourceFile: false);
                        }

                        isSelf = false;
                        foreach (var propertyFact in cached.Properties)
                        {
                            if (propertyCount >= maxPropertiesPerItem)
                            {
                                facts.PropertiesTruncated = true;
                                return facts;
                            }
                            facts.Properties.Add(propertyFact);
                            propertyCount++;
                        }
                        if (cached.Truncated)
                        {
                            facts.PropertiesTruncated = true;
                            return facts;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log only the first failure per process; later failures are not logged.
                    if (Interlocked.Exchange(ref _ancestorPropertiesFailureLogged, 1) == 0)
                        Logger.Warn("Could not read ancestor properties for model color scheme: " + ex.Message, "ModelColorScheme");
                }
            }
            finally
            {
                if (phases != null)
                    phases.Add("ancestor_properties", Stopwatch.GetTimestamp() - ancestorsStarted);
            }

            return facts;
        }

        private static int _cachedPropertiesFailureLogged;

        private static ModelColorSchemeCachedPropertyFacts ReadCachedProperties(
            ModelItem item,
            int maxProperties,
            List<string> categoryFilters,
            List<string> propertyFilters,
            Dictionary<ModelItem, ModelColorSchemeCachedPropertyFacts> cache,
            bool findSourceFile)
        {
            ModelColorSchemeCachedPropertyFacts cached;
            if (item != null && cache != null && cache.TryGetValue(item, out cached))
                return cached;

            cached = new ModelColorSchemeCachedPropertyFacts();
            try
            {
                if (item != null && item.PropertyCategories != null)
                {
                    foreach (var category in item.PropertyCategories)
                    {
                        if (category == null || category.Properties == null)
                            continue;

                        var categoryName = SafeString(() => category.DisplayName);
                        var categoryInternalName = SafeString(() => category.Name);
                        var categoryMatches = MatchesOptionalFilters(
                            categoryFilters,
                            categoryName,
                            categoryInternalName);
                        // With findSourceFile the source search ignores the filters, so a filtered-out
                        // category is still scanned for a source-file property until one is found.
                        if (!categoryMatches && (!findSourceFile || cached.SourceFile != null))
                            continue;

                        foreach (var property in category.Properties)
                        {
                            if (property == null)
                                continue;

                            var propertyName = SafeString(() => property.DisplayName);
                            var propertyInternalName = SafeString(() => property.Name);
                            var matchesFactFilters = categoryMatches &&
                                                     !cached.Truncated &&
                                                     MatchesOptionalFilters(
                                                         propertyFilters,
                                                         propertyName,
                                                         propertyInternalName);
                            var matchesSourceFile = findSourceFile &&
                                                    cached.SourceFile == null &&
                                                    IsSourceFileProperty(propertyName, propertyInternalName);
                            if (!matchesFactFilters && !matchesSourceFile)
                                continue;

                            // One value read serves both the fact and the source-file match.
                            var value = GetPropertyValue(property);
                            if (matchesSourceFile && !string.IsNullOrWhiteSpace(value))
                            {
                                cached.SourceFile = value;
                                if (cached.Truncated)
                                    break;
                            }

                            if (!matchesFactFilters)
                                continue;
                            if (string.IsNullOrWhiteSpace(value))
                                continue;
                            if (cached.Properties.Count >= maxProperties)
                            {
                                cached.Truncated = true;
                                if (!findSourceFile || cached.SourceFile != null)
                                    break;
                                continue;
                            }

                            cached.Properties.Add(new ModelColorSchemePropertyFact
                            {
                                Category = categoryName,
                                Property = propertyName,
                                Value = value,
                            });
                        }
                        // Past the facts cap the scan continues with names only until the
                        // source file is found; both done means the enumeration can stop.
                        if (cached.Truncated && (!findSourceFile || cached.SourceFile != null))
                            break;
                    }
                }

                if (findSourceFile && cached.SourceFile == null)
                    cached.SourceFile = string.Empty;
            }
            catch (Exception ex)
            {
                // Log only the first failure per process; later failures are not logged.
                if (Interlocked.Exchange(ref _cachedPropertiesFailureLogged, 1) == 0)
                    Logger.Warn("Could not read cached model item properties for model color scheme: " + ex.Message, "ModelColorScheme");
            }

            if (item != null && cache != null)
                cache[item] = cached;
            return cached;
        }

        private static bool MatchesOptionalFilters(
            List<string> filters,
            string displayName,
            string internalName)
        {
            if (filters == null || filters.Count == 0)
                return true;
            return filters.Any(filter =>
                (!string.IsNullOrWhiteSpace(displayName) &&
                 displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(internalName) &&
                 internalName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool HasPropertyMatchers(ModelColorSchemeRule rule)
        {
            return rule != null &&
                   (rule.CategoryContains.Count > 0 ||
                    rule.PropertyContains.Count > 0 ||
                    rule.PropertyValueContains.Count > 0);
        }

        private static List<ModelItem> SafeChildren(ModelItem item)
        {
            try
            {
                return item == null || item.Children == null
                    ? new List<ModelItem>()
                    : item.Children.Where(child => child != null).ToList();
            }
            catch
            {
                return new List<ModelItem>();
            }
        }

        private static string GetSourceFile(ModelItem item)
        {
            try
            {
                var model = item == null ? null : item.Model;
                if (model == null)
                    return string.Empty;
                return !string.IsNullOrWhiteSpace(model.SourceFileName)
                    ? model.SourceFileName
                    : model.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int _sourceFilePropertiesFailureLogged;

        private static string GetSourceFileFromProperties(
            ModelItem item,
            Dictionary<ModelItem, string> cache)
        {
            try
            {
                foreach (var current in item.AncestorsAndSelf)
                {
                    string cached;
                    if (cache != null && cache.TryGetValue(current, out cached))
                    {
                        if (!string.IsNullOrWhiteSpace(cached))
                            return cached;
                        continue;
                    }

                    var found = string.Empty;
                    foreach (var category in current.PropertyCategories)
                    {
                        if (category == null || category.Properties == null)
                            continue;
                        foreach (var property in category.Properties)
                        {
                            var name = SafeString(() => property.DisplayName);
                            var internalName = SafeString(() => property.Name);
                            if (!IsSourceFileProperty(name, internalName))
                                continue;
                            found = GetPropertyValue(property);
                            if (!string.IsNullOrWhiteSpace(found))
                                break;
                        }
                        if (!string.IsNullOrWhiteSpace(found))
                            break;
                    }
                    if (cache != null)
                        cache[current] = found;
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch (Exception ex)
            {
                // Log only the first failure per process; later failures are not logged.
                if (Interlocked.Exchange(ref _sourceFilePropertiesFailureLogged, 1) == 0)
                    Logger.Warn("Could not read source file from model item properties: " + ex.Message, "ModelColorScheme");
            }
            return string.Empty;
        }

        private static bool IsSourceFileProperty(string displayName, string internalName)
        {
            var values = new[] { displayName ?? string.Empty, internalName ?? string.Empty };
            return values.Any(value =>
                value.IndexOf("Файл источника", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Source File", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SourceFile", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildItemPath(ModelItem item)
        {
            try
            {
                return string.Join(
                    "/",
                    item.AncestorsAndSelf
                        .Reverse()
                        .Select(current => SafeString(() => current.DisplayName))
                        .Where(name => !string.IsNullOrWhiteSpace(name)));
            }
            catch
            {
                return SafeString(() => item.DisplayName);
            }
        }

        private static string GetPropertyValue(DataProperty property)
        {
            try
            {
                if (property == null)
                    return string.Empty;
                var value = property.Value;
                if (value == null)
                    return string.Empty;
                return value.ToDisplayString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeString(Func<string> read)
        {
            try { return read() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

    }
}
