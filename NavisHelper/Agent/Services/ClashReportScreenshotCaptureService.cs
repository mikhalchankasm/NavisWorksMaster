using System;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.ComApi;
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
            try
            {
                var state = ComApiBridge.State;
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

                return true;
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
                ReleaseComObjectIfNeeded(propertiesObject, "image export options collection");
                ReleaseComObjectIfNeeded(optionsObject, "image export options");
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

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
