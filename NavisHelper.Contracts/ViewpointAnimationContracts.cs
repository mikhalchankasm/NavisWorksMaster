using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ViewpointAnimationRequest
    {
        public string Action { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string SourceFolder { get; set; }
        public List<string> Viewpoints { get; set; } = new List<string>();
        public double? HoldSeconds { get; set; }
        public double? PauseSeconds { get; set; }
        public double? StepSeconds { get; set; }
        public bool? Loops { get; set; }
        public string Smoothing { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ViewpointAnimationResponse
    {
        public string Action { get; set; }
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public int FrameCount { get; set; }
        public int CutCount { get; set; }
        public double TotalPauseSeconds { get; set; }
        public List<ViewpointAnimationFrame> Frames { get; set; } = new List<ViewpointAnimationFrame>();
        public List<ViewpointAnimationSummary> Animations { get; set; } = new List<ViewpointAnimationSummary>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ViewpointAnimationFrame
    {
        public string Kind { get; set; }
        public string Source { get; set; }
        public double? DelaySeconds { get; set; }
    }

    public sealed class ViewpointAnimationSummary
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int FrameCount { get; set; }
        public int CutCount { get; set; }
        public bool? Loops { get; set; }
        public string Smoothing { get; set; }
    }

    public static class ViewpointAnimationRules
    {
        public static readonly string[] Actions = { "list", "create", "delete" };
        public static readonly string[] Smoothings = { "none", "sync", "spline" };
        public const int MaxViewpoints = 1000;

        public static string Describe(ViewpointAnimationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var action = string.IsNullOrWhiteSpace(request.Action) ? "list" : request.Action;
            if (!Actions.Contains(action, StringComparer.OrdinalIgnoreCase))
                return "action must be list, create or delete.";
            if (!string.Equals(action, "list", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(request.Name))
                return "name is required for create and delete.";
            if (string.Equals(action, "create", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(request.SourceFolder) == (request.Viewpoints != null && request.Viewpoints.Count > 0))
                    return "create needs exactly one of sourceFolder or viewpoints.";
                if (request.Viewpoints != null && request.Viewpoints.Any(string.IsNullOrWhiteSpace))
                    return "viewpoints must not contain blank entries.";
                if (request.Viewpoints != null && request.Viewpoints.Count > MaxViewpoints)
                    return "create accepts at most 1000 viewpoints.";
            }
            if (!string.IsNullOrEmpty(request.Smoothing) && !Smoothings.Contains(request.Smoothing, StringComparer.OrdinalIgnoreCase))
                return "smoothing must be none, sync or spline.";
            if (new[] { request.HoldSeconds, request.PauseSeconds, request.StepSeconds }
                .Any(value => value.HasValue && (double.IsNaN(value.Value) || value < 0 || value > 3600)))
                return "holdSeconds, pauseSeconds and stepSeconds must be between 0 and 3600.";
            return null;
        }

        public static List<ViewpointAnimationFrame> PlanFrames(IList<string> names, IList<string> cameraKeys,
            double holdSeconds, double pauseSeconds)
        {
            if (names == null || cameraKeys == null || names.Count != cameraKeys.Count)
                throw new ArgumentException("Names and camera keys must have equal lengths.");
            var result = new List<ViewpointAnimationFrame>();
            for (var index = 0; index < names.Count; index++)
            {
                if (index > 0 && string.Equals(cameraKeys[index - 1], cameraKeys[index], StringComparison.Ordinal) && pauseSeconds > 0)
                    result.Add(new ViewpointAnimationFrame { Kind = "cut", DelaySeconds = pauseSeconds });
                result.Add(new ViewpointAnimationFrame { Kind = "viewpoint", Source = names[index] });
                if (holdSeconds > 0 && (index == names.Count - 1 || !string.Equals(cameraKeys[index], cameraKeys[index + 1], StringComparison.Ordinal)))
                {
                    result.Add(new ViewpointAnimationFrame { Kind = "cut", DelaySeconds = holdSeconds });
                    result.Add(new ViewpointAnimationFrame { Kind = "viewpoint", Source = names[index] });
                }
            }
            return result;
        }

        public static string CameraKey(double x, double y, double z, double a, double b, double c, double d)
        {
            var rotation = new[] { a, b, c, d }.Select(value => Math.Round(value, 4)).ToArray();
            if (rotation.FirstOrDefault(value => value != 0) < 0)
                rotation = rotation.Select(value => -value).ToArray();
            return string.Join(";", new[] { Math.Round(x, 3), Math.Round(y, 3), Math.Round(z, 3) }
                .Concat(rotation).Select(value => (value == 0 ? 0 : value).ToString(CultureInfo.InvariantCulture)));
        }

        public static (double? Linear, double? Angular) StepSpeeds(double distance, double angleRadians, double stepSeconds)
        {
            return (stepSeconds > 1e-9 && distance > 1e-9 ? (double?)(distance / stepSeconds) : null,
                stepSeconds > 1e-9 && angleRadians > 1e-9 ? (double?)(angleRadians / stepSeconds) : null);
        }
    }
}
