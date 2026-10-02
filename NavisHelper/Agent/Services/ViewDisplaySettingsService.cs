using System;
using System.Globalization;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ViewDisplaySettingsService
    {
        private const string BackgroundWarning = "The public SDK does not expose full background mode/color readback. " +
            "Background setter completion is not visual verification; inspect the view. No previous background is inferred or cached.";

        public ViewDisplaySettingsState Read(Document document)
        {
            EnsureView(document);
            using (var view = document.CurrentViewpoint.CreateCopy())
            {
                var state = new ViewDisplaySettingsState
                {
                    Lighting = LightingName(view.Lighting),
                    RenderStyle = RenderName(view.RenderStyle),
                    SupportsHorizon = document.ActiveSheetType == SheetType.Sheet3D && view.Projection == ViewpointProjection.Perspective,
                    BackgroundReadbackAvailable = false,
                    DocumentModified = document.IsModified,
                };
                state.Warnings.Add(BackgroundWarning);
                return state;
            }
        }

        public ViewDisplaySettingsResponse Set(Document document, ViewDisplaySettingsRequest request)
        {
            var before = Read(document);
            ViewDisplaySettingsRequest plan;
            try { plan = ViewDisplaySettingsValidation.Validate(request, before.SupportsHorizon); }
            catch (ArgumentException exception) { throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message); }
            var response = new ViewDisplaySettingsResponse { Apply = plan.Apply == true, Requested = plan, Before = before };
            response.Warnings.Add(BackgroundWarning);
            if (!response.Apply)
                return response;

            using (var original = document.CurrentViewpoint.CreateCopy())
            using (var candidate = original.CreateCopy())
            {
                bool viewAttempted = false, backgroundAttempted = false;
                try
                {
                    if (plan.Lighting != null) candidate.Lighting = ParseLighting(plan.Lighting);
                    if (plan.RenderStyle != null) candidate.RenderStyle = ParseRender(plan.RenderStyle);
                    if (plan.Lighting != null || plan.RenderStyle != null)
                    {
                        viewAttempted = true;
                        document.CurrentViewpoint.CopyFrom(candidate);
                    }
                    var after = Read(document);
                    if ((plan.Lighting != null && after.Lighting != plan.Lighting) ||
                        (plan.RenderStyle != null && after.RenderStyle != plan.RenderStyle))
                        throw new InvalidOperationException("Navisworks did not retain the requested lighting/render style.");
                    // Apply background last: the public SDK cannot snapshot it for reliable rollback.
                    if (plan.Background != null)
                    {
                        var colors = Array.ConvertAll(plan.Background.Colors, ToColor);
                        backgroundAttempted = true;
                        if (plan.Background.Mode == "plain") document.SetPlainBackground(colors[0]);
                        else if (plan.Background.Mode == "graduated") document.SetGraduatedBackground(colors[0], colors[1]);
                        else document.SetHorizonBackground(colors[0], colors[1], colors[2], colors[3]);
                        response.BackgroundSetterCompleted = true;
                    }
                }
                catch (Exception exception)
                {
                    var recovery = "Lighting/render state was not changed.";
                    if (viewAttempted)
                    {
                        try
                        {
                            document.CurrentViewpoint.CopyFrom(original);
                            document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                            recovery = "Original viewpoint restored.";
                        }
                        catch (Exception restore) { recovery = "Viewpoint restoration failed: " + restore.Message; }
                    }
                    throw new AgentCommandException(ErrorCodes.CommandFailed,
                        "Display settings apply failed. " + recovery +
                        (backgroundAttempted ? " Background state is unknown and was not restored; inspect the view before retrying." : " Background was not touched.") +
                        " Cause: " + exception.Message);
                }
                // Mutation completed. A redraw/readback failure must not undo only the
                // viewpoint after the non-restorable background setter succeeded.
                response.Applied = true;
                try
                {
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                    response.After = Read(document);
                }
                catch (Exception exception)
                {
                    response.Warnings.Add("Settings applied; post-apply redraw/readback failed: " + exception.Message);
                }
                return response;
            }
        }

        private static void EnsureView(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
        }

        private static Color ToColor(string hex)
        {
            int rgb = int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0, (rgb & 255) / 255.0);
        }

        private static string LightingName(ViewpointLighting value)
        {
            switch (value)
            {
                case ViewpointLighting.None: return "none";
                case ViewpointLighting.SceneLights: return "scene_lights";
                case ViewpointLighting.Headlight: return "headlight";
                case ViewpointLighting.FullLights: return "full_lights";
                default: return "unknown";
            }
        }

        private static string RenderName(ViewpointRenderStyle value)
        {
            switch (value)
            {
                case ViewpointRenderStyle.FullRender: return "full_render";
                case ViewpointRenderStyle.Preview: return "preview";
                case ViewpointRenderStyle.Shaded: return "shaded";
                case ViewpointRenderStyle.Wireframe: return "wireframe";
                case ViewpointRenderStyle.HiddenLine: return "hidden_line";
                default: return "unknown";
            }
        }

        private static ViewpointLighting ParseLighting(string value)
        {
            switch (value)
            {
                case "none": return ViewpointLighting.None;
                case "scene_lights": return ViewpointLighting.SceneLights;
                case "headlight": return ViewpointLighting.Headlight;
                case "full_lights": return ViewpointLighting.FullLights;
                default: throw new ArgumentException("Unknown lighting.");
            }
        }

        private static ViewpointRenderStyle ParseRender(string value)
        {
            switch (value)
            {
                case "full_render": return ViewpointRenderStyle.FullRender;
                case "preview": return ViewpointRenderStyle.Preview;
                case "shaded": return ViewpointRenderStyle.Shaded;
                case "wireframe": return ViewpointRenderStyle.Wireframe;
                case "hidden_line": return ViewpointRenderStyle.HiddenLine;
                default: throw new ArgumentException("Unknown render style.");
            }
        }
    }
}
