using System;
using System.IO;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class CurrentViewScreenshotService
    {
        public static bool TrySave(string outputPath, out string warning)
        {
            warning = string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                warning = "Путь для сохранения изображения не задан.";
                return false;
            }

            var extension = Path.GetExtension(outputPath) ?? string.Empty;
            var format = extension.TrimStart('.').ToLowerInvariant();
            if (string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase))
                format = "jpg";
            if (format != "png" && format != "jpg" && format != "bmp")
            {
                warning = "Поддерживаются изображения PNG, JPG и BMP.";
                return false;
            }

            string optionError;
            var options = ClashReportOptionHelper.NormalizeScreenshotOptions(
                "source",
                format,
                0,
                0,
                95,
                out optionError);
            if (options == null)
            {
                warning = optionError;
                return false;
            }

            return ClashReportScreenshotCaptureService.TryCaptureCurrentViewImage(
                outputPath,
                options,
                out warning);
        }
    }
}
