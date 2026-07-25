using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Interfaces;
using Application = Autodesk.Navisworks.Api.Application;
using MessageBox = System.Windows.MessageBox;

namespace NavisHelper.Dev
{
    /// <summary>
    /// Создаёт матрицу clash-тестов по всем уникальным парам имён из текстового файла.
    /// Ищет элементы по списку имён и создаёт clash-тесты по всем уникальным парам.
    /// Тесты добавляются напрямую в корень Clash Detective, потому что вложение
    /// ClashTest внутрь ClashTestFolder через API Navisworks не поддерживается.
    /// </summary>
    public class ClashMatrixFromFileScript : IDevScript
    {
        private const string GeneratedPrefix = "[NH-MATRIX]";
        private const string SelectionFolderLabel = GeneratedPrefix + " SelectionSets ";
        private const string ClashFolderLabel = GeneratedPrefix + " ClashTests ";
        private const string LegacyStandaloneTestPrefix = GeneratedPrefix + " ";

        private static readonly (string Category, string Name)[] NameProps =
        {
            ("Item", "Name"),
            ("Элемент", "Имя"),
            ("Объект", "Имя"),
            ("Element", "Name"),
            ("", "Name"),
            ("", "Имя"),
        };

        private static readonly (string Category, string Name)[] InternalProps =
        {
            ("LcOaNode", "Name"),
            ("LcOaNode", "LcOaSceneBaseUserName"),
        };

        private sealed class MatchedEntry
        {
            public string RequestedName { get; set; }
            public ModelItemCollection Items { get; set; }
        }

        public string Name => "Матрица clash из файла";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Нет активной модели.", Name);
                return;
            }

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null)
            {
                MessageBox.Show("Clash Detective недоступен в текущем документе.", Name);
                return;
            }

            var filePath = RequestInputFilePath();
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var requestedNames = ReadRequestedNames(filePath);
            if (requestedNames.Count < 2)
            {
                MessageBox.Show(
                    "В файле должно быть минимум 2 уникальные непустые строки.",
                    Name);
                return;
            }

            var existingSelectionSetCount = CountGeneratedSelectionSets(doc.SelectionSets);
            var existingClashTestCount = CountGeneratedClashTests(clash.TestsData);

            var clearExisting = false;
            if (existingSelectionSetCount > 0 || existingClashTestCount > 0)
            {
                var answer = System.Windows.Forms.MessageBox.Show(
                    $"Найдено ранее сгенерированных данных:\n" +
                    $"- selection sets: {existingSelectionSetCount}\n" +
                    $"- clash tests: {existingClashTestCount}\n\n" +
                    "Удалить их перед созданием новой матрицы?\n\n" +
                    "Yes = удалить старые и создать заново\n" +
                    "No = оставить старые и добавить новые\n" +
                    "Cancel = отмена",
                    Name,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Cancel)
                    return;

                clearExisting = answer == DialogResult.Yes;
            }

            var matchedEntries = new List<MatchedEntry>();
            var missingNames = new List<string>();
            var unprocessedNames = new List<string>();
            var logLines = new List<string>();
            var progress = Application.BeginProgress();
            var canceled = false;
            var removedSelectionSetCount = 0;
            var removedClashTestCount = 0;
            var createdSelectionSetCount = 0;
            var createdClashTestCount = 0;
            var createdSelectionFolderName = string.Empty;
            var createdClashFolderName = string.Empty;
            SavedItem createdSelectionFolder = null;
            var createdClashItems = new List<SavedItem>();
            Exception failure = null;

            try
            {
                if (clearExisting)
                {
                    removedSelectionSetCount = RemoveGeneratedSelectionSets(doc.SelectionSets);
                    removedClashTestCount = RemoveGeneratedClashTests(clash.TestsData);
                }

                UpdateProgress(progress, 0.10);

                for (int i = 0; i < requestedNames.Count; i++)
                {
                    if (progress.IsCanceled)
                    {
                        canceled = true;
                        for (int j = i; j < requestedNames.Count; j++)
                            unprocessedNames.Add(requestedNames[j]);
                        break;
                    }

                    var requestedName = requestedNames[i];
                    var search = CreateSearchForName(requestedName);
                    var items = search.FindAll(doc, false);
                    var hasMatch = items != null && items.Count > 0;

                    if (hasMatch)
                    {
                        matchedEntries.Add(new MatchedEntry
                        {
                            RequestedName = requestedName,
                            Items = items,
                        });
                        logLines.Add("[MATCH] " + requestedName + " — найдено элементов: " + items.Count);
                    }
                    else
                    {
                        missingNames.Add(requestedName);
                        logLines.Add("[MISS] " + requestedName + " — не найден");
                    }

                    UpdatePhaseProgress(progress, 0.10, 0.40, i + 1, requestedNames.Count);
                }

                if (!canceled && matchedEntries.Count >= 2)
                {
                    var runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var testPrefix = GeneratedPrefix + " " + runId + " ";
                    var pairCount = matchedEntries.Count * (matchedEntries.Count - 1) / 2;
                    var pairDone = 0;
                    var root = clash.TestsData.Value.TestsRoot;
                    logLines.Add("[CLASH] Тесты добавляются в корень Clash Detective.");

                    for (int i = 0; i < matchedEntries.Count; i++)
                    {
                        if (progress.IsCanceled)
                        {
                            canceled = true;
                            break;
                        }

                        var entryA = matchedEntries[i];
                        for (int j = i + 1; j < matchedEntries.Count; j++)
                        {
                            if (progress.IsCanceled)
                            {
                                canceled = true;
                                break;
                            }

                            var entryB = matchedEntries[j];
                            var beforeCount = root.Children.Count;
                            clash.TestsData.TestsAddCopy(root, BuildClashTest(
                                testPrefix,
                                entryA.RequestedName,
                                entryA.Items,
                                entryB.RequestedName,
                                entryB.Items));

                            if (root.Children.Count > beforeCount)
                                createdClashItems.Add(root.Children[root.Children.Count - 1]);

                            pairDone++;
                            createdClashTestCount = pairDone;
                            UpdatePhaseProgress(progress, 0.50, 0.45, pairDone, pairCount);
                        }
                    }
                }

                UpdateProgress(progress, 1.0);
            }
            catch (Exception ex)
            {
                failure = ex;
                logLines.Add("[ERROR] " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Application.EndProgress();
            }

            if ((canceled || failure != null) && createdClashItems.Count > 0)
            {
                for (int index = createdClashItems.Count - 1; index >= 0; index--)
                {
                    RemoveRootClashItem(clash.TestsData, createdClashItems[index]);
                }

                logLines.Add("[ROLLBACK] Созданные clash tests удалены из-за " +
                             (canceled ? "отмены операции." : "ошибки."));
                createdClashTestCount = 0;
            }

            if ((canceled || failure != null) && createdSelectionFolder != null)
            {
                doc.SelectionSets.Remove(createdSelectionFolder);
                createdSelectionFolder = null;
                createdSelectionFolderName = string.Empty;
                createdSelectionSetCount = 0;
                logLines.Add("[ROLLBACK] Созданная папка selection sets удалена из-за " +
                             (canceled ? "отмены операции." : "ошибки."));
            }

            var logPath = WriteLog(
                filePath,
                requestedNames,
                matchedEntries,
                missingNames,
                unprocessedNames,
                removedSelectionSetCount,
                removedClashTestCount,
                createdSelectionSetCount,
                createdClashTestCount,
                createdSelectionFolderName,
                createdClashFolderName,
                canceled,
                failure,
                logLines);

            ShowSummary(
                filePath,
                requestedNames.Count,
                matchedEntries.Count,
                missingNames,
                unprocessedNames,
                removedSelectionSetCount,
                removedClashTestCount,
                createdSelectionSetCount,
                createdClashTestCount,
                createdSelectionFolderName,
                createdClashFolderName,
                canceled,
                failure,
                logPath);
        }

        private static string RequestInputFilePath()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Выберите файл со списком имён для clash-матрицы"
            };

            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.FileName
                : null;
        }

        private static List<string> ReadRequestedNames(string filePath)
        {
            IEnumerable<string> rawLines;

            try
            {
                rawLines = File.ReadAllLines(filePath, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                rawLines = File.ReadAllLines(filePath, Encoding.Default);
            }

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in rawLines)
            {
                var line = NormalizeLine(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (seen.Add(line))
                    names.Add(line);
            }

            return names;
        }

        private static string NormalizeLine(string value)
        {
            return (value ?? string.Empty).Trim().Trim('\uFEFF');
        }

        private static Search CreateSearchForName(string itemName)
        {
            var search = new Search
            {
                Locations = SearchLocations.DescendantsAndSelf,
                PruneBelowMatch = false,
            };
            search.Selection.SelectAll();

            foreach (var property in InternalProps)
            {
                search.SearchConditions.AddGroup(new[]
                {
                    SearchCondition.HasPropertyByName(property.Category, property.Name)
                        .EqualValue(VariantData.FromDisplayString(itemName))
                        .IgnoreStringValueCase()
                });
            }

            foreach (var property in NameProps)
            {
                search.SearchConditions.AddGroup(new[]
                {
                    SearchCondition.HasPropertyByDisplayName(property.Category, property.Name)
                        .EqualValue(VariantData.FromDisplayString(itemName))
                        .IgnoreStringValueCase()
                });
            }

            return search;
        }

        private static ClashTest BuildClashTest(
            string testPrefix,
            string nameA,
            ModelItemCollection itemsA,
            string nameB,
            ModelItemCollection itemsB)
        {
            var test = new ClashTest
            {
                DisplayName = testPrefix + nameA + " vs " + nameB,
                Guid = Guid.Empty,
            };

            test.SelectionA.Selection.CopyFrom(itemsA);
            test.SelectionA.SelfIntersect = false;

            test.SelectionB.Selection.CopyFrom(itemsB);
            test.SelectionB.SelfIntersect = false;

            return test;
        }

        private static int CountGeneratedSelectionSets(DocumentSelectionSets selectionSets)
        {
            var count = 0;
            foreach (SavedItem child in selectionSets.RootItem.Children)
            {
                if (IsGeneratedSelectionRootItem(child))
                    count += CountSelectionSetsRecursive(child);
            }

            return count;
        }

        private static int RemoveGeneratedSelectionSets(DocumentSelectionSets selectionSets)
        {
            var toRemove = new List<SavedItem>();
            foreach (SavedItem child in selectionSets.RootItem.Children)
            {
                if (IsGeneratedSelectionRootItem(child))
                    toRemove.Add(child);
            }

            var removed = 0;
            foreach (var child in toRemove)
            {
                removed += CountSelectionSetsRecursive(child);
                selectionSets.Remove(child);
            }

            return removed;
        }

        private static int CountGeneratedClashTests(DocumentClashTests testsData)
        {
            var root = testsData.Value.TestsRoot;
            var count = 0;
            foreach (SavedItem child in root.Children)
            {
                if (IsGeneratedClashRootItem(child) || IsLegacyStandaloneGeneratedTest(child))
                    count += CountClashTestsRecursive(child);
            }

            return count;
        }

        private static int RemoveGeneratedClashTests(DocumentClashTests testsData)
        {
            var root = testsData.Value.TestsRoot;
            var removed = 0;
            var index = 0;

            while (index < root.Children.Count)
            {
                var child = root.Children[index];
                if (IsGeneratedClashRootItem(child) || IsLegacyStandaloneGeneratedTest(child))
                {
                    removed += CountClashTestsRecursive(child);
                    testsData.TestsRemoveAt(root, index);
                    continue;
                }

                index++;
            }

            return removed;
        }

        private static bool IsGeneratedSelectionRootItem(SavedItem item)
        {
            return HasDisplayNamePrefix(item, SelectionFolderLabel);
        }

        private static bool IsGeneratedClashRootItem(SavedItem item)
        {
            return HasDisplayNamePrefix(item, ClashFolderLabel);
        }

        private static bool IsLegacyStandaloneGeneratedTest(SavedItem item)
        {
            return HasDisplayNamePrefix(item, LegacyStandaloneTestPrefix);
        }

        private static bool HasDisplayNamePrefix(SavedItem item, string prefix)
        {
            return item != null &&
                   !string.IsNullOrWhiteSpace(item.DisplayName) &&
                   item.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RemoveRootClashItem(DocumentClashTests testsData, SavedItem item)
        {
            if (testsData == null || item == null)
                return false;

            var root = testsData.Value.TestsRoot;
            for (int index = 0; index < root.Children.Count; index++)
            {
                if (ReferenceEquals(root.Children[index], item))
                {
                    testsData.TestsRemoveAt(root, index);
                    return true;
                }
            }

            return false;
        }

        private static int CountSelectionSetsRecursive(SavedItem item)
        {
            if (item is SelectionSet)
                return 1;

            var group = item as GroupItem;
            if (group == null)
                return 0;

            var count = 0;
            foreach (SavedItem child in group.Children)
                count += CountSelectionSetsRecursive(child);

            return count;
        }

        private static int CountClashTestsRecursive(SavedItem item)
        {
            if (item is ClashTest)
                return 1;

            var group = item as GroupItem;
            if (group == null)
                return 0;

            var count = 0;
            foreach (SavedItem child in group.Children)
                count += CountClashTestsRecursive(child);

            return count;
        }

        private static SavedItem FindRootItemByName(GroupItem root, string displayName)
        {
            foreach (SavedItem child in root.Children)
            {
                if (string.Equals(child.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        private static void UpdateProgress(Progress progress, double percent)
        {
            if (progress == null)
                return;

            var clamped = Math.Max(0.0, Math.Min(1.0, percent));
            progress.Update(clamped);
        }

        private static void UpdatePhaseProgress(
            Progress progress,
            double phaseStart,
            double phaseWeight,
            int completed,
            int total)
        {
            if (total <= 0)
            {
                UpdateProgress(progress, phaseStart + phaseWeight);
                return;
            }

            UpdateProgress(progress, phaseStart + phaseWeight * ((double)completed / total));
        }

        private static string WriteLog(
            string inputFilePath,
            IReadOnlyCollection<string> requestedNames,
            IReadOnlyCollection<MatchedEntry> matchedEntries,
            IReadOnlyCollection<string> missingNames,
            IReadOnlyCollection<string> unprocessedNames,
            int removedSelectionSetCount,
            int removedClashTestCount,
            int createdSelectionSetCount,
            int createdClashTestCount,
            string createdSelectionFolderName,
            string createdClashFolderName,
            bool canceled,
            Exception failure,
            IEnumerable<string> logLines)
        {
            var logPath = Path.Combine(
                Path.GetDirectoryName(inputFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.GetFileNameWithoutExtension(inputFilePath) + "_nh_clash_matrix_log.txt");

            var content = new List<string>
            {
                "NavisHelper.Dev - Clash matrix from file",
                "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "Input file: " + inputFilePath,
                "Requested names: " + requestedNames.Count,
                "Processed names: " + (requestedNames.Count - unprocessedNames.Count),
                "Matched names: " + matchedEntries.Count,
                "Missing names: " + missingNames.Count,
                "Unprocessed names: " + unprocessedNames.Count,
                "Removed selection sets: " + removedSelectionSetCount,
                "Removed clash tests: " + removedClashTestCount,
                "Created selection sets: " + createdSelectionSetCount,
                "Created clash tests: " + createdClashTestCount,
                "Created selection folder: " + (string.IsNullOrWhiteSpace(createdSelectionFolderName) ? "-" : createdSelectionFolderName),
                "Created clash folder: " + (string.IsNullOrWhiteSpace(createdClashFolderName) ? "-" : createdClashFolderName),
                "Canceled: " + (canceled ? "yes" : "no"),
                "Error: " + (failure == null ? "-" : failure.GetType().Name + ": " + failure.Message),
                string.Empty,
            };

            foreach (var logLine in logLines)
                content.Add(logLine);

            if (unprocessedNames.Count > 0)
            {
                content.Add(string.Empty);
                content.Add("[UNPROCESSED]");
                foreach (var name in unprocessedNames)
                    content.Add(name);
            }

            File.WriteAllLines(logPath, content, Encoding.UTF8);
            return logPath;
        }

        private void ShowSummary(
            string filePath,
            int requestedCount,
            int matchedCount,
            IReadOnlyList<string> missingNames,
            IReadOnlyList<string> unprocessedNames,
            int removedSelectionSetCount,
            int removedClashTestCount,
            int createdSelectionSetCount,
            int createdClashTestCount,
            string createdSelectionFolderName,
            string createdClashFolderName,
            bool canceled,
            Exception failure,
            string logPath)
        {
            var summary = new StringBuilder();
            summary.AppendLine("Файл: " + Path.GetFileName(filePath));
            summary.AppendLine("Строк в списке: " + requestedCount);
            summary.AppendLine("Найдено наборов: " + matchedCount);
            summary.AppendLine("Создано selection sets: " + createdSelectionSetCount);
            summary.AppendLine("Создано clash-тестов: " + createdClashTestCount);

            if (removedSelectionSetCount > 0 || removedClashTestCount > 0)
            {
                summary.AppendLine("Удалено старых selection sets: " + removedSelectionSetCount);
                summary.AppendLine("Удалено старых clash-тестов: " + removedClashTestCount);
            }

            if (!string.IsNullOrWhiteSpace(createdSelectionFolderName))
                summary.AppendLine("Папка selection sets: " + createdSelectionFolderName);

            if (!string.IsNullOrWhiteSpace(createdClashFolderName))
                summary.AppendLine("Папка clash tests: " + createdClashFolderName);
            else if (createdClashTestCount > 0)
                summary.AppendLine("Тесты добавлены в корень Clash Detective.");

            if (missingNames.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Не найдены:");
                foreach (var missingName in missingNames.Take(10))
                    summary.AppendLine("  - " + missingName);

                if (missingNames.Count > 10)
                    summary.AppendLine("  ... ещё " + (missingNames.Count - 10));
            }

            if (unprocessedNames.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Не обработаны из-за отмены:");
                foreach (var name in unprocessedNames.Take(10))
                    summary.AppendLine("  - " + name);

                if (unprocessedNames.Count > 10)
                    summary.AppendLine("  ... ещё " + (unprocessedNames.Count - 10));
            }

            if (canceled)
            {
                summary.AppendLine();
                summary.AppendLine("Операция прервана пользователем.");
            }

            if (failure != null)
            {
                summary.AppendLine();
                summary.AppendLine("Ошибка: " + failure.Message);
            }

            if (createdSelectionSetCount > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Selection sets нужно оставить в документе: clash-тесты ссылаются на них.");
            }

            summary.AppendLine();
            summary.AppendLine("Лог: " + logPath);

            MessageBox.Show(summary.ToString(), Name);
        }
    }
}
