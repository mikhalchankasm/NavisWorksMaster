using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class AppearanceProfileService
    {
        private const long MaximumFileBytes = 10 * 1024 * 1024;
        private const int MaximumReturnedRules = 200;

        public AppearanceProfileApplyResponse Execute(Document document, AppearanceProfileApplyRequest request)
        {
            var timer = Stopwatch.StartNew();
            request = request ?? new AppearanceProfileApplyRequest();
            var pathError = AppearanceProfileFile.DescribePathError(request.ProfilePath);
            if (pathError != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, pathError);
            if (!File.Exists(request.ProfilePath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Appearance Profiler profile file does not exist.");

            AppearanceProfileSummary profile;
            try
            {
                using (var stream = File.OpenRead(request.ProfilePath))
                {
                    if (stream.Length > MaximumFileBytes)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Appearance Profiler profile exceeds 10 MB.");
                    using (var reader = new StreamReader(stream))
                        profile = AppearanceProfileFile.Parse(reader.ReadToEnd());
                }
            }
            catch (FormatException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
            }
            catch (IOException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot read Appearance Profiler profile: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Cannot read Appearance Profiler profile: " + exception.Message);
            }

            if (request.Apply == true && profile.Errors.Count > 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    "Appearance Profiler profile cannot be applied: " + string.Join("; ", profile.Errors.Take(5)));

            var response = new AppearanceProfileApplyResponse
            {
                ProfilePath = request.ProfilePath,
                Apply = request.Apply == true,
                Profile = profile,
                RulesTruncated = profile.Rules.Count > MaximumReturnedRules,
                Message = "Appearance Profiler dry-run; no overrides applied.",
            };
            profile.Rules = profile.Rules.Take(MaximumReturnedRules).ToList();
            response.Warnings.AddRange(profile.Warnings);
            response.Warnings.Add("Appearance Profiler overrides apply as one undo step (Ctrl+Z in Navisworks); model_color_scheme reset does not undo them. Use Ctrl+Z, or reopen without saving. Unmatched items are greyed as in the profiler.");
            if (response.Apply)
            {
                // The native loader silently returns 0 unless the Appearance Profiler dock pane
                // plugin is enabled and loaded (it calls TryLoadPlugin itself and gives up quietly).
                // Load it first and report its state, so a silent no-op becomes visible.
                EnsureProfilerPaneLoaded(response);
                try
                {
                    response.PluginResult = Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin(
                        "AutoAppearanceLoader.Navisworks", request.ProfilePath);
                }
                catch (Exception exception)
                {
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Appearance Profiler loader failed: " + exception.Message);
                }
                response.Applied = true;
                response.Message = "Appearance Profiler loader completed; inspect the view to verify the overrides.";
                try
                {
                    if (document != null && document.ActiveView != null)
                        document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                }
                catch (Exception exception)
                {
                    response.Warnings.Add("Could not request redraw: " + exception.Message);
                }
            }
            response.ElapsedMs = timer.ElapsedMilliseconds;
            return response;
        }

        private static void EnsureProfilerPaneLoaded(AppearanceProfileApplyResponse response)
        {
            try
            {
                var record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("AutoAppearanceDockPane.Navisworks");
                if (record == null)
                {
                    response.Warnings.Add("Appearance Profiler pane plugin not found; the native loader will do nothing.");
                    return;
                }
                var state = "pane plugin: type " + record.GetType().Name + ", enabled " + record.IsEnabled + ", loaded " + record.IsLoaded;
                if (record.IsEnabled && !record.IsLoaded)
                {
                    record.LoadPlugin();
                    state += " -> loaded " + record.IsLoaded;
                }
                var pane = record.LoadedPlugin as Autodesk.Navisworks.Api.Plugins.DockPanePlugin;
                state += ", dock pane " + (pane == null ? "missing" : "ready");
                response.Warnings.Add("Appearance Profiler " + state + ".");
            }
            catch (Exception exception)
            {
                response.Warnings.Add("Could not prepare the Appearance Profiler pane: " + exception.Message);
            }
        }
    }
}
