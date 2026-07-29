using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel
    {
        private void CreateClashOrbitGif()
        {
            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear || doc.CurrentViewpoint == null)
            {
                SetGlobalStatusResource("Panel_Common_NoActiveDocument", Brushes.Orange);
                return;
            }

            var row = _clashGrid?.SelectedItem;
            if (row == null)
            {
                SetGlobalStatusResource("Panel_Clash_SelectResult", Brushes.Orange);
                return;
            }

            var results = GetClashResultsFromRow(row);
            if (results.Count == 0)
            {
                SetGlobalStatusResource("Panel_Clash_InvalidResult", Brushes.Orange);
                return;
            }

            PreviewSelectedClash();

            var center = _clashMgr.LastClashCenter ?? results.First().Center;
            if (center == null)
            {
                SetGlobalStatusResource("Panel_Clash_Gif_NoPoint", Brushes.Orange);
                return;
            }

            string rowName = null;
            try
            {
                dynamic dyn = row;
                rowName = dyn.Name as string;
            }
            catch
            {
            }

            var defaultGifName = NormalizeSavedItemName(
                rowName ?? results.First().DisplayName,
                PanelUi("Panel_Clash_Gif_DefaultName")) + ".gif";
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = PanelUi("Panel_Clash_Gif_SaveTitle"),
                Filter = PanelUi("Panel_Clash_Gif_FileFilter"),
                FileName = defaultGifName,
                AddExtension = true,
                DefaultExt = ".gif",
                OverwritePrompt = true
            };

            if (saveDialog.ShowDialog() != true)
            {
                SetGlobalStatusResource("Panel_Clash_Gif_Cancelled", Brushes.Orange);
                return;
            }

            var gifPath = saveDialog.FileName;

            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Create Clash orbit GIF");
            var baseViewpoint = doc.CurrentViewpoint.CreateCopy();
            var outputDir = Path.Combine(Path.GetTempPath(), "NavisHelper-ClashOrbitGif-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            var framesDir = Path.Combine(outputDir, "frames");
            Directory.CreateDirectory(framesDir);
            var framePaths = new List<string>();
            Progress progress = null;
            var gifCreated = false;

            try
            {
                progress = NwApplication.BeginProgress(PanelUi("Panel_Clash_Gif_Progress"));
                var basePosition = baseViewpoint.Position;
                var relative = new[] { basePosition.X - center.X, basePosition.Y - center.Y, basePosition.Z - center.Z };
                var horizontalRadius = Math.Sqrt(relative[0] * relative[0] + relative[1] * relative[1]);
                if (horizontalRadius < 1e-6)
                {
                    var fallbackRadius = GetClashOrbitFallbackRadius(_clashMgr.LastExpandedBox);
                    relative = new[] { fallbackRadius, -fallbackRadius, fallbackRadius * 0.6 };
                }

                const int frameCount = 24;
                const int stepDegrees = 15;
                for (var i = 0; i < frameCount; i++)
                {
                    if (progress.IsCanceled)
                        break;

                    var radians = stepDegrees * i * Math.PI / 180.0;
                    var rotated = RotateAroundZ(relative, radians);
                    var frameViewpoint = baseViewpoint.CreateCopy();
                    var framePosition = new Point3D(center.X + rotated[0], center.Y + rotated[1], center.Z + rotated[2]);
                    frameViewpoint.Position = framePosition;
                    ResetViewpointOffsets(frameViewpoint);
                    frameViewpoint.PointAt(center);
                    try { frameViewpoint.AlignUp(new Vector3D(0, 0, 1)); } catch { }
                    frameViewpoint.FocalDistance = Distance(framePosition, center);
                    doc.CurrentViewpoint.CopyFrom(frameViewpoint);
                    try { doc.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }
                    PumpDispatcherOnce();
                    Thread.Sleep(75);
                    PumpDispatcherOnce();

                    var framePath = Path.Combine(framesDir, "frame_" + i.ToString("000", CultureInfo.InvariantCulture) + ".bmp");
                    string warning;
                    if (!TryCaptureCurrentViewImage(framePath, out warning))
                        throw new InvalidOperationException(
                            UiLocalizationService.Current.Format(
                                "Panel_Clash_Gif_FrameFailed_Format",
                                i + 1,
                                warning));

                    framePaths.Add(framePath);
                    progress.Update((double)(i + 1) / frameCount);
                }

                if (framePaths.Count == 0)
                {
                    SetGlobalStatusResource("Panel_Clash_Gif_Cancelled", Brushes.Orange);
                    return;
                }

                WriteAnimatedGif(framePaths, gifPath, 8);
                gifCreated = true;
                doc.CurrentViewpoint.CopyFrom(baseViewpoint);
                SetGlobalStatusResource("Panel_Clash_Gif_Created_Format", Brushes.DarkGreen, gifPath);
                try { Process.Start("explorer.exe", "/select,\"" + gifPath + "\""); } catch { }
            }
            finally
            {
                if (progress != null)
                {
                    try { NwApplication.EndProgress(); } catch { }
                }
                try { doc.CurrentViewpoint.CopyFrom(baseViewpoint); } catch { }
                try
                {
                    if (gifCreated)
                    {
                        if (Directory.Exists(framesDir))
                            Directory.Delete(framesDir, true);
                        if (Directory.Exists(outputDir))
                            Directory.Delete(outputDir, true);
                    }
                    else if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, true);
                    }
                }
                catch
                {
                }
                interactiveBusy.Dispose();
            }
        }

        private static void PumpDispatcherOnce()
        {
            try
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new DispatcherOperationCallback(state =>
                    {
                        ((DispatcherFrame)state).Continue = false;
                        return null;
                    }),
                    frame);
                Dispatcher.PushFrame(frame);
            }
            catch
            {
            }
        }

        private static double GetClashOrbitFallbackRadius(BoundingBox3D box)
        {
            if (box == null)
                return 10.0;

            var dx = Math.Abs(box.Max.X - box.Min.X);
            var dy = Math.Abs(box.Max.Y - box.Min.Y);
            var dz = Math.Abs(box.Max.Z - box.Min.Z);
            return Math.Max(Math.Max(dx, dy), dz) * 1.5 + 1.0;
        }

        private static double[] RotateAroundZ(double[] vector, double radians)
        {
            var c = Math.Cos(radians);
            var s = Math.Sin(radians);
            return new[]
            {
                vector[0] * c - vector[1] * s,
                vector[0] * s + vector[1] * c,
                vector[2],
            };
        }

        private static double Distance(Point3D a, Point3D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void ResetViewpointOffsets(Viewpoint viewpoint)
        {
            try { viewpoint.RightOffsetAtFocalDistance = 0; } catch { }
            try { viewpoint.UpOffsetAtFocalDistance = 0; } catch { }
            try { viewpoint.RightOffsetFactor = 0; } catch { }
            try { viewpoint.UpOffsetFactor = 0; } catch { }
        }

        private static bool TryCaptureCurrentViewImage(string outputPath, out string warning)
        {
            warning = string.Empty;
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

                state.DriveIOPlugin("lcodpimage", outputPath, options);
                if (!File.Exists(outputPath))
                {
                    warning = "Image exporter completed but did not create a file.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                return false;
            }
            finally
            {
                ReleaseComObjectIfNeeded(propertiesObject, "image export options collection");
                ReleaseComObjectIfNeeded(optionsObject, "image export options");
            }
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
                Logger.Error("Failed to release COM object " + (context ?? string.Empty) + ": " + ex.Message, "ClashUI");
            }
        }

        private static void WriteAnimatedGif(IEnumerable<string> framePaths, string outputPath, ushort frameDelay)
        {
            var encoder = new GifBitmapEncoder();
            foreach (var framePath in framePaths)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(framePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                BitmapFrame frame;
                try
                {
                    var metadata = new BitmapMetadata("gif");
                    metadata.SetQuery("/grctlext/Delay", frameDelay);
                    metadata.SetQuery("/grctlext/Disposal", (byte)2);
                    frame = BitmapFrame.Create(image, null, metadata, null);
                }
                catch
                {
                    frame = BitmapFrame.Create(image);
                }

                encoder.Frames.Add(frame);
            }

            using (var stream = File.Create(outputPath))
                encoder.Save(stream);
        }
    }
}
