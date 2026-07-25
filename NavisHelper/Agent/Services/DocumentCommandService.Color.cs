using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int DefaultSelectionColorGroupLimit = 1000;
        private const int MaxSelectionColorGroupLimit = 50000;

        private static readonly Tuple<int, int, int>[] ColorByPropertyPalette = new[]
        {
            Tuple.Create(0x1F, 0x77, 0xB4),
            Tuple.Create(0xFF, 0x7F, 0x0E),
            Tuple.Create(0x2C, 0xA0, 0x2C),
            Tuple.Create(0xD6, 0x27, 0x28),
            Tuple.Create(0x94, 0x67, 0xBD),
            Tuple.Create(0x8C, 0x56, 0x4B),
            Tuple.Create(0xE3, 0x77, 0xC2),
            Tuple.Create(0x7F, 0x7F, 0x7F),
            Tuple.Create(0xBC, 0xBD, 0x22),
            Tuple.Create(0x17, 0xBE, 0xCF),
            Tuple.Create(0x39, 0x3B, 0x79),
            Tuple.Create(0x63, 0x79, 0x39),
            Tuple.Create(0x8C, 0x6D, 0x31),
            Tuple.Create(0x84, 0x3C, 0x39),
            Tuple.Create(0x7B, 0x41, 0x73),
            Tuple.Create(0x31, 0x82, 0xBD),
            Tuple.Create(0xE6, 0x55, 0x0D),
            Tuple.Create(0x31, 0xA3, 0x54),
            Tuple.Create(0x75, 0x6B, 0xB1),
            Tuple.Create(0xDE, 0x2D, 0x26),
            Tuple.Create(0x6B, 0x6E, 0xC7),
            Tuple.Create(0x9E, 0x9A, 0x42),
            Tuple.Create(0x52, 0x5C, 0xA3),
            Tuple.Create(0xAD, 0x49, 0x4A),
        };

        public SelectionColorByPropertyResponse SelectionColorByProperty(Document document, SelectionColorByPropertyRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new SelectionColorByPropertyRequest();
            var apply = request.Apply == true;
            var itemLimit = ClampSelectionPropertyItemLimit(request.ItemLimit);
            var groupLimit = ClampSelectionColorGroupLimit(request.GroupLimit);
            var includeEmptyValues = request.IncludeEmptyValues.GetValueOrDefault(false);
            var categoryFilters = NormalizeReportFilters(request.CategoryFilters);
            var propertyFilters = NormalizeReportFilters(request.PropertyFilters);
            if (categoryFilters.Count == 0 && propertyFilters.Count == 0)
                throw new ArgumentException("At least one category or property filter is required.", nameof(request));

            var transparency = NormalizeTransparency(request.Transparency);
            var selectedItems = document.CurrentSelection == null
                ? new List<ModelItem>()
                : document.CurrentSelection.SelectedItems.ToList();
            var buckets = new Dictionary<string, ColorByPropertyBucket>(StringComparer.OrdinalIgnoreCase);
            var response = new SelectionColorByPropertyResponse
            {
                Applied = apply,
                SelectedItemCount = selectedItems.Count,
                ScannedItemCount = Math.Min(selectedItems.Count, itemLimit),
                ItemsTruncated = selectedItems.Count > itemLimit,
            };

            foreach (var item in selectedItems.Take(itemLimit))
            {
                if (item == null)
                    continue;

                string value;
                string categoryName;
                string propertyName;
                if (!TryGetFirstMatchingPropertyValue(item, categoryFilters, propertyFilters, includeEmptyValues, out value, out categoryName, out propertyName))
                    continue;

                ColorByPropertyBucket bucket;
                if (!buckets.TryGetValue(value, out bucket))
                {
                    bucket = new ColorByPropertyBucket
                    {
                        Value = value,
                        Category = categoryName,
                        Property = propertyName,
                        SampleItemPath = BuildItemPath(item),
                        SampleItemName = SafeString(() => item.DisplayName),
                    };
                    buckets[value] = bucket;
                }

                bucket.Items.Add(item);
                response.MatchedItemCount++;
            }

            var orderedBuckets = buckets.Values
                .OrderBy(bucket => bucket.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            response.DistinctValueCount = orderedBuckets.Count;
            response.GroupsTruncated = orderedBuckets.Count > groupLimit;
            if (apply && response.GroupsTruncated)
                throw new InvalidOperationException("Distinct group count exceeds group_limit. Increase group_limit to apply all groups.");

            var colorIndex = 0;
            foreach (var bucket in orderedBuckets)
            {
                var color = GetColorByPropertyColor(colorIndex, bucket.Value);
                bucket.R = color.Item1;
                bucket.G = color.Item2;
                bucket.B = color.Item3;
                colorIndex++;
            }

            if (apply)
            {
                foreach (var bucket in orderedBuckets)
                {
                    var color = Autodesk.Navisworks.Api.Color.FromByteRGB((byte)bucket.R, (byte)bucket.G, (byte)bucket.B);
                    document.Models.OverridePermanentColor(bucket.Items, color);
                    if (transparency.HasValue)
                        document.Models.OverridePermanentTransparency(bucket.Items, transparency.Value);
                    response.ColoredItemCount += bucket.Items.Count;
                }
            }

            response.Groups = orderedBuckets
                .Take(groupLimit)
                .Select(bucket => new SelectionColorByPropertyGroup
                {
                    Value = bucket.Value,
                    Count = bucket.Items.Count,
                    R = bucket.R,
                    G = bucket.G,
                    B = bucket.B,
                    ColorHex = "#" + bucket.R.ToString("X2") + bucket.G.ToString("X2") + bucket.B.ToString("X2"),
                    Category = bucket.Category,
                    Property = bucket.Property,
                    SampleItemPath = bucket.SampleItemPath,
                    SampleItemName = bucket.SampleItemName,
                })
                .ToList();
            response.ReturnedGroupCount = response.Groups.Count;
            response.Message = apply ? "Color overrides applied." : "Dry-run only. Pass apply=true to apply color overrides.";
            return response;
        }

        private static int ClampSelectionColorGroupLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultSelectionColorGroupLimit);
            if (value < 1)
                return 1;
            if (value > MaxSelectionColorGroupLimit)
                return MaxSelectionColorGroupLimit;
            return value;
        }

        private static float? NormalizeTransparency(float? transparency)
        {
            if (!transparency.HasValue)
                return null;

            var value = transparency.Value;
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static bool TryGetFirstMatchingPropertyValue(
            ModelItem item,
            List<string> categoryFilters,
            List<string> propertyFilters,
            bool includeEmptyValues,
            out string value,
            out string categoryDisplayName,
            out string propertyDisplayName)
        {
            value = string.Empty;
            categoryDisplayName = string.Empty;
            propertyDisplayName = string.Empty;

            if (item == null || item.PropertyCategories == null)
                return false;

            foreach (var category in item.PropertyCategories)
            {
                if (category == null || category.Properties == null)
                    continue;

                var currentCategoryDisplayName = SafeString(() => category.DisplayName);
                var currentCategoryName = SafeString(() => category.Name);
                if (!MatchesReportFilters(categoryFilters, currentCategoryDisplayName, currentCategoryName))
                    continue;

                foreach (var property in category.Properties)
                {
                    if (property == null)
                        continue;

                    var currentPropertyDisplayName = SafeString(() => property.DisplayName);
                    var currentPropertyName = SafeString(() => property.Name);
                    if (!MatchesReportFilters(propertyFilters, currentPropertyDisplayName, currentPropertyName))
                        continue;

                    var currentValue = GetSelectionPropertyReportValue(property);
                    if (!includeEmptyValues && string.IsNullOrWhiteSpace(currentValue))
                        continue;

                    value = currentValue;
                    categoryDisplayName = currentCategoryDisplayName;
                    propertyDisplayName = currentPropertyDisplayName;
                    return true;
                }
            }

            return false;
        }

        private static Tuple<int, int, int> GetColorByPropertyColor(int index, string value)
        {
            if (index >= 0 && index < ColorByPropertyPalette.Length)
                return ColorByPropertyPalette[index];

            unchecked
            {
                uint hash = 2166136261;
                var text = value ?? string.Empty;
                for (var i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }

                var r = 64 + (int)(hash & 0x7F);
                var g = 64 + (int)((hash >> 8) & 0x7F);
                var b = 64 + (int)((hash >> 16) & 0x7F);
                return Tuple.Create(r, g, b);
            }
        }

        private sealed class ColorByPropertyBucket
        {
            public string Value;
            public string Category;
            public string Property;
            public string SampleItemPath;
            public string SampleItemName;
            public int R;
            public int G;
            public int B;
            public ModelItemCollection Items = new ModelItemCollection();
        }
    }
}
