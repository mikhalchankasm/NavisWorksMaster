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
                var doc = Application.ActiveDocument;
                if (doc == null || doc.SelectionSets == null || doc.SelectionSets.RootItem == null)
                    return -1;

                var fileName = string.Empty;
                if (parameters == null || parameters.Length < 1)
                {
                    using (var openDialog = new OpenFileDialog()
                    {
                        Filter = UiLocalizationService.Current.GetString("CommonFileFilterXmlAll"),
                        RestoreDirectory = true
                    })
                    {
                        if (openDialog.ShowDialog() == DialogResult.OK)
                        {
                            fileName = openDialog.FileName;
                        }
                        else
                        {
                            return -1;
                        }
                    }
                }
                else
                {
                    fileName = parameters[0];
                }

                //Добавление sets
                var root = doc.SelectionSets.RootItem;
                var xdoc = XDocument.Load(fileName);
                if (xdoc.Root == null)
                    return -1;

                currentItems = 0;
                DocumentSelectionSets oSets = doc.SelectionSets;
                foreach (var item in xdoc.Root.Elements())
                {
                    var folderName = (string)item.Attribute("Name") ?? string.Empty;
                    var folder = new FolderItem() { DisplayName = folderName };

                    foreach (var item2 in item.Elements())
                        AddElements(folder, item2);

                    oSets.AddCopy(root, folder);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex}", "ImportPslists");
                return -1;
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
                    var subFolder = new FolderItem() { DisplayName = (string)el.Attribute("Name") ?? string.Empty };
                    foreach (var element in el.Elements())
                        AddElements(subFolder, element);
                    folder.Children.Add(subFolder);
                    break;
                case "pspool":
                    var col = new ModelItemCollection();
                    foreach (var element in el.Elements())
                    {
                        var elementName = (string)element.Attribute("Name");
                        if (string.IsNullOrEmpty(elementName))
                        {
                            Logger.Warn($"Пропущен элемент <{element.Name.LocalName}> без атрибута Name в pspool '{(string)el.Attribute("Name") ?? string.Empty}'.", "ImportPslists");
                            continue;
                        }
                        var find = FindModelItem(elementName);
                        if (find != null)
                            col.Add(find);
                    }

                    if (col.Count > 0)
                    {
                        var selSet = new SelectionSet() { DisplayName = (string)el.Attribute("Name") ?? string.Empty };
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
