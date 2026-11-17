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
            // With no recorded model, children and model roots are searched together, so a file loaded as a
            // model root and a same-named child of another model refuse as ambiguous.
            var recorded = !string.IsNullOrWhiteSpace(rootName) || !string.IsNullOrWhiteSpace(sourceFile);
            if (!recorded)
                return Find(models, null, childName, true);
            // The recorded model, when it is here, is the only place to look: a recorded model without the
            // child is a miss, and two models that fit the record (same file name in two folders) refuse as
            // ambiguous, as model-root references always have. Other models are searched only when the
            // recorded one is absent (another revision, or an NWF where the file is a model root).
            var recordedModels = RecordedModels(models, rootName, sourceFile);
            if (recordedModels.Count > 1)
                return new ModelRootChildMatch { Kind = ModelRootChildMatchKind.Ambiguous };
            if (recordedModels.Count == 1)
                return Find(models, recordedModels, childName, false);
            return Find(models, null, childName, true);
        }

        private static List<int> RecordedModels(IList<ModelRootCandidate> models, string rootName, string sourceFile)
        {
            var matched = Enumerable.Range(0, models.Count)
                .Where(index => models[index] != null && MatchesRoot(models[index], rootName, sourceFile))
                .ToList();
            if (matched.Count <= 1 || string.IsNullOrWhiteSpace(sourceFile))
                return matched;
            // Several models fit by file name; an exact path match decides, otherwise all stay (ambiguous).
            var exact = matched.Where(index =>
                string.Equals((models[index].SourceFile ?? string.Empty).Trim(), sourceFile.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals((models[index].FileName ?? string.Empty).Trim(), sourceFile.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return exact.Count == 1 ? exact : matched;
        }

        private static ModelRootChildMatch Find(IList<ModelRootCandidate> models, IList<int> only, string childName, bool fallback)
        {
            ModelRootChildMatch result = null;
            for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                var model = models[modelIndex];
                if (model == null || (only != null && !only.Contains(modelIndex)))
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
                if (fallback && (SameName(model.RootName, childName) || SameName(FileNameOf(model.SourceFile), childName) ||
                                 SameName(FileNameOf(model.FileName), childName)))
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

        private static string FileNameOf(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path.Trim());
        }

        private static bool SameName(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
