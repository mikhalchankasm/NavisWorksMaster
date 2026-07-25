using System;
using System.Globalization;
using System.Reflection;
using Autodesk.Navisworks.Api;
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
            var c = CultureInfo.InvariantCulture;

            // Пробуем сохранить текущий rotation если есть
            string rotation = "0,0,0";
            try
            {
                var view = Application.ActiveDocument.ActiveView;
                string current = view.GetClippingPlanes();
                if (!string.IsNullOrEmpty(current))
                {
                    int rotIdx = current.IndexOf("\"Rotation\"");
                    if (rotIdx >= 0)
                    {
                        int rotStart = current.IndexOf("[", rotIdx);
                        int rotEnd = current.IndexOf("]", rotStart);
                        if (rotStart >= 0 && rotEnd > rotStart)
                            rotation = current.Substring(rotStart + 1, rotEnd - rotStart - 1);
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
                var view = Application.ActiveDocument.ActiveView;
                string json = view.GetClippingPlanes();
                if (string.IsNullOrEmpty(json)) return;

                // Если уже отключён — ничего не делаем
                if (!json.Contains("\"Enabled\":true")) return;

                // Заменяем Enabled:true на Enabled:false
                string disabled = json.Replace("\"Enabled\":true", "\"Enabled\":false");
                ApplyClippingPlanes(disabled);
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
            var view = Application.ActiveDocument.ActiveView;
            if (!view.TrySetClippingPlanes(json))
                view.SetClippingPlanes(json);
            view.RequestDelayedRedraw(ViewRedrawRequests.All);
        }

        /// <summary>Конвертирует миллиметры в единицы модели документа.</summary>
        public static double MmToDocUnits(double mm)
        {
            try
            {
                var units = Application.ActiveDocument.Units;
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
            var view = Application.ActiveDocument.ActiveView;
            double offsetUnits = MmToDocUnits(offsetMm);

            string json = view.GetClippingPlanes();
            if (string.IsNullOrEmpty(json)) return;

            int boxIdx = json.IndexOf("\"Box\"");
            if (boxIdx < 0) return;

            int firstBracket = json.IndexOf("[[", boxIdx);
            int lastBracket = json.IndexOf("]]", firstBracket);
            if (firstBracket < 0 || lastBracket < 0) return;

            string boxStr = json.Substring(firstBracket + 2, lastBracket - firstBracket - 2);
            string[] halves = boxStr.Split(new[] { "],[" }, StringSplitOptions.None);
            if (halves.Length != 2) return;

            var c = CultureInfo.InvariantCulture;
            string[] minP = halves[0].Split(',');
            string[] maxP = halves[1].Split(',');
            if (minP.Length != 3 || maxP.Length != 3) return;

            double minX = double.Parse(minP[0], c) - offsetUnits;
            double minY = double.Parse(minP[1], c) - offsetUnits;
            double minZ = double.Parse(minP[2], c) - offsetUnits;
            double maxX = double.Parse(maxP[0], c) + offsetUnits;
            double maxY = double.Parse(maxP[1], c) + offsetUnits;
            double maxZ = double.Parse(maxP[2], c) + offsetUnits;

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
