using System;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("TopViewSection", "CBC", DisplayName = "Вид сверху")]
    [AddInPlugin(AddInLocation.None)]
    public class TopViewSection : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                Document doc = Application.ActiveDocument;
                if (doc == null)
                {
                    Logger.Error("Нет активного документа", "TopViewSection");
                    return 0;
                }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection.Count == 0)
                {
                    Logger.Info("Нет выделенных элементов", "TopViewSection");
                    return 0;
                }

                // Вычисляем объединённый BoundingBox всех выделенных элементов
                BoundingBox3D combinedBBox = selection.BoundingBox();
                if (combinedBBox == null)
                {
                    Logger.Info("Не удалось определить область выделенных элементов", "TopViewSection");
                    return 0;
                }

                // 1. Устанавливаем вид сверху (ортографический, камера смотрит по -Z)
                Viewpoint vp = doc.CurrentViewpoint.CreateCopy();
                vp.Rotation = new Rotation3D(0, 0, 0, -1); // X=0, Y=0, Z=0, W=-1 → вид сверху
                vp.Projection = ViewpointProjection.Orthographic;

                // 2. Зумируем на выделенные элементы
                vp.ZoomBox(combinedBBox);
                doc.CurrentViewpoint.CopyFrom(vp);

                // 3. Включаем сечение (если выключено)
                EnableSectionIfNeeded();

                Logger.Info($"Вид сверху установлен ({selection.Count} элементов)", "TopViewSection");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}\n{ex.StackTrace}", "TopViewSection");
                return 0;
            }
        }

        private void EnableSectionIfNeeded()
        {
            try
            {
                // Проверяем состояние режима сечения через GUI ribbon
                bool isSectionEnabled = false;
                var mainWindow = Application.Gui.MainWindow;
                if (mainWindow != null)
                {
                    var ribbonControl = mainWindow.GetType()
                        .GetProperty("RibbonControl")
                        ?.GetValue(mainWindow);

                    if (ribbonControl != null)
                    {
                        var findItem = ribbonControl.GetType().GetMethod("FindItem");
                        if (findItem != null)
                        {
                            dynamic sectionButton = findItem.Invoke(ribbonControl, new object[] { "ID_SECTION_BOX" });
                            if (sectionButton != null)
                            {
                                isSectionEnabled = sectionButton.IsChecked;
                            }
                        }
                    }
                }

                // Включаем режим сечения только если он выключен
                if (!isSectionEnabled)
                {
                    // Используем reflection для доступа к внутренним типам Navisworks API
                    var fwType = FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcRmFrameworkInterface");
                    var ctxType = FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcUCIPExecutionContext");

                    if (fwType != null && ctxType != null)
                    {
                        var toolbarValue = Enum.Parse(ctxType, "eTOOLBAR");
                        var execMethod = fwType.GetMethod("ExecuteCommand",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                            null,
                            new Type[] { typeof(string), ctxType },
                            null);
                        execMethod?.Invoke(null, new object[] { "RoamerGUI_OM_SECTION_MASTER_ENABLE", toolbarValue });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Не удалось включить сечение: {ex.Message}", "TopViewSection");
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
