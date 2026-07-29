using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
// using Microsoft.Toolkit.Uwp.Notifications; // Временно отключено из-за проблем с совместимостью

namespace NavisHelper
{
    [Plugin("ImportPslists", //Plugin name
        "CBC", //Developer ID or GUID
               //ToolTip = "Add Custom Properties to Model Item",
               //The tooltip for the item in the ribbon
        DisplayName = "ImportPslists")]
    [AddInPlugin(AddInLocation.None)]
    public class ImportPslists : AddInPlugin
    {
        //private int allItems;
        private int currentItems;

        public override int Execute(params string[] parameters)
        {
            try
            {
                var fileName = string.Empty;
                if (parameters.Count() < 1)
                {
                    var openDialog = new OpenFileDialog()
                    {
                        Filter = UiLocalizationService.Current.GetString("CommonFileFilterXmlAll"),
                        RestoreDirectory = true
                    };

                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        fileName = openDialog.FileName;                        
                    }
                    else
                    {
                        return -1;
                    }
                }
                else
                {
                    fileName = parameters[0];
                }
                    

                //Добавление sets
                var root = Application.ActiveDocument.SelectionSets.RootItem;
                var xdoc = XDocument.Load(fileName);               

                currentItems = 0;
                foreach (var item in xdoc.Root.Elements())
                {
                    var folder = new FolderItem() { DisplayName = item.Attribute("Name").Value };
                    DocumentSelectionSets oSets = Application.ActiveDocument.SelectionSets;

                    foreach(var item2 in item.Elements())
                        AddElements(folder, item2);

                    oSets.AddCopy(root, folder);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex}", "ImportPslists");
                Logger.Info($"Ошибка: {ex.Message}", "ImportPslists");
                return 0;
            }

            return 0;
        }

        private void AddElements(GroupItem folder, XElement el)
        {            
            switch (el.Name.LocalName)
            {
                case "element":
                    break;
                case "folder":
                    var subFolder = new FolderItem() { DisplayName = el.Attribute("Name").Value };
                    foreach (var element in el.Elements())
                        AddElements(subFolder, element);
                    folder.Children.Add(subFolder);
                    break;
                case "pspool":
                    var col = new ModelItemCollection();
                    foreach (var element in el.Elements())
                    {
                        var find = FindModelItem(element.Attribute("Name").Value);
                        if (find != null)
                            col.Add(find);
                    }

                    if (col.Count > 0)
                    {
                        var selSet = new SelectionSet() { DisplayName = el.Attribute("Name").Value };
                        selSet.ExplicitModelItems.AddRange(col);
                        folder.Children.Add(selSet);
                    }

                    currentItems += el.Elements().Count();
                    Logger.Info($"Обработано {currentItems} элементов.", "ImportPslists");
                    break;
                case "main":
                    foreach (var mem in el.Elements())
                        AddElements(folder, mem);
                    break;
                default:
                    Logger.Info($"{el.Name.LocalName}", "ImportPslists");
                    break;
            }            
        }

        private ModelItem FindModelItem(string name)
        {
            var search = new Search();
            search.Selection.SelectAll();
            search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Item", "Name").EqualValue(VariantData.FromDisplayString(name)));
            return search.FindFirst(Application.ActiveDocument, true);
        }

        private ModelItemCollection FindAll()
        {
            var seach = new Search();

            return seach.FindAll(Application.ActiveDocument, false);
        }
    }
}
