using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ItemViewpointsRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public string FolderPath { get; set; }
        public string NameTemplate { get; set; }
        public string Section { get; set; }
        public double? PlaneOffsetMm { get; set; }
        public double? BoxOffsetMm { get; set; }
        public string Camera { get; set; }
        public double? ContextMm { get; set; }
        public string MarkStyle { get; set; }
        public List<double> EllipseColor { get; set; }
        public int? Thickness { get; set; }
        public bool? Overwrite { get; set; }
        public int? MaxItems { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ItemViewpointsResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public int ItemCount { get; set; }
        public int CreatedCount { get; set; }
        public bool Truncated { get; set; }
        public string FolderPath { get; set; }
        public List<ItemViewpointResult> Items { get; set; } = new List<ItemViewpointResult>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ItemViewpointResult
    {
        public string ItemName { get; set; }
        public string Path { get; set; }
        public string ViewpointName { get; set; }
        public int MarkCount { get; set; }
        public string Error { get; set; }
    }

    public static class ItemViewpointRules
    {
        public static readonly string[] Sections = { "none", "plane", "box" };

        public static int ClampMaxItems(int? value) => Math.Max(1, Math.Min(1000, value ?? 500));

        public static string Describe(ItemViewpointsRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.MatchHandles != null && request.MatchHandles.Any(string.IsNullOrWhiteSpace))
                return "matchHandles must not contain blank handles.";
            if (!string.IsNullOrEmpty(request.Section) &&
                !Sections.Contains(request.Section, StringComparer.OrdinalIgnoreCase))
                return "section must be none, plane or box.";
            if (!string.IsNullOrEmpty(request.NameTemplate) && !request.NameTemplate.Contains("{name}"))
                return "nameTemplate must contain {name}.";
            if (request.PlaneOffsetMm < 0 || request.BoxOffsetMm < 0 || request.ContextMm < 0)
                return "planeOffsetMm, boxOffsetMm and contextMm must not be negative.";
            return ViewpointCameraRules.Describe(request.Camera ?? "top");
        }

        public static List<string> PlanNames(IList<string> displayNames, string template, int firstIndex = 1)
        {
            template = string.IsNullOrEmpty(template) ? "{name}" : template;
            var names = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < displayNames.Count; index++)
            {
                var itemName = (displayNames[index] ?? "").Replace('/', ' ').Replace('\\', ' ').Trim();
                if (itemName.Length == 0) itemName = "item";
                var name = Regex.Replace(template, @"\{name\}|\{index\}", match =>
                    match.Value == "{name}" ? itemName : (firstIndex + index).ToString(CultureInfo.InvariantCulture));
                var candidate = name;
                var suffix = 2;
                while (!used.Add(candidate))
                    candidate = name + " (" + (suffix++).ToString(CultureInfo.InvariantCulture) + ")";
                names.Add(candidate);
            }
            return names;
        }
    }
}
