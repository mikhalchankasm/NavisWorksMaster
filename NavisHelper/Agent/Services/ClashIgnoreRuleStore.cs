using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal static class ClashIgnoreRuleStore
    {
        private const string DataFolderName = "__NavisHelper_Data";
        private const string DataViewpointName = "Clash Ignore Rules";
        private const string PayloadPrefix = "NavisHelperIgnoreRules:v1:";

        public static List<ClashIgnoreRule> Load(Document document)
        {
            var viewpoint = FindDataViewpoint(document);
            if (viewpoint == null || viewpoint.Comments == null)
                return new List<ClashIgnoreRule>();
            foreach (var comment in viewpoint.Comments)
            {
                var body = comment == null ? string.Empty : comment.Body ?? string.Empty;
                if (!body.StartsWith(PayloadPrefix, StringComparison.Ordinal))
                    continue;
                try
                {
                    return JsonConvert.DeserializeObject<List<ClashIgnoreRule>>(body.Substring(PayloadPrefix.Length))
                        ?? new List<ClashIgnoreRule>();
                }
                catch
                {
                    return new List<ClashIgnoreRule>();
                }
            }
            return new List<ClashIgnoreRule>();
        }

        public static void Save(Document document, IList<ClashIgnoreRule> rules)
        {
            if (document == null || document.SavedViewpoints == null || document.CurrentViewpoint == null)
                throw new InvalidOperationException("Saved Viewpoints are not available for document-persistent ignore rules.");
            var folder = document.SavedViewpoints.RootItem.Children
                .OfType<FolderItem>()
                .FirstOrDefault(item => string.Equals(item.DisplayName, DataFolderName, StringComparison.Ordinal));
            if (folder == null)
            {
                document.SavedViewpoints.InsertCopy(document.SavedViewpoints.RootItem, document.SavedViewpoints.RootItem.Children.Count, new FolderItem { DisplayName = DataFolderName });
                folder = document.SavedViewpoints.RootItem.Children
                    .OfType<FolderItem>()
                    .FirstOrDefault(item => string.Equals(item.DisplayName, DataFolderName, StringComparison.Ordinal));
            }
            if (folder == null)
                throw new InvalidOperationException("Could not create NavisHelper document data folder.");

            var viewpoint = folder.Children
                .OfType<SavedViewpoint>()
                .FirstOrDefault(item => string.Equals(item.DisplayName, DataViewpointName, StringComparison.Ordinal));
            if (viewpoint == null)
            {
                document.SavedViewpoints.AddCopy(folder, new SavedViewpoint(document.CurrentViewpoint.CreateCopy()) { DisplayName = DataViewpointName });
                viewpoint = folder.Children
                    .OfType<SavedViewpoint>()
                    .FirstOrDefault(item => string.Equals(item.DisplayName, DataViewpointName, StringComparison.Ordinal));
            }
            if (viewpoint == null)
                throw new InvalidOperationException("Could not create NavisHelper ignore-rule data viewpoint.");

            var payload = PayloadPrefix + JsonConvert.SerializeObject(rules ?? new List<ClashIgnoreRule>(), Formatting.None);
            var comments = new CommentCollection { new Comment(payload, CommentStatus.New) };
            document.SavedViewpoints.EditComments(viewpoint, comments);
        }

        public static int ApplyRules(DocumentClashTests testsData, ClashTest test, IEnumerable<ClashIgnoreRule> rules)
        {
            if (testsData == null || test == null)
                return 0;
            var normalizedRules = (rules ?? Enumerable.Empty<ClashIgnoreRule>()).Where(rule => rule != null).ToList();
            if (normalizedRules.Count == 0)
                return 0;
            var affected = 0;
            foreach (var result in ClashWorkflowService.EnumerateResults(test))
            {
                var rule = normalizedRules.FirstOrDefault(item => Matches(item, test.DisplayName ?? string.Empty, result));
                if (rule == null || ClashWorkflowService.IsIgnoredResult(result))
                    continue;
                var reason = string.IsNullOrWhiteSpace(rule.Reason) ? rule.Name : rule.Name + ": " + rule.Reason;
                ClashWorkflowService.ApplyResultUpdates(
                    testsData,
                    new[] { result },
                    ClashResultStatus.Approved,
                    string.Empty,
                    ClashWorkflowService.IgnoreCommentPrefix + reason);
                affected++;
            }
            return affected;
        }

        public static bool Matches(ClashIgnoreRule rule, string testName, ClashResult result)
        {
            if (rule == null || result == null || !WildcardMatch(testName, rule.TestNamePattern))
                return false;
            return MatchesSide(result.Selection1, result.Item1, rule.ItemAContains) &&
                   MatchesSide(result.Selection2, result.Item2, rule.ItemBContains);
        }

        private static bool MatchesSide(ModelItemCollection selection, ModelItem fallback, IEnumerable<string> filters)
        {
            var values = (filters ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            if (values.Count == 0)
                return true;
            var candidates = new List<string>();
            if (selection != null)
            {
                foreach (ModelItem item in selection)
                {
                    candidates.Add(SafeName(item));
                    candidates.Add(BuildPath(item));
                }
            }
            if (fallback != null)
            {
                candidates.Add(SafeName(fallback));
                candidates.Add(BuildPath(fallback));
            }
            return values.All(filter => candidates.Any(candidate =>
                !string.IsNullOrWhiteSpace(candidate) && candidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool WildcardMatch(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return true;
            var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildPath(ModelItem item)
        {
            var segments = new List<string>();
            var current = item;
            while (current != null)
            {
                var name = SafeName(current);
                if (!string.IsNullOrWhiteSpace(name))
                    segments.Add(name);
                try { current = current.Parent; }
                catch { current = null; }
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string SafeName(ModelItem item)
        {
            try { return item == null ? string.Empty : item.DisplayName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static SavedViewpoint FindDataViewpoint(Document document)
        {
            if (document == null || document.SavedViewpoints == null)
                return null;
            var folder = document.SavedViewpoints.RootItem.Children
                .OfType<FolderItem>()
                .FirstOrDefault(item => string.Equals(item.DisplayName, DataFolderName, StringComparison.Ordinal));
            return folder == null ? null : folder.Children
                .OfType<SavedViewpoint>()
                .FirstOrDefault(item => string.Equals(item.DisplayName, DataViewpointName, StringComparison.Ordinal));
        }
    }
}
