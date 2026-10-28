using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ViewpointAnimationService
    {
        private static readonly PropertyInfo Delay = typeof(SavedViewpointAnimationCut).GetProperty("Delay");
        private static readonly PropertyInfo Loops = typeof(SavedViewpointAnimation).GetProperty("Loops");
        private static readonly PropertyInfo Smoothing = typeof(SavedViewpointAnimation).GetProperty("Smoothing");

        public ViewpointAnimationResponse Execute(Document document, ViewpointAnimationRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ViewpointAnimationRequest();
            var error = ViewpointAnimationRules.Describe(request);
            if (error != null) throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var root = document.SavedViewpoints == null ? null : document.SavedViewpoints.RootItem;
            if (root == null) throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The document has no saved viewpoints collection.");
            var action = string.IsNullOrWhiteSpace(request.Action) ? "list" : request.Action.ToLowerInvariant();
            var folderPath = DocumentCommandService.NormalizeFolderPath(request.FolderPath);
            var name = (request.Name ?? "").Trim();
            var response = new ViewpointAnimationResponse
            {
                Action = action, Apply = request.Apply == true, Name = name, Path = Join(folderPath, name),
            };
            if (action == "list")
            {
                response.Animations = DocumentCommandService.BuildSavedViewpointNodes(root)
                    .Where(node => node.Item is SavedViewpointAnimation)
                    .Select(node => Summarize((SavedViewpointAnimation)node.Item, node.Path)).ToList();
                response.Message = "Listed saved viewpoint animations.";
                return response;
            }

            var folder = ResolveFolder(document, folderPath, false);
            var matches = folder == null ? new List<SavedItem>() : folder.Children.Cast<SavedItem>()
                .Where(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (action == "delete")
            {
                var animation = matches.Count == 1 ? matches[0] as SavedViewpointAnimation : null;
                if (animation == null)
                    throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "Saved viewpoint animation was not found uniquely in the target folder.");
                response.Animations.Add(Summarize(animation, response.Path));
                if (response.Apply)
                {
                    using (var transaction = document.BeginTransaction("NavisHelper MCP Delete Viewpoint Animation"))
                    {
                        if (!document.SavedViewpoints.Remove(folder, animation))
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to delete the saved viewpoint animation.");
                        transaction.Commit();
                    }
                    response.Applied = true;
                }
                response.Message = response.Applied ? "Deleted saved viewpoint animation." : "Dry-run: animation would be deleted.";
                return response;
            }
            if (matches.Count > 0 && (request.Overwrite != true || matches.Any(match => !(match is SavedViewpointAnimation))))
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, request.Overwrite == true
                    ? "overwrite replaces only a saved viewpoint animation; another item has this name in the target folder."
                    : "An item with the same name already exists in the target folder.");

            var sources = ResolveSources(root, request);
            if (sources.Count > ViewpointAnimationRules.MaxViewpoints)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "create accepts at most 1000 viewpoints.");
            if (sources.Count == 0)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The source folder has no direct saved viewpoints.");
            var keys = sources.Select(node => CameraKey(((SavedViewpoint)node.Item).Viewpoint)).ToList();
            // Index tokens preserve distinct source states even when siblings have identical display paths.
            var tokens = Enumerable.Range(0, sources.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToList();
            var plan = ViewpointAnimationRules.PlanFrames(tokens, keys, request.HoldSeconds ?? 0, request.PauseSeconds ?? 2);
            response.CutCount = plan.Count(frame => frame.Kind == "cut");
            response.FrameCount = plan.Count - response.CutCount;
            response.TotalPauseSeconds = plan.Sum(frame => frame.DelaySeconds ?? 0);
            using (var animation = new SavedViewpointAnimation())
            {
                animation.DisplayName = name;
                // The tool always sends loops; on 2024/2025 only a requested loop is worth a warning.
                var loops = request.Loops;
                if (loops == true || (loops.HasValue && Loops != null))
                    SetOptional(Loops, animation, loops.Value, "This Navisworks version cannot set animation loops; the Navisworks default is kept.", response);
                if (!string.IsNullOrEmpty(request.Smoothing))
                    SetOptional(Smoothing, animation, request.Smoothing, "This Navisworks version cannot set animation smoothing; the Navisworks default is kept.", response);
                int? previousIndex = null;
                foreach (var frame in plan)
                {
                    if (frame.Kind == "cut")
                    {
                        var cut = new SavedViewpointAnimationCut();
                        SetOptional(Delay, cut, frame.DelaySeconds.Value,
                            "This Navisworks version cannot set the cut delay; cuts keep the Navisworks default.", response);
                        animation.Children.Add(cut);
                    }
                    else
                    {
                        var index = int.Parse(frame.Source, CultureInfo.InvariantCulture);
                        var source = (SavedViewpoint)sources[index].Item;
                        var copy = source.CreateCopy() as SavedViewpoint;
                        if (copy == null) throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to copy a saved viewpoint.");
                        if (previousIndex.HasValue && keys[previousIndex.Value] != keys[index] && request.StepSeconds > 0)
                        {
                            SetSpeeds(((SavedViewpoint)sources[previousIndex.Value].Item).Viewpoint,
                                copy.Viewpoint, request.StepSeconds.Value, response);
                        }
                        animation.Children.Add(copy);
                        previousIndex = index;
                        frame.Source = sources[index].Path;
                    }
                    response.Frames.Add(frame);
                }
                if (response.Apply)
                {
                    using (var transaction = document.BeginTransaction("NavisHelper MCP Create Viewpoint Animation"))
                    {
                        folder = ResolveFolder(document, folderPath, true);
                        foreach (var match in matches)
                            if (!document.SavedViewpoints.Remove(folder, match))
                                throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to replace an existing saved viewpoint item.");
                        document.SavedViewpoints.AddCopy(folder, animation);
                        transaction.Commit();
                    }
                    response.Applied = true;
                }
            }
            response.Message = response.Applied ? "Created saved viewpoint animation." : "Dry-run: no saved viewpoints were changed.";
            return response;
        }

        private static List<DocumentCommandService.SavedViewpointNode> ResolveSources(GroupItem root, ViewpointAnimationRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.SourceFolder))
            {
                var source = DocumentCommandService.ResolveSavedViewpointNode(root,
                    new SavedViewpointsManageRequest { PathOrName = request.SourceFolder }, item => item is FolderItem,
                    "Source folder was not found.");
                return ((FolderItem)source.Item).Children.OfType<SavedViewpoint>()
                    .Select(item => new DocumentCommandService.SavedViewpointNode { Item = item, Path = Join(source.Path, item.DisplayName) }).ToList();
            }
            return request.Viewpoints.Select(path => DocumentCommandService.ResolveSavedViewpointNode(root,
                new SavedViewpointsManageRequest { PathOrName = path },
                item => item is SavedViewpoint && !(item.Parent is SavedViewpointAnimation),
                "Saved viewpoint was not found.")).ToList();
        }

        private static GroupItem ResolveFolder(Document document, string path, bool create)
        {
            var current = document.SavedViewpoints.RootItem;
            foreach (var segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var matches = current.Children.Cast<SavedItem>()
                    .Where(item => string.Equals(item.DisplayName, segment, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 0)
                {
                    if (matches.Count != 1 || !(matches[0] is FolderItem))
                        throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Target folder path conflicts with another saved item.");
                    current = (FolderItem)matches[0];
                }
                else
                {
                    if (!create) return null;
                    document.SavedViewpoints.AddCopy(current, new FolderItem { DisplayName = segment });
                    current = current.Children.OfType<FolderItem>().Single(item => string.Equals(item.DisplayName, segment, StringComparison.OrdinalIgnoreCase));
                }
            }
            return current;
        }

        private static string Join(string parent, string name) => string.IsNullOrEmpty(parent) ? name : parent + "/" + name;

        private static string CameraKey(Viewpoint camera)
        {
            var p = camera.Position;
            var q = camera.Rotation;
            return ViewpointAnimationRules.CameraKey(p.X, p.Y, p.Z, q.A, q.B, q.C, q.D);
        }

        private static void SetSpeeds(Viewpoint previous, Viewpoint destination, double seconds, ViewpointAnimationResponse response)
        {
            Warn(response, "Step timing via frame speeds is unverified until L3.");
            if (destination.IsReadOnly)
            {
                Warn(response, "The copied viewpoint is read-only; saved speeds are kept.");
                return;
            }
            var p = previous.Position;
            var r = destination.Position;
            var distance = Math.Sqrt(Math.Pow(r.X - p.X, 2) + Math.Pow(r.Y - p.Y, 2) + Math.Pow(r.Z - p.Z, 2));
            var q = previous.Rotation;
            var s = destination.Rotation;
            var dot = q.A * s.A + q.B * s.B + q.C * s.C + q.D * s.D;
            var norm = Math.Sqrt((q.A * q.A + q.B * q.B + q.C * q.C + q.D * q.D) *
                (s.A * s.A + s.B * s.B + s.C * s.C + s.D * s.D));
            var angle = norm > 1e-9 ? 2 * Math.Acos(Math.Min(1, Math.Abs(dot / norm))) : 0;
            var speeds = ViewpointAnimationRules.StepSpeeds(distance, angle, seconds);
            if (speeds.Linear.HasValue) destination.LinearSpeed = speeds.Linear.Value;
            if (speeds.Angular.HasValue) destination.AngularSpeed = speeds.Angular.Value;
        }

        private static void SetOptional(PropertyInfo property, object target, object value, string warning, ViewpointAnimationResponse response)
        {
            if (property == null || !property.CanWrite) { Warn(response, warning); return; }
            property.SetValue(target, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value, true) : value, null);
        }

        private static void Warn(ViewpointAnimationResponse response, string warning)
        {
            if (!response.Warnings.Contains(warning)) response.Warnings.Add(warning);
        }

        private static object ReadOptional(PropertyInfo property, object target)
        {
            try { return property == null || !property.CanRead ? null : property.GetValue(target, null); }
            catch { return null; }
        }

        private static ViewpointAnimationSummary Summarize(SavedViewpointAnimation animation, string path)
        {
            var smoothing = ReadOptional(Smoothing, animation);
            return new ViewpointAnimationSummary
            {
                Name = animation.DisplayName, Path = path,
                FrameCount = animation.Children.OfType<SavedViewpoint>().Count(),
                CutCount = animation.Children.OfType<SavedViewpointAnimationCut>().Count(),
                Loops = ReadOptional(Loops, animation) as bool?,
                Smoothing = smoothing == null ? null : smoothing.ToString().ToLowerInvariant(),
            };
        }
    }
}
