using System;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Plugins;
using Microsoft.VisualBasic;
using Application = Autodesk.Navisworks.Api.Application;
using Autodesk.Navisworks.Api.Automation;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Internal.ApiImplementation;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using NavisworksIntegratedAPI21 = Autodesk.Navisworks.Api.Interop.ComApi;
using System.Collections.Generic;
using System.IO;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("RunSaveClashReport", "MS", DisplayName = "Проверка всех коллизий")]
    [AddInPlugin(AddInLocation.None)]
    public class RunSaveClashReport : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                var document = Application.ActiveDocument;
                RunAllTests(document);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Произошла ошибка: {ex.Message}", "RunSaveClashReport");
                return -1;
            }
        }

        private void RunAllTests(Document document)
        {
            // Получаем менеджер коллизий
            DocumentClash clash = document.GetClash();
            
            // Запускаем все тесты на коллизии
            clash.TestsData.TestsRunAllTests();
        }
    }
}
