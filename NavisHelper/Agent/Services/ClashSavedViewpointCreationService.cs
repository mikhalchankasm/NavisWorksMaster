using System;
using System.Globalization;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashSavedViewpointCreationService
    {
        public static ClashSavedViewpointTarget ResolveTargetFolder(Document document, string folderPath, out string warning)
        {
            warning = string.Empty;
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            GroupItem targetFolder;
            if (!DocumentCommandService.TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, true, out targetFolder, out _, out _))
            {
                warning = "Target Saved Viewpoints folder could not be created; using root.";
                return new ClashSavedViewpointTarget
                {
                    Folder = document.SavedViewpoints.RootItem,
                    FolderPath = string.Empty,
                };
            }

            return new ClashSavedViewpointTarget
            {
                Folder = targetFolder,
                FolderPath = folderPath ?? string.Empty,
            };
        }

        public static ClashSavedViewpointCreationResult SaveResetViewpoint(Document document, ClashSavedViewpointTarget target)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (target == null || target.Folder == null)
                return new ClashSavedViewpointCreationResult();

            var resetName = MakeUniqueSavedItemName(target.Folder, "0000 Базовый вид");
            SavedViewpointAppearanceHelper.SaveCurrentViewpointOnlyAtStart(document, target.Folder, resetName);
            RefreshTargetFolder(document, target);
            return new ClashSavedViewpointCreationResult
            {
                ViewpointName = resetName,
                ViewpointPath = BuildViewpointPath(target.FolderPath, resetName),
                Created = true,
            };
        }

        public static ClashSavedViewpointCreationResult SaveCurrentViewpointWithAppearance(Document document, ClashSavedViewpointTarget target, string baseName)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (target == null || target.Folder == null)
                return new ClashSavedViewpointCreationResult();

            var viewpointName = MakeUniqueSavedItemName(target.Folder, baseName);
            SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides(document, target.Folder, target.FolderPath, viewpointName);
            return new ClashSavedViewpointCreationResult
            {
                ViewpointName = viewpointName,
                ViewpointPath = BuildViewpointPath(target.FolderPath, viewpointName),
                Created = true,
            };
        }

        private static void RefreshTargetFolder(Document document, ClashSavedViewpointTarget target)
        {
            if (document == null || target == null)
                return;

            if (string.IsNullOrEmpty(target.FolderPath))
            {
                target.Folder = document.SavedViewpoints.RootItem;
                return;
            }

            GroupItem refreshedFolder;
            if (DocumentCommandService.TryResolveSavedViewpointFolder(
                document.SavedViewpoints,
                target.FolderPath,
                false,
                out refreshedFolder,
                out _,
                out _))
            {
                target.Folder = refreshedFolder;
            }
        }

        private static string MakeUniqueSavedItemName(GroupItem folder, string baseName)
        {
            var name = string.IsNullOrWhiteSpace(baseName) ? "Clash" : baseName;
            if (!DocumentCommandService.SavedItemExists(folder, name))
                return name;

            for (var i = 2; i < 10000; i++)
            {
                var candidate = name + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                if (!DocumentCommandService.SavedItemExists(folder, candidate))
                    return candidate;
            }

            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string BuildViewpointPath(string folderPath, string viewpointName)
        {
            return string.IsNullOrWhiteSpace(folderPath)
                ? viewpointName ?? string.Empty
                : folderPath + "/" + (viewpointName ?? string.Empty);
        }
    }

    internal sealed class ClashSavedViewpointTarget
    {
        public GroupItem Folder { get; set; }
        public string FolderPath { get; set; }
    }

    internal sealed class ClashSavedViewpointCreationResult
    {
        public string ViewpointName { get; set; }
        public string ViewpointPath { get; set; }
        public bool Created { get; set; }
    }
}
