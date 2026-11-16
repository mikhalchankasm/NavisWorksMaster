using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashReportScreenshotCaptureService
    {
        public static bool TryCaptureCurrentViewImage(string outputPath, ClashReportScreenshotOptions exportOptions, out string warning)
        {
            warning = string.Empty;
            exportOptions = exportOptions ?? CreateDefaultScreenshotOptions();
            var capturePath = outputPath;
            if (exportOptions.RequiresPostProcess)
                capturePath = outputPath + ".source.bmp";

            object optionsObject = null;
            object propertiesObject = null;
            Dictionary<string, object> storedOptions = null;
            var captured = false;
            try
            {
                var state = ComApiBridge.State;
                storedOptions = ReadStoredImageOptions();
                var view = Autodesk.Navisworks.Api.Application.ActiveDocument?.ActiveView;
                int renderWidth;
                int renderHeight;
                ClashReportOptionHelper.ResolveRenderSize(exportOptions, view?.Width ?? 0, view?.Height ?? 0, out renderWidth, out renderHeight);
                var options = state.GetIOPluginOptions("lcodpimage");
                optionsObject = options;
                var properties = options.Properties();
                propertiesObject = properties;
                foreach (Autodesk.Navisworks.Api.Interop.ComApi.InwOaProperty opt in properties)
                {
                    try
                    {
                        if (string.Equals(opt.name, "export.image.format", StringComparison.OrdinalIgnoreCase))
                            opt.value = "lcodpodvbmp";
                        else if (renderWidth > 0 && renderHeight > 0)
                        {
                            if (string.Equals(opt.name, "export.image.width", StringComparison.OrdinalIgnoreCase))
                                opt.value = renderWidth;
                            else if (string.Equals(opt.name, "export.image.height", StringComparison.OrdinalIgnoreCase))
                                opt.value = renderHeight;
                        }
                    }
                    finally
                    {
                        ReleaseComObjectIfNeeded(opt, "image export option property");
                    }
                }

                state.DriveIOPlugin("lcodpimage", capturePath, options);
                if (!File.Exists(capturePath))
                {
                    warning = "Image exporter completed but did not create a file.";
                    return false;
                }

                if (exportOptions.RequiresPostProcess)
                {
                    ConvertCapturedImage(capturePath, outputPath, exportOptions);
                    if (!File.Exists(outputPath))
                    {
                        warning = "Image conversion completed but did not create a file.";
                        return false;
                    }

                    TryDeleteFile(capturePath);
                }

                captured = true;
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                if (!string.Equals(capturePath, outputPath, StringComparison.OrdinalIgnoreCase))
                    TryDeleteFile(capturePath);
                return false;
            }
            finally
            {
                if (storedOptions != null)
                {
                    try { RestoreStoredImageOptions(storedOptions); }
                    catch (Exception ex)
                    {
                        // The image is already written; a dialog-store restore failure is reported, not fatal.
                        warning = (string.IsNullOrEmpty(warning) ? string.Empty : warning + " ") +
                            "Could not restore Image Export dialog options: " + ex.Message;
                        Logger.Warn(warning, "ClashMcp");
                    }
                }
                ReleaseComObjectIfNeeded(propertiesObject, "image export options collection");
                ReleaseComObjectIfNeeded(optionsObject, "image export options");
            }
            return captured;
        }

        private static Dictionary<string, object> ReadStoredImageOptions()
        {
            InwOaPropertyVec vector = null;
            object properties = null;
            IEnumerator enumerator = null;
            try
            {
                vector = ComApiBridge.State.get_PluginOptions("lcodpimage");
                if (vector == null)
                    throw new InvalidOperationException("The image exporter returned no stored options.");
                properties = vector.Properties();
                enumerator = ((IEnumerable)properties).GetEnumerator();
                var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
                while (enumerator.MoveNext())
                {
                    var property = (InwOaProperty)enumerator.Current;
                    object value = null;
                    try
                    {
                        value = property.value;
                        if (value != null && Marshal.IsComObject(value))
                            throw new InvalidOperationException("Stored image exporter options must be scalar values.");
                        snapshot.Add(property.name, value);
                    }
                    finally
                    {
                        ReleaseComObjectIfNeeded(value, "stored image option value");
                        ReleaseComObjectIfNeeded(property, "stored image option property");
                    }
                }
                return snapshot;
            }
            finally
            {
                ReleaseComObjectIfNeeded(enumerator, "stored image options enumerator");
                ReleaseComObjectIfNeeded(properties, "stored image options collection");
                ReleaseComObjectIfNeeded(vector, "stored image options");
            }
        }

        private static void RestoreStoredImageOptions(Dictionary<string, object> snapshot)
        {
            var current = ReadStoredImageOptions();
            foreach (var pair in snapshot)
            {
                object value;
                if (current.TryGetValue(pair.Key, out value) && Equals(pair.Value, value))
                    continue;
                try { RestoreStoredImageOption(pair.Key, pair.Value); }
                catch (Exception ex)
                {
                    // Continue restoring other keys; readback below decides whether restoration succeeded.
                    Logger.Warn("Restoring image exporter option '" + pair.Key + "' failed: " + ex.Message, "ClashMcp");
                }
            }
            var after = ReadStoredImageOptions();
            if (after.Count != snapshot.Count || snapshot.Any(pair =>
                !after.ContainsKey(pair.Key) || !Equals(after[pair.Key], pair.Value)))
                throw new InvalidOperationException("Stored image exporter options differ from the pre-capture snapshot.");
        }

        private static void RestoreStoredImageOption(string key, object value)
        {
            InwOaPropertyVec vector = null;
            object properties = null;
            IEnumerator enumerator = null;
            InwOaProperty property = null;
            try
            {
                // Start each put with a fresh vector so a rejected key cannot affect subsequent puts.
                vector = ComApiBridge.State.get_PluginOptions("lcodpimage");
                if (vector == null)
                    throw new InvalidOperationException("The image exporter returned no stored options.");
                var optionProperties = vector.Properties();
                properties = optionProperties;
                enumerator = ((IEnumerable)properties).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var candidate = (InwOaProperty)enumerator.Current;
                    try
                    {
                        if (candidate.name == key) { property = candidate; break; }
                    }
                    finally
                    {
                        if (property == null) ReleaseComObjectIfNeeded(candidate, "stored image option property");
                    }
                }
                var added = property == null;
                if (added)
                {
                    property = (InwOaProperty)ComApiBridge.State.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, null, null);
                    property.name = key;
                }
                property.value = value;
                if (added) optionProperties.Add(property);
                ComApiBridge.State.set_PluginOptions("lcodpimage", vector);
            }
            finally
            {
                ReleaseComObjectIfNeeded(enumerator, "stored image options enumerator");
                ReleaseComObjectIfNeeded(property, "stored image option property");
                ReleaseComObjectIfNeeded(properties, "stored image options collection");
                ReleaseComObjectIfNeeded(vector, "stored image options");
            }
        }

        private static ClashReportScreenshotOptions CreateDefaultScreenshotOptions()
        {
            string errorMessage;
            return ClashReportOptionHelper.NormalizeScreenshotOptions(null, null, null, null, null, out errorMessage);
        }

        private static void ReleaseComObjectIfNeeded(object value, string context)
        {
            if (value == null)
                return;

            try
            {
                if (Marshal.IsComObject(value))
                    Marshal.ReleaseComObject(value);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to release COM object " + (context ?? string.Empty) + ": " + ex.Message, "ClashMcp");
            }
        }

        private static void ConvertCapturedImage(string sourcePath, string outputPath, ClashReportScreenshotOptions exportOptions)
        {
            using (var source = new System.Drawing.Bitmap(sourcePath))
            {
                int targetWidth;
                int targetHeight;
                ClashReportOptionHelper.CalculateImageTargetSize(source.Width, source.Height, exportOptions.MaxWidth, exportOptions.MaxHeight, out targetWidth, out targetHeight);

                if (targetWidth == source.Width && targetHeight == source.Height)
                {
                    SaveImage(source, outputPath, exportOptions);
                    return;
                }

                using (var resized = new System.Drawing.Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb))
                {
                    resized.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                    using (var graphics = System.Drawing.Graphics.FromImage(resized))
                    {
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
                    }

                    SaveImage(resized, outputPath, exportOptions);
                }
            }
        }

        private static void SaveImage(System.Drawing.Image image, string outputPath, ClashReportScreenshotOptions exportOptions)
        {
            if (string.Equals(exportOptions.Format, "jpg", StringComparison.OrdinalIgnoreCase))
            {
                var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => string.Equals(x.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
                if (codec == null)
                {
                    image.Save(outputPath, ImageFormat.Jpeg);
                    return;
                }

                using (var encoderParameters = new EncoderParameters(1))
                {
                    encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)exportOptions.JpegQuality);
                    image.Save(outputPath, codec, encoderParameters);
                }
                return;
            }

            if (string.Equals(exportOptions.Format, "png", StringComparison.OrdinalIgnoreCase))
            {
                image.Save(outputPath, ImageFormat.Png);
                return;
            }

            image.Save(outputPath, ImageFormat.Bmp);
        }

        private static int _screenshotCleanupFailureLogged;

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                // Log only the first failure per process; later failures are not logged.
                if (Interlocked.Exchange(ref _screenshotCleanupFailureLogged, 1) == 0)
                    Logger.Warn("Could not remove temporary clash screenshot: " + ex.Message, "ClashMcp");
            }
        }
    }
}
