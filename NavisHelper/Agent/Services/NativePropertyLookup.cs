using Autodesk.Navisworks.Api;

namespace NavisHelper.Agent.Services
{
    internal static class NativePropertyLookup
    {
        internal const string ItemInternalCategory = "LcOaNode";
        internal const string SourceFileInternalProperty = "LcOaNodeSourceFile";

        private static readonly (string Category, string Property)[] SourceFileDisplayProperties =
        {
            ("Item", "Source File"),
            ("Элемент", "Файл источника"),
            ("", "Source File"),
            ("", "Файл источника"),
        };

        internal static DataProperty FindInternalProperty(ModelItem item, string category, string property)
        {
            if (item == null || item.PropertyCategories == null || string.IsNullOrWhiteSpace(property))
                return null;

            if (string.IsNullOrWhiteSpace(category))
            {
                foreach (PropertyCategory propertyCategory in item.PropertyCategories)
                {
                    if (propertyCategory == null || propertyCategory.Properties == null)
                        continue;

                    var propertyInAnyCategory = propertyCategory.Properties.FindPropertyByName(property);
                    if (propertyInAnyCategory != null)
                        return propertyInAnyCategory;
                }

                return null;
            }

            return item.PropertyCategories.FindPropertyByName(category, property);
        }

        internal static DataProperty FindDisplayProperty(ModelItem item, string category, string property)
        {
            if (item == null || item.PropertyCategories == null || string.IsNullOrWhiteSpace(property))
                return null;

            if (string.IsNullOrWhiteSpace(category))
            {
                foreach (PropertyCategory propertyCategory in item.PropertyCategories)
                {
                    if (propertyCategory == null || propertyCategory.Properties == null)
                        continue;

                    var propertyInAnyCategory = propertyCategory.Properties.FindPropertyByDisplayName(property);
                    if (propertyInAnyCategory != null)
                        return propertyInAnyCategory;
                }

                return null;
            }

            return item.PropertyCategories.FindPropertyByDisplayName(category, property);
        }

        internal static DataProperty FindSourceFileProperty(ModelItem item)
        {
            var property = FindInternalProperty(item, ItemInternalCategory, SourceFileInternalProperty);
            if (property != null)
                return property;

            foreach (var alias in SourceFileDisplayProperties)
            {
                property = FindDisplayProperty(item, alias.Category, alias.Property);
                if (property != null)
                    return property;
            }

            return null;
        }
    }
}
