using System;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("FilterModels", "COMPANY", DisplayName = "Фильтр по списку из файла")]
    [AddInPlugin(AddInLocation.None)]
    public class FilterModelsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                // Получаем активный документ
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    Logger.Error("Нет загруженного документа.", "FilterNavisModels");
                    return 0;
                }

                // Retained compatibility command; the legacy window is not compiled.
                Logger.Info("Фильтр моделей временно отключен", "FilterNavisModels");

                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}", "FilterNavisModels");
                return -1;
            }
        }
    }
}
