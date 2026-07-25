using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ResolvedSelectionSetReference
    {
        public SelectionSet Item;
        public ModelItemCollection ExplicitItems;
        public string ItemId;
        public string Name;
        public string Path;
        public string Type;
    }

    internal static class SelectionSetReferenceResolver
    {
        public static ResolvedSelectionSetReference Resolve(Document document, SelectionSetReference reference)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (reference == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selection set reference is required.");

            if (!string.IsNullOrWhiteSpace(reference.RootName) || !string.IsNullOrWhiteSpace(reference.SourceFile))
                return ResolveModelRoot(document, reference);

            var nodes = Build(document.SelectionSets.RootItem);
            List<ResolvedSelectionSetReference> matches;
            if (!string.IsNullOrWhiteSpace(reference.ItemId))
            {
                matches = nodes.Where(node => string.Equals(node.ItemId, reference.ItemId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(reference.Path))
            {
                var path = NormalizePath(reference.Path);
                matches = nodes.Where(node => string.Equals(NormalizePath(node.Path), path, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(reference.Name))
            {
                matches = nodes.Where(node => string.Equals(node.Name, reference.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selection set reference requires itemId, path, or name.");
            }

            if (reference.Occurrence.HasValue)
            {
                var index = reference.Occurrence.Value - 1;
                if (index < 0 || index >= matches.Count)
                    throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Selection set occurrence is outside the matched range.");
                return matches[index];
            }
            if (matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Selection set was not found.");
            if (matches.Count > 1)
                throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one selection set matched. Pass itemId from list_selection_sets or occurrence.");
            return matches[0];
        }

        private static ResolvedSelectionSetReference ResolveModelRoot(Document document, SelectionSetReference reference)
        {
            var matches = document.Models.Cast<Model>().Where(candidate =>
                (string.IsNullOrWhiteSpace(reference.RootName) || string.Equals(candidate.RootItem == null ? string.Empty : candidate.RootItem.DisplayName, reference.RootName.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(reference.SourceFile) ||
                 string.Equals(candidate.SourceFileName, reference.SourceFile.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.FileName, reference.SourceFile.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(System.IO.Path.GetFileName(candidate.SourceFileName), System.IO.Path.GetFileName(reference.SourceFile.Trim()), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Model root/source file was not found.");
            if (reference.Occurrence.HasValue)
            {
                var index = reference.Occurrence.Value - 1;
                if (index < 0 || index >= matches.Count)
                    throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "Model root occurrence is outside the matched range.");
                matches = new List<Model> { matches[index] };
            }
            if (matches.Count > 1)
                throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one model root matched; pass occurrence or a more specific sourceFile path.");
            var model = matches[0];
            var items = new ModelItemCollection();
            items.Add(model.RootItem);
            var source = !string.IsNullOrWhiteSpace(model.SourceFileName) ? model.SourceFileName : model.FileName;
            return new ResolvedSelectionSetReference
            {
                ExplicitItems = items,
                ItemId = BuildModelItemId(model, source),
                Name = model.RootItem == null ? PortableFileName(source) : model.RootItem.DisplayName,
                Path = source,
                Type = "ModelRoot",
            };
        }

        private static string BuildModelItemId(Model model, string source)
        {
            if (model.Guid != Guid.Empty)
                return "model:guid:" + model.Guid.ToString("D");
            if (model.SourceGuid != Guid.Empty)
                return "model:source-guid:" + model.SourceGuid.ToString("D");
            return "model:" + (source ?? string.Empty) + ":" + model.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static string PortableFileName(string value)
        {
            var text = value ?? string.Empty;
            var index = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
            return index >= 0 && index + 1 < text.Length ? text.Substring(index + 1) : text;
        }

        private static List<ResolvedSelectionSetReference> Build(GroupItem root)
        {
            var result = new List<ResolvedSelectionSetReference>();
            if (root == null)
                return result;
            var index = 0;
            foreach (SavedItem child in root.Children)
            {
                Collect(child, string.Empty, "ss:" + index.ToString("D4", CultureInfo.InvariantCulture), result);
                index++;
            }
            return result;
        }

        private static void Collect(SavedItem item, string parentPath, string itemId, ICollection<ResolvedSelectionSetReference> result)
        {
            if (item == null)
                return;
            var name = item.DisplayName ?? string.Empty;
            var path = string.IsNullOrWhiteSpace(parentPath) ? name : parentPath + "/" + name;
            var set = item as SelectionSet;
            if (set != null)
            {
                result.Add(new ResolvedSelectionSetReference
                {
                    Item = set,
                    ItemId = itemId,
                    Name = name,
                    Path = path,
                    Type = set.HasSearch ? "SearchSet" : "SelectionSet",
                });
            }

            var group = item as GroupItem;
            if (group == null)
                return;
            var index = 0;
            foreach (SavedItem child in group.Children)
            {
                Collect(child, path, itemId + "." + index.ToString("D4", CultureInfo.InvariantCulture), result);
                index++;
            }
        }

        private static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        }
    }
}
