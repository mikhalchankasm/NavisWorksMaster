using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NavisHelper
{
    // Keep the historical plugin id because existing ribbon and panel buttons use it.
    [Plugin("SaveHierarhy", "COMPANY", DisplayName = "Сохранить иерархию")]
    [AddInPlugin(AddInLocation.None)]
    public class SaveHierarhyPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            Document document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                ShowMessage("Нет активного документа Navisworks.", MessageBoxIcon.Warning);
                return -1;
            }

            if (string.IsNullOrWhiteSpace(document.FileName))
            {
                ShowMessage("Сначала сохраните документ Navisworks, чтобы определить папку экспорта.", MessageBoxIcon.Warning);
                return -1;
            }

            string temporaryPath = null;
            try
            {
                string outputPath = PromptForOutputPath(document.FileName);
                if (string.IsNullOrEmpty(outputPath))
                    return 0;

                temporaryPath = Path.Combine(
                    Path.GetDirectoryName(outputPath),
                    "." + Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

                bool canceled;
                int itemCount = ExportModelHierarchy(document, temporaryPath, out canceled);
                if (canceled)
                {
                    ShowMessage("Экспорт иерархии отменён. Исходный файл не изменён.", MessageBoxIcon.Information);
                    return 0;
                }

                CommitTemporaryFile(temporaryPath, outputPath);
                temporaryPath = null;

                Logger.Info(
                    string.Format("Иерархия модели сохранена: {0} элементов, файл {1}", itemCount, outputPath),
                    "SaveHierarhy",
                    document.FileName);

                ShowMessage(
                    string.Format("Иерархия модели сохранена.\n\nЭлементов: {0}\nФайл: {1}", itemCount, outputPath),
                    MessageBoxIcon.Information);
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка экспорта иерархии: " + ex, "SaveHierarhy", document.FileName);
                ShowMessage("Не удалось сохранить иерархию.\n\n" + ex.Message, MessageBoxIcon.Error);
                return -1;
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static string PromptForOutputPath(string documentPath)
        {
            string directory = Path.GetDirectoryName(documentPath);
            string fileName = Path.GetFileNameWithoutExtension(documentPath);

            using (var dialog = new SaveFileDialog
            {
                Title = "Сохранить иерархию модели",
                Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
                DefaultExt = "txt",
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true,
                InitialDirectory = directory,
                FileName = fileName + "_hierarchy.txt"
            })
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private static int ExportModelHierarchy(Document document, string outputPath, out bool canceled)
        {
            int itemCount = 0;
            int modelIndex = 0;
            int modelCount = document.Models.Count;
            canceled = false;

            Autodesk.Navisworks.Api.Progress progress = null;
            try
            {
                progress = Autodesk.Navisworks.Api.Application.BeginProgress("Экспорт иерархии модели");
                using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true)))
                {
                    foreach (Model model in document.Models)
                    {
                        if (progress.IsCanceled)
                        {
                            canceled = true;
                            break;
                        }

                        ModelItem root = model.RootItem;
                        if (root != null && !WriteTree(writer, root, modelIndex, modelCount, progress, ref itemCount))
                        {
                            canceled = true;
                            break;
                        }

                        modelIndex++;
                        progress.Update(modelCount == 0 ? 1.0 : (double)modelIndex / modelCount);
                    }
                }
            }
            finally
            {
                if (progress != null)
                    Autodesk.Navisworks.Api.Application.EndProgress();
            }

            return itemCount;
        }

        private static bool WriteTree(
            StreamWriter writer,
            ModelItem root,
            int modelIndex,
            int modelCount,
            Autodesk.Navisworks.Api.Progress progress,
            ref int itemCount)
        {
            var pending = new Stack<HierarchyNode>();
            pending.Push(new HierarchyNode(root, 0));

            while (pending.Count > 0)
            {
                if ((itemCount & 511) == 0)
                {
                    if (progress.IsCanceled)
                        return false;

                    progress.Update(modelCount == 0 ? 0.0 : (double)modelIndex / modelCount);
                }

                HierarchyNode node = pending.Pop();
                WriteItem(writer, node.Item, node.Depth);
                itemCount++;

                var children = new List<ModelItem>();
                foreach (ModelItem child in node.Item.Children)
                    children.Add(child);

                for (int index = children.Count - 1; index >= 0; index--)
                    pending.Push(new HierarchyNode(children[index], node.Depth + 1));
            }

            return true;
        }

        private static void WriteItem(StreamWriter writer, ModelItem item, int depth)
        {
            string displayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? "<без имени>"
                : item.DisplayName.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

            int indentationDepth = Math.Min(depth, 256);
            writer.Write(new string('\t', indentationDepth));
            if (depth > indentationDepth)
                writer.Write("[уровень " + depth + "] ");
            writer.WriteLine(displayName);
        }

        private static void CommitTemporaryFile(string temporaryPath, string outputPath)
        {
            if (File.Exists(outputPath))
                File.Replace(temporaryPath, outputPath, null);
            else
                File.Move(temporaryPath, outputPath);
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private struct HierarchyNode
        {
            public HierarchyNode(ModelItem item, int depth)
            {
                Item = item;
                Depth = depth;
            }

            public ModelItem Item { get; }
            public int Depth { get; }
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(message, "Сохранить иерархию", MessageBoxButtons.OK, icon);
        }
    }
}
