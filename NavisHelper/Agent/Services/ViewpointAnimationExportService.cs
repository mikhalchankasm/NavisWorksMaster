using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ViewpointAnimationExportService
    {
        public ViewpointAnimationExportResponse Execute(Document document, ViewpointAnimationExportRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ViewpointAnimationExportRequest();
            var error = ViewpointAnimationExportRules.Describe(request);
            if (error != null) throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var root = document.SavedViewpoints == null ? null : document.SavedViewpoints.RootItem;
            if (root == null) throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The document has no saved viewpoints collection.");
            var node = DocumentCommandService.ResolveSavedViewpointNode(root,
                new SavedViewpointsManageRequest { PathOrName = request.Animation }, item => item is SavedViewpointAnimation,
                "Saved viewpoint animation was not found.");
            var animation = (SavedViewpointAnimation)node.Item;
            string outputDirectory;
            try
            {
                outputDirectory = ViewpointAnimationExportRules.ResolveOutputDirectory(document.FileName, request.OutputDirectory, animation.DisplayName);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
            var response = new ViewpointAnimationExportResponse
            {
                Animation = node.Path,
                FrameCount = animation.Children.OfType<SavedViewpoint>().Count(),
                CutCount = animation.Children.OfType<SavedViewpointAnimationCut>().Count(),
                Format = ViewpointAnimationExportRules.NormalizeFormat(request.Format),
                OutputDirectory = outputDirectory,
                OutputDirectoryExists = Directory.Exists(outputDirectory),
                Message = "Reported the exporter options; nothing was exported.",
            };
            response.Warnings.Add("Export is not available yet; this tool only reports the exporter options and validates the request.");
            var state = ComApiBridge.State;
            object currentAnimationObject = null;
            try
            {
                var currentAnimation = state.CurrentAnimation;
                currentAnimationObject = currentAnimation;
                response.CurrentAnimation = currentAnimation == null ? null : currentAnimation.name;
            }
            finally { Release(currentAnimationObject); }

            foreach (var name in ViewpointAnimationExportRules.ProbedPlugins)
            {
                var plugin = new ExporterPluginOptions { Plugin = name };
                response.Plugins.Add(plugin);
                InwOaPropertyVec vector = null;
                try
                {
                    vector = state.GetIOPluginOptions(name);
                    if (vector == null) plugin.Error = "The plugin returned no options.";
                    else ReadOptions(vector, "", 1, plugin);
                }
                catch (Exception ex) { plugin.Error = ex.Message; }
                finally { Release(vector); }
            }
            return response;
        }

        private static void ReadOptions(InwOaPropertyVec vector, string parent, int depth, ExporterPluginOptions plugin)
        {
            object properties = null;
            IEnumerator enumerator = null;
            try
            {
                properties = vector.Properties();
                enumerator = ((IEnumerable)properties).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (depth > ViewpointAnimationExportRules.MaxOptionDepth || plugin.Options.Count >= ViewpointAnimationExportRules.MaxOptionsPerPlugin)
                    {
                        plugin.Truncated = true;
                        break;
                    }
                    InwOaProperty property = null;
                    object value = null;
                    try
                    {
                        property = (InwOaProperty)enumerator.Current;
                        value = property.value;
                        var nested = value as InwOaPropertyVec;
                        string type;
                        var text = ViewpointAnimationExportRules.FormatOptionValue(nested == null ? value : null, out type);
                        var option = new ExporterOption
                        {
                            Name = property.name, UserName = property.UserName, Parent = parent,
                            Type = nested == null ? type : "Options", Value = text,
                        };
                        plugin.Options.Add(option);
                        if (nested != null) ReadOptions(nested, option.Name, depth + 1, plugin);
                    }
                    finally
                    {
                        Release(value);
                        Release(property);
                    }
                }
            }
            finally
            {
                Release(enumerator);
                Release(properties);
            }
        }

        private static void Release(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            }
            catch (Exception ex) { Logger.Error("Failed to release animation exporter COM object: " + ex.Message, "ViewpointAnimationExport"); }
        }
    }
}
