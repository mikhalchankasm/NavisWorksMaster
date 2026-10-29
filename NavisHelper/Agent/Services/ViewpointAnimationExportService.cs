using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NavisHelper"),
            };
            if (ViewpointAnimationExportRules.IsForbiddenOutputRoot(outputDirectory, roots))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ViewpointAnimationExportRules.ProtectedFolderMessage);
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
            if (request.Apply == true)
                return Export(animation, request, response);
            response.Warnings.Add("Dry-run: nothing was exported; pass apply=true to export.");
            SnapshotStoredOptions("lcodpanim", response);
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

        private static ViewpointAnimationExportResponse Export(SavedViewpointAnimation animation,
            ViewpointAnimationExportRequest request, ViewpointAnimationExportResponse response)
        {
            if (response.OutputDirectoryExists && request.Overwrite != true
                && ViewpointAnimationExportRules.HasExistingOutput(Directory.EnumerateFiles(response.OutputDirectory), animation.DisplayName, response.Format))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ViewpointAnimationExportRules.ExistingOutputMessage);
            var directoryExisted = response.OutputDirectoryExists;
            Directory.CreateDirectory(response.OutputDirectory);
            response.OutputDirectoryExists = true;
            response.OutputFile = ViewpointAnimationExportRules.OutputFile(response.OutputDirectory, animation.DisplayName, response.Format);
            // Files already in the folder are reported only when the export rewrote them.
            var before = new DirectoryInfo(response.OutputDirectory).EnumerateFiles()
                .ToDictionary(file => file.Name, file => file.LastWriteTimeUtc.Ticks + ":" + file.Length, StringComparer.OrdinalIgnoreCase);
            var state = ComApiBridge.State;
            InwOpAnimView view = null;
            InwOpAnimView previous = null;
            InwOaPropertyVec vector = null;
            var exporter = ViewpointAnimationExportRules.NormalizeExporter(request.Exporter);
            Dictionary<string, object> storedOptions = null;
            try
            {
                var chain = new List<SavedItem>();
                for (SavedItem item = animation; item.Parent != null; item = item.Parent) chain.Add(item);
                chain.Reverse();
                object views = null;
                try
                {
                    views = state.SavedViews();
                    view = FindComAnimation((IEnumerable)views, chain, 0);
                }
                finally { Release(views); }
                if (view == null)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "The animation was not found in the COM saved views.");
                previous = state.CurrentAnimation;
                response.CurrentAnimation = previous == null ? null : previous.name;
                try
                {
                    storedOptions = SnapshotStoredOptions(exporter, response);
                    state.CurrentAnimation = view;
                    vector = state.GetIOPluginOptions(exporter);
                    ApplyOptions(vector, request, response);
                    var stopwatch = Stopwatch.StartNew();
                    try { response.ExportStatus = state.DriveIOPlugin(exporter, response.OutputFile, vector).ToString(); }
                    finally
                    {
                        stopwatch.Stop();
                        response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    }
                    var optionsAfter = new ExporterPluginOptions { Plugin = exporter };
                    try { ReadOptions(vector, "", 1, optionsAfter); }
                    catch (Exception ex) { response.Warnings.Add("Could not read exporter options after export: " + ex.Message); }
                    response.OptionsAfter = optionsAfter.Options;
                    if (optionsAfter.Truncated) response.Warnings.Add("Exporter options after export were truncated (200 options or three levels).");
                }
                finally
                {
                    RestoreStoredOptions(exporter, storedOptions, response);
                    try { state.CurrentAnimation = previous; }
                    catch (Exception ex) { Logger.Warn("Restoring the current animation failed; the readback decides: " + ex.Message, "ViewpointAnimationExport"); }
                    try
                    {
                        response.CurrentAnimationAfter = ReadCurrentAnimationName();
                        if ((response.CurrentAnimationAfter ?? "") != (response.CurrentAnimation ?? "") && previous != null)
                        {
                            InwOpAnimView copy = null;
                            try
                            {
                                copy = (InwOpAnimView)previous.Copy();
                                state.CurrentAnimation = copy;
                            }
                            catch (Exception ex) { Logger.Warn("Restoring a copy of the current animation failed; the readback decides: " + ex.Message, "ViewpointAnimationExport"); }
                            finally { Release(copy); }
                            response.CurrentAnimationAfter = ReadCurrentAnimationName();
                        }
                        if ((response.CurrentAnimationAfter ?? "") != (response.CurrentAnimation ?? ""))
                            response.Warnings.Add(ViewpointAnimationExportRules.RestoreWarning(response.CurrentAnimationAfter));
                    }
                    catch (Exception ex)
                    {
                        response.Warnings.Add("Could not verify restoration of the previous current animation: " + ex.Message);
                    }
                }
            }
            finally
            {
                Release(vector);
                Release(previous);
                Release(view);
            }
            var prefix = ViewpointAnimationExportRules.SafeFileName(animation.DisplayName);
            foreach (var file in new DirectoryInfo(response.OutputDirectory).EnumerateFiles()
                .Where(file => file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && (!before.TryGetValue(file.Name, out var stamp) || stamp != file.LastWriteTimeUtc.Ticks + ":" + file.Length))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ThenBy(file => file.Name, StringComparer.Ordinal))
            {
                string kind;
                try
                {
                    using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new BinaryReader(stream))
                        kind = ViewpointAnimationExportRules.DetectKind(reader.ReadBytes(12));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    kind = "unreadable";
                }
                response.Files.Add(new ExportedFile { Name = file.Name, Bytes = file.Length, Kind = kind });
            }
            response.Applied = true;
            response.FileCount = response.Files.Count;
            response.TotalBytes = response.Files.Sum(file => file.Bytes);
            response.FormatMatches = response.FileCount > 0 && response.Files.All(file => file.Kind == response.Format);
            response.Exported = response.ExportStatus == "eExport_OK" && response.FileCount > 0;
            try
            {
                if (response.FileCount == 0 && !directoryExisted && !Directory.EnumerateFileSystemEntries(response.OutputDirectory).Any())
                {
                    Directory.Delete(response.OutputDirectory);
                    response.OutputDirectoryExists = false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                response.Warnings.Add("Could not remove the empty output directory created by this call: " + ex.Message);
            }
            response.Message = response.Exported ? "Exported the animation." : ViewpointAnimationExportRules.FailedExportMessage(response.ExportStatus);
            if (!response.Exported && response.FileCount > 0)
                response.Warnings.Add("The exporter wrote " + response.FileCount + " file(s) before returning " + response.ExportStatus + "; they may be incomplete.");
            return response;

            string ReadCurrentAnimationName()
            {
                InwOpAnimView current = null;
                try
                {
                    current = state.CurrentAnimation;
                    return current == null ? null : current.name;
                }
                finally { Release(current); }
            }
        }

        private static InwOpAnimView FindComAnimation(IEnumerable views, IList<SavedItem> chain, int depth)
        {
            if (depth >= chain.Count) return null;
            var expected = chain[depth];
            var index = expected.Parent.Children.IndexOf(expected);
            var enumerator = views.GetEnumerator();
            try
            {
                var position = 0;
                while (enumerator.MoveNext())
                {
                    var item = (InwOpSavedView)enumerator.Current;
                    var retained = false;
                    try
                    {
                        if (position++ != index) continue;
                        if (!string.Equals(item.name, expected.DisplayName, StringComparison.Ordinal)) return null;
                        if (depth == chain.Count - 1)
                        {
                            var animation = item.Type == nwESavedViewType.eSavedViewType_Anim ? item as InwOpAnimView : null;
                            retained = animation != null;
                            return animation;
                        }
                        if (item.Type != nwESavedViewType.eSavedViewType_Folder) return null;
                        object children = null;
                        try
                        {
                            children = ((InwOpFolderView)item).SavedViews();
                            return FindComAnimation((IEnumerable)children, chain, depth + 1);
                        }
                        finally { Release(children); }
                    }
                    finally { if (!retained) Release(item); }
                }
                return null;
            }
            finally { Release(enumerator); }
        }

        private static void ApplyOptions(InwOaPropertyVec vector, ViewpointAnimationExportRequest request,
            ViewpointAnimationExportResponse response)
        {
            var assignments = ViewpointAnimationExportRules.MergedAssignments(request);
            var properties = vector.Properties();
            try
            {
                foreach (var pair in assignments)
                {
                    InwOaProperty property = null;
                    IEnumerator enumerator = null;
                    try
                    {
                        enumerator = ((IEnumerable)properties).GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            var candidate = (InwOaProperty)enumerator.Current;
                            try
                            {
                                if (candidate.name == pair.Key) { property = candidate; break; }
                            }
                            finally { if (property == null) Release(candidate); }
                        }
                        var added = property == null;
                        if (added)
                        {
                            property = (InwOaProperty)ComApiBridge.State.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, null, null);
                            property.name = pair.Key;
                        }
                        property.value = pair.Value;
                        if (added) properties.Add(property);
                        string type;
                        response.AppliedOptions.Add((added ? "+" : "") + pair.Key + "=" + ViewpointAnimationExportRules.FormatOptionValue(pair.Value, out type));
                    }
                    finally
                    {
                        Release(enumerator);
                        Release(property);
                    }
                }
            }
            finally { Release(properties); }
        }

        private static Dictionary<string, object> SnapshotStoredOptions(string exporter, ViewpointAnimationExportResponse response)
        {
            try
            {
                var snapshot = ReadStoredOptions(exporter);
                response.StoredOptions = snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + ViewpointAnimationExportRules.FormatOptionValue(pair.Value, out _)).ToList();
                return snapshot;
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not read the Export Animation dialog store; option restoration will be skipped: " + ex.Message);
                return null;
            }
        }

        private static Dictionary<string, object> ReadStoredOptions(string exporter)
        {
            InwOaPropertyVec vector = null;
            object properties = null;
            IEnumerator enumerator = null;
            try
            {
                vector = ComApiBridge.State.get_PluginOptions(exporter);
                if (vector == null) throw new InvalidOperationException("The exporter returned no stored options.");
                properties = vector.Properties();
                enumerator = ((IEnumerable)properties).GetEnumerator();
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                while (enumerator.MoveNext())
                {
                    InwOaProperty property = null;
                    object value = null;
                    try
                    {
                        property = (InwOaProperty)enumerator.Current;
                        value = property.value;
                        if (value != null && Marshal.IsComObject(value))
                            throw new InvalidOperationException("Stored exporter options must be scalar values.");
                        result.Add(property.name, value);
                    }
                    finally
                    {
                        Release(value);
                        Release(property);
                    }
                }
                return result;
            }
            finally
            {
                Release(enumerator);
                Release(properties);
                Release(vector);
            }
        }

        private static void RestoreStoredOptions(string exporter, IDictionary<string, object> snapshot,
            ViewpointAnimationExportResponse response)
        {
            if (snapshot == null) return;
            try
            {
                var current = ReadStoredOptions(exporter);
                var putBack = new List<string>();
                foreach (var key in ViewpointAnimationExportRules.ChangedOptions(snapshot, current))
                {
                    try
                    {
                        RestoreStoredOption(exporter, key, snapshot[key]);
                        putBack.Add(key);
                    }
                    catch (Exception ex) { Logger.Warn("Restoring stored exporter option '" + key + "' failed; the readback decides: " + ex.Message, "ViewpointAnimationExport"); }
                }
                var after = ReadStoredOptions(exporter);
                var remaining = ViewpointAnimationExportRules.ChangedOptions(snapshot, after);
                response.OptionsRestored = remaining.Count == 0;
                response.RestoredOptions = putBack.Where(key => !remaining.Contains(key))
                    .Select(key => key + "=" + ViewpointAnimationExportRules.FormatOptionValue(after[key], out _)).ToList();
                var warning = ViewpointAnimationExportRules.OptionsRestoreWarning(remaining, after);
                if (warning != null) response.Warnings.Add(warning);
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not verify restoration of the Export Animation dialog store: " + ex.Message);
            }
        }

        private static void RestoreStoredOption(string exporter, string key, object value)
        {
            InwOaPropertyVec vector = null;
            InwOaProperty property = null;
            IEnumerator enumerator = null;
            try
            {
                // Each put starts from a fresh vector so one rejected key cannot poison later puts.
                vector = ComApiBridge.State.get_PluginOptions(exporter);
                if (vector == null) throw new InvalidOperationException("The exporter returned no stored options.");
                var properties = vector.Properties();
                try
                {
                    enumerator = ((IEnumerable)properties).GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var candidate = (InwOaProperty)enumerator.Current;
                        try
                        {
                            if (candidate.name == key) { property = candidate; break; }
                        }
                        finally { if (property == null) Release(candidate); }
                    }
                    var added = property == null;
                    if (added)
                    {
                        property = (InwOaProperty)ComApiBridge.State.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, null, null);
                        property.name = key;
                    }
                    property.value = value;
                    if (added) properties.Add(property);
                    ComApiBridge.State.set_PluginOptions(exporter, vector);
                }
                finally { Release(properties); }
            }
            finally
            {
                Release(enumerator);
                Release(property);
                Release(vector);
            }
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
