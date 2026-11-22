using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.Core
{
    public static class SectionBoxHelper
    {
        /// <summary>Section box: Mode_Box + FIT_SELECTION.</summary>
        public static void EnableAndFitSelection()
        {
            var exec = GetExecMethod();
            var toolbar = GetToolbarValue();
            if (exec == null || toolbar == null) return;

            exec.Invoke(null, new object[] { "RoamerGUI_OM_SECTION_Mode_Box", toolbar });
            exec.Invoke(null, new object[] { "RoamerGUI_OM_SECTION_FIT_SELECTION", toolbar });
        }

        /// <summary>
        /// Section box по BoundingBox3D напрямую через ClippingPlanes API.
        /// Не использует FIT_SELECTION — задаёт box целиком программно.
        /// </summary>
        public static void SetSectionBox(BoundingBox3D box)
        {
            if (box == null) return;
            var view = Application.ActiveDocument?.ActiveView;
            if (view == null) return;

            var c = CultureInfo.InvariantCulture;

            // Пробуем сохранить текущий rotation если есть
            string rotation = "0,0,0";
            try
            {
                string current = view.GetClippingPlanes();
                if (!string.IsNullOrEmpty(current))
                {
                    int rotIdx = current.IndexOf("\"Rotation\"", StringComparison.Ordinal);
                    if (rotIdx >= 0)
                    {
                        int rotStart = current.IndexOf("[", rotIdx, StringComparison.Ordinal);
                        if (rotStart >= 0)
                        {
                            int rotEnd = current.IndexOf("]", rotStart, StringComparison.Ordinal);
                            if (rotEnd > rotStart)
                                rotation = current.Substring(rotStart + 1, rotEnd - rotStart - 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read current section-box rotation: " + ex.Message, "SectionBox");
            }

            string json = string.Format(c,
                "{{\"Type\":\"ClipPlaneSet\",\"Version\":1," +
                "\"OrientedBox\":{{\"Type\":\"OrientedBox3D\",\"Version\":1," +
                "\"Box\":[[{0},{1},{2}],[{3},{4},{5}]]," +
                "\"Rotation\":[{6}]}}," +
                "\"Enabled\":true}}",
                box.Min.X, box.Min.Y, box.Min.Z,
                box.Max.X, box.Max.Y, box.Max.Z,
                rotation);

            ApplyClippingPlanes(json);
        }

        /// <summary>
        /// Section box: FIT_SELECTION, затем расширение через ClippingPlanes API.
        /// </summary>
        public static void EnableFitAndExpand(double offsetMm)
        {
            EnableAndFitSelection();
            if (offsetMm <= 0) return;

            try
            {
                ExpandViaClippingPlanes(offsetMm);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to expand section box via clipping planes: " + ex.Message, "SectionBox");
            }
        }

        /// <summary>Идемпотентное включение section.</summary>
        public static void Enable()
        {
            // FIT_SELECTION неявно включает section в режиме box
            var exec = GetExecMethod();
            var toolbar = GetToolbarValue();
            if (exec == null || toolbar == null) return;
            exec.Invoke(null, new object[] { "RoamerGUI_OM_SECTION_Mode_Box", toolbar });
        }

        /// <summary>Идемпотентное выключение section через ClippingPlanes.</summary>
        public static void Disable()
        {
            try
            {
                var view = Application.ActiveDocument?.ActiveView;
                if (view == null) return;
                string json = view.GetClippingPlanes();
                if (string.IsNullOrEmpty(json)) return;

                JObject root;
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);

                if (root["Enabled"]?.Type != JTokenType.Boolean || !root["Enabled"].Value<bool>())
                    return;

                root["Enabled"] = false;
                ApplyClippingPlanes(root.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                // Не используем Toggle() как fallback — это может включить section
                // Просто игнорируем ошибку
                Logger.Error("Failed to disable section box via clipping planes: " + ex.Message, "SectionBox");
            }
        }

        /// <summary>Toggle section on/off (неидемпотентно).</summary>
        public static void Toggle()
        {
            var exec = GetExecMethod();
            var toolbar = GetToolbarValue();
            if (exec == null || toolbar == null) return;
            exec.Invoke(null, new object[] { "RoamerGUI_OM_SECTION_MASTER_ENABLE", toolbar });
        }

        // === ClippingPlanes API ===

        private static void ApplyClippingPlanes(string json)
        {
            var view = Application.ActiveDocument?.ActiveView;
            if (view == null) return;
            if (!view.TrySetClippingPlanes(json))
                view.SetClippingPlanes(json);
            view.RequestDelayedRedraw(ViewRedrawRequests.All);
        }

        /// <summary>Конвертирует миллиметры в единицы модели документа.</summary>
        public static double MmToDocUnits(double mm)
        {
            try
            {
                var doc = Application.ActiveDocument;
                if (doc == null) return mm / 1000.0;
                var units = doc.Units;
                switch (units)
                {
                    case Units.Centimeters: return mm / 10.0;
                    case Units.Meters: return mm / 1000.0;
                    case Units.Kilometers: return mm / 1000000.0;
                    case Units.Inches: return mm / 25.4;
                    case Units.Feet: return mm / 304.8;
                    case Units.Yards: return mm / 914.4;
                    case Units.Miles: return mm / 1609344.0;
                    case Units.Millimeters: return mm;
                    default: return mm / 1000.0; // fallback метры
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read document units for section-box conversion: " + ex.Message, "SectionBox");
                return mm / 1000.0;
            }
        }

        private static void ExpandViaClippingPlanes(double offsetMm)
        {
            var view = Application.ActiveDocument?.ActiveView;
            if (view == null) return;
            double offsetUnits = MmToDocUnits(offsetMm);

            string json = view.GetClippingPlanes();
            if (string.IsNullOrEmpty(json)) return;

            int boxIdx = json.IndexOf("\"Box\"", StringComparison.Ordinal);
            if (boxIdx < 0) return;

            int firstBracket = json.IndexOf("[[", boxIdx, StringComparison.Ordinal);
            if (firstBracket < 0) return;
            int lastBracket = json.IndexOf("]]", firstBracket, StringComparison.Ordinal);
            if (lastBracket <= firstBracket) return;

            string boxStr = json.Substring(firstBracket + 2, lastBracket - firstBracket - 2);
            string[] halves = boxStr.Split(new[] { "],[" }, StringSplitOptions.None);
            if (halves.Length != 2) return;

            var c = CultureInfo.InvariantCulture;
            string[] minP = halves[0].Split(',');
            string[] maxP = halves[1].Split(',');
            if (minP.Length != 3 || maxP.Length != 3) return;

            if (!double.TryParse(minP[0], NumberStyles.Float, c, out double minX) ||
                !double.TryParse(minP[1], NumberStyles.Float, c, out double minY) ||
                !double.TryParse(minP[2], NumberStyles.Float, c, out double minZ) ||
                !double.TryParse(maxP[0], NumberStyles.Float, c, out double maxX) ||
                !double.TryParse(maxP[1], NumberStyles.Float, c, out double maxY) ||
                !double.TryParse(maxP[2], NumberStyles.Float, c, out double maxZ))
                return;

            minX -= offsetUnits;
            minY -= offsetUnits;
            minZ -= offsetUnits;
            maxX += offsetUnits;
            maxY += offsetUnits;
            maxZ += offsetUnits;

            string newBox = string.Format(c, "[[{0},{1},{2}],[{3},{4},{5}]]",
                minX, minY, minZ, maxX, maxY, maxZ);
            string newJson = json.Substring(0, firstBracket) + newBox + json.Substring(lastBracket + 2);

            ApplyClippingPlanes(newJson);
        }

        // === Internal API reflection ===

        private static MethodInfo _execMethod;
        private static object _toolbarValue;

        private static readonly string[] FrameworkTypeNames = {
            "Autodesk.Navisworks.Api.Interop.LcRmFrameworkInterface",
            "Autodesk.Navisworks.Internal.ApiImplementation.LcRmFrameworkInterface",
        };
        private static readonly string[] ContextTypeNames = {
            "Autodesk.Navisworks.Api.Interop.LcUCIPExecutionContext",
            "Autodesk.Navisworks.Internal.ApiImplementation.LcUCIPExecutionContext",
        };

        private static MethodInfo GetExecMethod()
        {
            if (_execMethod != null) return _execMethod;
            Type fwType = null, ctxType = null;
            foreach (var n in FrameworkTypeNames) { fwType = FindType(n); if (fwType != null) break; }
            foreach (var n in ContextTypeNames) { ctxType = FindType(n); if (ctxType != null) break; }
            if (fwType == null || ctxType == null) return null;
            _execMethod = fwType.GetMethod("ExecuteCommand",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new Type[] { typeof(string), ctxType }, null);
            return _execMethod;
        }

        private static object GetToolbarValue()
        {
            if (_toolbarValue != null) return _toolbarValue;
            Type ctxType = null;
            foreach (var n in ContextTypeNames) { ctxType = FindType(n); if (ctxType != null) break; }
            if (ctxType == null) return null;
            _toolbarValue = Enum.Parse(ctxType, "eTOOLBAR");
            return _toolbarValue;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null)
                        return t;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to inspect assembly for type '" + fullName + "': " + ex.Message, "SectionBox");
                }
            }
            return null;
        }
    }
}
