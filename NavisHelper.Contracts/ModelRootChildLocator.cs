using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ModelRootCandidate
    {
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        // Navisworks also accepts Model.FileName as an exact source selector.
        public string FileName { get; set; }
        public List<string> ChildNames { get; set; } = new List<string>();
    }

    public enum ModelRootChildMatchKind { Child, Root, NotFound, Ambiguous }

    public sealed class ModelRootChildMatch
    {
        public ModelRootChildMatchKind Kind { get; set; }
        public int ModelIndex { get; set; } = -1;
        public int ChildIndex { get; set; } = -1;
    }

    public static class ModelRootChildLocator
    {
        public static bool IsDocumentRoot(string rootName, string documentFileName)
        {
            return !string.IsNullOrWhiteSpace(rootName) && !string.IsNullOrWhiteSpace(documentFileName) &&
                   SameName(rootName, Path.GetFileName(documentFileName.Trim()));
        }

        public static ModelRootChildMatch Locate(IList<ModelRootCandidate> models, string rootName, string sourceFile, string childName)
        {
            if (models == null)
                throw new ArgumentNullException(nameof(models));
            if (string.IsNullOrWhiteSpace(childName))
                return new ModelRootChildMatch { Kind = ModelRootChildMatchKind.NotFound };
            var strict = Find(models, rootName, sourceFile, childName, false);
            if (strict.Kind != ModelRootChildMatchKind.NotFound)
                return strict;
            // Search other models only when the recorded one is not in this document (another revision, or
            // an NWF where the file is a model root) or none was recorded; a recorded model without the
            // child is a miss, not a reason to take a same-named child from a different model.
            var recorded = !string.IsNullOrWhiteSpace(rootName) || !string.IsNullOrWhiteSpace(sourceFile);
            if (recorded && models.Any(model => model != null && MatchesRoot(model, rootName, sourceFile)))
                return strict;
            return Find(models, rootName, sourceFile, childName, true);
        }

        private static ModelRootChildMatch Find(IList<ModelRootCandidate> models, string rootName, string sourceFile, string childName, bool fallback)
        {
            ModelRootChildMatch result = null;
            for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                var model = models[modelIndex];
                if (model == null || (!fallback && !MatchesRoot(model, rootName, sourceFile)))
                    continue;
                var children = model.ChildNames;
                for (var childIndex = 0; children != null && childIndex < children.Count; childIndex++)
                {
                    if (!SameName(children[childIndex], childName))
                        continue;
                    if (result != null)
                        return new ModelRootChildMatch { Kind = ModelRootChildMatchKind.Ambiguous };
                    result = new ModelRootChildMatch { Kind = ModelRootChildMatchKind.Child, ModelIndex = modelIndex, ChildIndex = childIndex };
                }
                if (fallback && (SameName(model.RootName, childName) || SameName(Path.GetFileName(model.SourceFile), childName)))
                {
                    if (result != null)
                        return new ModelRootChildMatch { Kind = ModelRootChildMatchKind.Ambiguous };
                    result = new ModelRootChildMatch { Kind = ModelRootChildMatchKind.Root, ModelIndex = modelIndex };
                }
            }
            return result ?? new ModelRootChildMatch { Kind = ModelRootChildMatchKind.NotFound };
        }

        private static bool MatchesRoot(ModelRootCandidate model, string rootName, string sourceFile)
        {
            return (string.IsNullOrWhiteSpace(rootName) || string.Equals(model.RootName, rootName.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                   (string.IsNullOrWhiteSpace(sourceFile) ||
                    string.Equals(model.SourceFile, sourceFile.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(model.FileName, sourceFile.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(model.SourceFile), Path.GetFileName(sourceFile.Trim()), StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameName(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
