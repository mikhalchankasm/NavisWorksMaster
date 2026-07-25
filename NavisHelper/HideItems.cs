using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;
using Color = System.Drawing.Color;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("HideItems", //Plugin name
        "CBC", //Developer ID or GUID
               //ToolTip = "Add Custom Properties to Model Item",
               //The tooltip for the item in the ribbon
        DisplayName = "Override PDMS colors")]
    [AddInPlugin(AddInLocation.None)]
    public class HideItems : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                if (parameters.Count() < 1)
                    return -1;
                var filename = parameters[0];
                if (!File.Exists(filename))
                    return -1;

                var names = File.ReadAllLines(filename);

                var oS = new Search();

                foreach (var name in names)
                {
                    oS.Clear();
                    oS.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Item", "Name").EqualValue(VariantData.FromDisplayString(name)));
                    oS.Selection.SelectAll();
                    var findItems = oS.FindAll(Application.ActiveDocument, false);
                    if (findItems.Count > 0)
                    {
                        Application.ActiveDocument.Models.SetHidden(findItems, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}\n{ex.StackTrace}", "HideItems");
                return 0;
            }

            return 0;
        }
    }
}
