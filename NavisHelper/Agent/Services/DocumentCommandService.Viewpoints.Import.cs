using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public SavedViewpointsImportResponse SavedViewpointsImport(Document document, SavedViewpointsImportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");
            if (string.IsNullOrWhiteSpace(request.InputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "InputPath is required.");

            var apply = request.Apply == true;
            var preserveXmlFolders = request.PreserveXmlFolders.GetValueOrDefault(true);
            var previewLimit = ClampPreviewLimit(request.PreviewLimit, 200, 1000);
            var targetFolderPath = NormalizeFolderPath(request.TargetFolderPath);
            var inputPath = ResolveExistingFilePath(request.InputPath);
            var plans = ParseSavedViewpointsXml(inputPath, preserveXmlFolders);

            GroupItem targetFolder;
            bool targetFolderExists;
            int createdTargetFolderCount;
            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, targetFolderPath, false, out targetFolder, out targetFolderExists, out createdTargetFolderCount))
                targetFolder = document.SavedViewpoints.RootItem;

            var response = new SavedViewpointsImportResponse
            {
                Apply = apply,
                InputPath = inputPath,
                TargetFolderPath = targetFolderPath,
                PreserveXmlFolders = preserveXmlFolders,
                TargetFolderExists = targetFolderExists,
                ParsedFolderCount = plans.Count(item => string.Equals(item.Type, "folder", StringComparison.OrdinalIgnoreCase)),
                ParsedViewpointCount = plans.Count(item => string.Equals(item.Type, "view", StringComparison.OrdinalIgnoreCase)),
            };

            foreach (var warning in plans.SelectMany(item => item.Warnings))
                AddUniqueWarning(response.Warnings, warning);

            if (apply)
            {
                if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, targetFolderPath, true, out targetFolder, out targetFolderExists, out createdTargetFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target saved viewpoints folder.");

                response.CreatedFolderCount = createdTargetFolderCount;
            }

            foreach (var plan in plans)
            {
                var targetPath = CombineSavedItemPath(targetFolderPath, plan.RelativePath);
                AddImportPreview(response, plan, targetPath, previewLimit, apply && string.Equals(plan.Type, "view", StringComparison.OrdinalIgnoreCase) && plan.Viewpoint != null);

                if (string.Equals(plan.Type, "folder", StringComparison.OrdinalIgnoreCase))
                {
                    if (apply)
                    {
                        GroupItem ignoredFolder;
                        bool ignoredExists;
                        int createdFolderCount;
                        TryResolveSavedViewpointFolder(document.SavedViewpoints, targetPath, true, out ignoredFolder, out ignoredExists, out createdFolderCount);
                        response.CreatedFolderCount = response.CreatedFolderCount.GetValueOrDefault() + createdFolderCount;
                    }

                    continue;
                }

                if (plan.Viewpoint == null)
                {
                    response.SkippedItemCount++;
                    continue;
                }

                if (!apply)
                    continue;

                var parentPath = CombineSavedItemPath(targetFolderPath, plan.RelativeFolderPath);
                GroupItem importFolder;
                bool importFolderExists;
                int createdImportFolderCount;
                if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, parentPath, true, out importFolder, out importFolderExists, out createdImportFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve import folder: " + parentPath);

                response.CreatedFolderCount = response.CreatedFolderCount.GetValueOrDefault() + createdImportFolderCount;

                var savedViewpoint = new SavedViewpoint(plan.Viewpoint.CreateCopy())
                {
                    DisplayName = plan.Name,
                };

                document.SavedViewpoints.AddCopy(importFolder, savedViewpoint);
                response.ImportedViewpointCount++;

                var inserted = FindLastSavedViewpoint(importFolder, plan.Name);
                if (inserted != null && !string.IsNullOrWhiteSpace(plan.RedlinesJson))
                    ApplyImportedRedlines(document, inserted, plan.Viewpoint, plan.RedlinesJson, response.Warnings);
            }

            response.Changed = apply ? (bool?)(response.ImportedViewpointCount > 0 || response.CreatedFolderCount.GetValueOrDefault() > 0) : null;
            return response;
        }

        private static string ResolveExistingFilePath(string inputPath)
        {
            try
            {
                var path = Path.GetFullPath(inputPath);
                if (!File.Exists(path))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Input XML file was not found: " + path);

                return path;
            }
            catch (AgentCommandException)
            {
                throw;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid inputPath: " + ex.Message);
            }
        }

        private static List<SavedViewpointsImportPlan> ParseSavedViewpointsXml(string inputPath, bool preserveXmlFolders)
        {
            XDocument xml;
            try
            {
                xml = XDocument.Load(inputPath, LoadOptions.None);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to read saved viewpoints XML: " + ex.Message);
            }

            var viewpoints = xml.Root == null
                ? null
                : xml.Root.Descendants().FirstOrDefault(element => IsXmlElement(element, "viewpoints"));
            if (viewpoints == null && xml.Root != null && IsXmlElement(xml.Root, "viewpoints"))
                viewpoints = xml.Root;
            if (viewpoints == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The XML file does not contain a viewpoints node.");

            var plans = new List<SavedViewpointsImportPlan>();
            foreach (var child in viewpoints.Elements())
                CollectSavedViewpointImportPlans(child, string.Empty, string.Empty, preserveXmlFolders, plans);

            return plans;
        }

        private static void CollectSavedViewpointImportPlans(
            XElement element,
            string xmlFolderPath,
            string sourcePath,
            bool preserveXmlFolders,
            IList<SavedViewpointsImportPlan> plans)
        {
            if (element == null || plans == null)
                return;

            if (IsXmlElement(element, "viewfolder") || IsXmlElement(element, "folder"))
            {
                var folderName = NormalizeImportedSavedItemName(ReadStringAttribute(element, "name"), "Imported Folder");
                var nextXmlFolderPath = CombineSavedItemPath(xmlFolderPath, folderName);
                var nextSourcePath = CombineSavedItemPath(sourcePath, folderName);
                if (preserveXmlFolders)
                {
                    plans.Add(new SavedViewpointsImportPlan
                    {
                        Type = "folder",
                        Name = folderName,
                        RelativePath = nextXmlFolderPath,
                        RelativeFolderPath = xmlFolderPath,
                        SourcePath = nextSourcePath,
                    });
                }

                foreach (var child in element.Elements())
                    CollectSavedViewpointImportPlans(child, preserveXmlFolders ? nextXmlFolderPath : xmlFolderPath, nextSourcePath, preserveXmlFolders, plans);
                return;
            }

            if (!IsXmlElement(element, "view"))
                return;

            var name = NormalizeImportedSavedItemName(ReadStringAttribute(element, "name"), "Imported Viewpoint");
            var plan = new SavedViewpointsImportPlan
            {
                Type = "view",
                Name = name,
                RelativeFolderPath = xmlFolderPath,
                RelativePath = CombineSavedItemPath(xmlFolderPath, name),
                SourcePath = CombineSavedItemPath(sourcePath, name),
                Xml = element,
            };

            Viewpoint viewpoint;
            string warning;
            if (TryBuildViewpointFromXml(element, out viewpoint, out warning))
            {
                plan.Viewpoint = viewpoint;
                plan.RedlinesJson = BuildRedlinesJson(element, plan.Warnings);
                AddUnsupportedViewpointXmlWarnings(element, plan.Warnings);
            }
            else
            {
                plan.Warnings.Add(string.IsNullOrWhiteSpace(warning) ? "Skipped viewpoint without a supported camera: " + plan.SourcePath : warning);
            }

            plans.Add(plan);
        }

        private static bool TryBuildViewpointFromXml(XElement viewElement, out Viewpoint viewpoint, out string warning)
        {
            viewpoint = null;
            warning = null;

            var parsed = SavedViewpointCameraXmlParser.Parse(viewElement);
            if (!parsed.Success)
            {
                warning = parsed.Warning;
                return false;
            }

            try
            {
                var result = new Viewpoint
                {
                    Position = new Point3D(
                        parsed.PositionX,
                        parsed.PositionY,
                        parsed.PositionZ),
                    Rotation = new Rotation3D(
                        parsed.RotationA,
                        parsed.RotationB,
                        parsed.RotationC,
                        parsed.RotationD),
                };

                result.Projection = parsed.Projection == SavedViewpointProjectionKind.Orthographic
                    ? ViewpointProjection.Orthographic
                    : ViewpointProjection.Perspective;
                TrySetImportedViewpointDouble(value => result.NearPlaneDistance = value, parsed.NearPlaneDistance, "near");
                TrySetImportedViewpointDouble(value => result.FarPlaneDistance = value, parsed.FarPlaneDistance, "far");
                TrySetImportedViewpointDouble(value => result.AspectRatio = value, parsed.AspectRatio, "aspect");
                TrySetImportedViewpointDouble(value => result.HeightField = value, parsed.HeightField, "height");

                TrySetImportedViewpointDouble(value => result.FocalDistance = value, parsed.FocalDistance, "focal");
                TrySetImportedViewpointDouble(value => result.LinearSpeed = value, parsed.LinearSpeed, "linear");
                TrySetImportedViewpointDouble(value => result.AngularSpeed = value, parsed.AngularSpeed, "angular");

                if (parsed.HasWorldUpVector)
                    result.WorldUpVector = new UnitVector3D(
                        parsed.WorldUpX,
                        parsed.WorldUpY,
                        parsed.WorldUpZ);

                result.RenderStyle = ToNavisworksRenderStyle(parsed.RenderStyle);
                result.Lighting = ToNavisworksLighting(parsed.Lighting);

                if (!string.IsNullOrWhiteSpace(parsed.ViewerAvatar))
                {
                    try
                    {
                        result.ViewerAvatar = parsed.ViewerAvatar;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to apply imported viewpoint viewer avatar: " + ex.Message, "ViewpointsMcp");
                    }
                }

                try
                {
                    if (parsed.ViewerCameraMode == SavedViewpointCameraModeKind.ThirdPerson)
                        result.ViewerCameraMode = CameraMode.ThirdPerson;
                    else if (parsed.ViewerCameraMode == SavedViewpointCameraModeKind.FirstPerson)
                        result.ViewerCameraMode = CameraMode.FirstPerson;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to apply imported viewpoint camera mode: " + ex.Message, "ViewpointsMcp");
                }

                viewpoint = result;
                return true;
            }
            catch (Exception ex)
            {
                warning = "Skipped viewpoint because camera data could not be applied: " + ex.Message;
                return false;
            }
        }

        private static ViewpointRenderStyle ToNavisworksRenderStyle(SavedViewpointRenderStyleKind value)
        {
            if (value == SavedViewpointRenderStyleKind.FullRender)
                return ViewpointRenderStyle.FullRender;
            if (value == SavedViewpointRenderStyleKind.Wireframe)
                return ViewpointRenderStyle.Wireframe;
            if (value == SavedViewpointRenderStyleKind.HiddenLine)
                return ViewpointRenderStyle.HiddenLine;

            return ViewpointRenderStyle.Shaded;
        }

        private static ViewpointLighting ToNavisworksLighting(SavedViewpointLightingKind value)
        {
            if (value == SavedViewpointLightingKind.Headlight)
                return ViewpointLighting.Headlight;
            if (value == SavedViewpointLightingKind.FullLights)
                return ViewpointLighting.FullLights;
            if (value == SavedViewpointLightingKind.None)
                return ViewpointLighting.None;

            return ViewpointLighting.SceneLights;
        }

        private static string BuildRedlinesJson(XElement viewElement, IList<string> warnings)
        {
            var parsed = SavedViewpointRedlineXmlParser.Parse(viewElement);
            foreach (var warning in parsed.Warnings)
                warnings.Add(warning);
            if (parsed.Redlines.Count == 0)
                return null;

            var values = new JArray();
            foreach (var redline in parsed.Redlines)
            {
                var color = redline.IntegerColor
                    ? new JArray((int)redline.Red, (int)redline.Green, (int)redline.Blue)
                    : new JArray(redline.Red, redline.Green, redline.Blue);
                if (string.Equals(redline.Type, RedlineTypeWhitelistHelper.Ellipse, StringComparison.Ordinal))
                {
                    values.Add(new JObject
                    {
                        ["Type"] = "RedlineEllipse",
                        ["Version"] = 1,
                        ["Thickness"] = redline.Thickness,
                        ["Color"] = color,
                        ["MinPoint"] = new JArray(redline.StartX, redline.StartY),
                        ["MaxPoint"] = new JArray(redline.EndX, redline.EndY),
                    });
                    continue;
                }

                values.Add(new JObject
                {
                    ["Type"] = "RedlineLine",
                    ["Version"] = 1,
                    ["Thickness"] = redline.Thickness,
                    ["Color"] = color,
                    ["Start"] = new JArray(redline.StartX, redline.StartY),
                    ["End"] = new JArray(redline.EndX, redline.EndY),
                });
            }

            return new JObject
            {
                ["Type"] = "RedlineCollection",
                ["Version"] = 1,
                ["Values"] = values,
            }.ToString(Formatting.None);
        }

        private static void ApplyImportedRedlines(Document document, SavedViewpoint savedViewpoint, Viewpoint viewpoint, string redlinesJson, IList<string> warnings)
        {
            if (document == null || document.ActiveView == null || document.CurrentViewpoint == null)
            {
                AddUniqueWarning(warnings, "Imported viewpoint camera, but redlines were not applied because there is no active view.");
                return;
            }

            Viewpoint originalViewpoint = null;
            string originalRedlines = null;
            try
            {
                originalViewpoint = document.CurrentViewpoint.CreateCopy();
                originalRedlines = document.ActiveView.GetRedlines();
                document.CurrentViewpoint.CopyFrom(viewpoint);
                RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, redlinesJson, warnings);
                document.SavedViewpoints.ReplaceFromCurrentView(savedViewpoint);
            }
            catch (Exception ex)
            {
                AddUniqueWarning(warnings, "Imported viewpoint camera, but failed to apply redlines: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (originalViewpoint != null)
                        document.CurrentViewpoint.CopyFrom(originalViewpoint);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to restore original viewpoint after redline import: " + ex.Message, "ViewpointsMcp");
                }

                try
                {
                    if (originalRedlines != null && document.ActiveView != null)
                    {
                        // This payload came from the same host view immediately before the import.
                        // Preserve host-supported UI primitives when restoring the user's state;
                        // only external/generated payloads pass through the writable-type whitelist.
                        try
                        {
                            document.ActiveView.SetRedlines(originalRedlines);
                        }
                        catch (Exception ex)
                        {
                            AddUniqueWarning(warnings, "The active view's original redlines could not be restored exactly; unsupported primitives were omitted.");
                            Logger.Error("Exact original redline restore failed; retrying with supported primitives: " + ex.Message, "ViewpointsMcp");
                            RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, originalRedlines, warnings);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to restore original redlines after redline import: " + ex.Message, "ViewpointsMcp");
                }
            }
        }

        private static void AddUnsupportedViewpointXmlWarnings(XElement viewElement, IList<string> warnings)
        {
            var clipPlaneSet = ChildElement(viewElement, "clipplaneset");
            if (clipPlaneSet != null && ReadBoolishAttribute(clipPlaneSet, "enabled"))
                warnings.Add("Clip planes are present and enabled in XML; camera was imported, but clip planes are not restored by saved_viewpoints_import.");

            if (viewElement.Descendants().Any(element => IsXmlElement(element, "hide") || IsXmlElement(element, "override")))
                warnings.Add("Visibility/material override XML was detected; camera/redlines were imported, but object overrides are not restored by saved_viewpoints_import.");
        }

        private static void AddImportPreview(SavedViewpointsImportResponse response, SavedViewpointsImportPlan plan, string targetPath, int previewLimit, bool imported)
        {
            if (response.Items.Count >= previewLimit)
            {
                response.Truncated = true;
                return;
            }

            response.Items.Add(new SavedViewpointsImportItem
            {
                Type = plan.Type,
                Name = plan.Name,
                SourcePath = plan.SourcePath,
                TargetPath = targetPath,
                Imported = imported,
                Warning = plan.Warnings.FirstOrDefault(),
            });
        }

        private static SavedViewpoint FindLastSavedViewpoint(GroupItem folder, string name)
        {
            if (folder == null)
                return null;

            return folder.Children
                .OfType<SavedViewpoint>()
                .LastOrDefault(item => string.Equals(item.DisplayName ?? string.Empty, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static string CombineSavedItemPath(string left, string right)
        {
            left = NormalizeFolderPath(left);
            right = NormalizeFolderPath(right);
            if (string.IsNullOrWhiteSpace(left))
                return right;
            if (string.IsNullOrWhiteSpace(right))
                return left;

            return left + "/" + right;
        }

        private static string NormalizeImportedSavedItemName(string value, string fallback)
        {
            value = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static bool IsXmlElement(XElement element, string localName)
        {
            return element != null && string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        }

        private static XElement ChildElement(XElement element, string localName)
        {
            return element == null
                ? null
                : element.Elements().FirstOrDefault(child => IsXmlElement(child, localName));
        }

        private static string ReadStringAttribute(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static void TrySetImportedViewpointDouble(Action<double> setter, double? value, string attributeName)
        {
            if (!value.HasValue)
                return;

            try
            {
                setter(value.Value);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to apply imported viewpoint numeric attribute '" + attributeName + "': " + ex.Message, "ViewpointsMcp");
            }
        }

        private static bool ReadBoolishAttribute(XElement element, string name)
        {
            var value = ReadStringAttribute(element, name).Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int ClampPreviewLimit(int? value, int fallback, int max)
        {
            var result = value.GetValueOrDefault(fallback);
            if (result < 1)
                return 1;
            if (result > max)
                return max;
            return result;
        }

        private static void AddUniqueWarning(IList<string> warnings, string warning)
        {
            if (warnings == null || string.IsNullOrWhiteSpace(warning))
                return;
            if (!warnings.Contains(warning))
                warnings.Add(warning);
        }

        private sealed class SavedViewpointsImportPlan
        {
            public string Type { get; set; }
            public string Name { get; set; }
            public string RelativePath { get; set; }
            public string RelativeFolderPath { get; set; }
            public string SourcePath { get; set; }
            public XElement Xml { get; set; }
            public Viewpoint Viewpoint { get; set; }
            public string RedlinesJson { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }
    }
}
