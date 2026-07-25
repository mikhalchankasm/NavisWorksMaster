using System;
using System.Linq;
using System.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace NavisHelper.Core
{
    public static class SavedViewpointAppearanceHelper
    {
        public static SavedViewpoint SaveCurrentViewWithAppearanceOverrides(
            Document document,
            GroupItem targetFolder,
            string folderPath,
            string viewpointName)
        {
            return SaveCurrentViewWithAppearanceOverrides(document, targetFolder, folderPath, viewpointName, true, true);
        }

        public static SavedViewpoint SaveCurrentViewWithAppearanceOverrides(
            Document document,
            GroupItem targetFolder,
            string folderPath,
            string viewpointName,
            bool captureMaterialAttributes,
            bool captureHideAttributes)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.CurrentViewpoint == null)
                throw new InvalidOperationException("Current viewpoint is unavailable.");
            if (string.IsNullOrWhiteSpace(viewpointName))
                throw new ArgumentException("Viewpoint name is required.", nameof(viewpointName));

            targetFolder = targetFolder ?? document.SavedViewpoints.RootItem;
            SaveCurrentViewWithCom(document, folderPath, viewpointName, captureMaterialAttributes, captureHideAttributes);

            var refreshedFolder = ResolveNetFolder(document.SavedViewpoints.RootItem, folderPath) ?? targetFolder;
            var savedViewpoint = FindSavedViewpointWithRetry(refreshedFolder, viewpointName);
            if (savedViewpoint == null)
                throw new InvalidOperationException("Created saved viewpoint was not found: " + viewpointName);

            document.SavedViewpoints.ReplaceFromCurrentView(savedViewpoint);

            return savedViewpoint;
        }

        public static SavedViewpoint SaveCurrentViewpointOnly(
            Document document,
            GroupItem targetFolder,
            string viewpointName)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.CurrentViewpoint == null)
                throw new InvalidOperationException("Current viewpoint is unavailable.");
            if (document.SavedViewpoints == null)
                throw new InvalidOperationException("Saved viewpoints are unavailable.");
            if (string.IsNullOrWhiteSpace(viewpointName))
                throw new ArgumentException("Viewpoint name is required.", nameof(viewpointName));

            targetFolder = targetFolder ?? document.SavedViewpoints.RootItem;
            var savedViewpoint = new SavedViewpoint(document.CurrentViewpoint.CreateCopy())
            {
                DisplayName = viewpointName
            };
            document.SavedViewpoints.AddCopy(targetFolder, savedViewpoint);
            return FindSavedViewpointWithRetry(targetFolder, viewpointName);
        }

        public static SavedViewpoint SaveCurrentViewpointOnlyAtStart(
            Document document,
            GroupItem targetFolder,
            string viewpointName)
        {
            var savedViewpoint = SaveCurrentViewpointOnly(document, targetFolder, viewpointName);
            if (savedViewpoint != null)
                MoveSavedViewpointToStart(document, targetFolder ?? document.SavedViewpoints.RootItem, savedViewpoint);

            return savedViewpoint;
        }

        public static SavedViewpoint SaveCurrentResetViewpointAtStart(
            Document document,
            GroupItem targetFolder,
            string folderPath,
            string viewpointName)
        {
            return SaveCurrentResetViewpointAtStart(document, targetFolder, folderPath, viewpointName, true, true);
        }

        public static SavedViewpoint SaveCurrentResetViewpointAtStart(
            Document document,
            GroupItem targetFolder,
            string folderPath,
            string viewpointName,
            bool captureMaterialAttributes,
            bool captureHideAttributes)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.CurrentViewpoint == null)
                throw new InvalidOperationException("Current viewpoint is unavailable.");
            if (document.SavedViewpoints == null)
                throw new InvalidOperationException("Saved viewpoints are unavailable.");
            if (targetFolder == null)
                throw new ArgumentNullException(nameof(targetFolder));
            if (string.IsNullOrWhiteSpace(viewpointName))
                throw new ArgumentException("Viewpoint name is required.", nameof(viewpointName));

            SaveCurrentViewWithCom(document, folderPath, viewpointName, captureMaterialAttributes, captureHideAttributes);

            var refreshedFolder = ResolveNetFolder(document.SavedViewpoints.RootItem, folderPath) ?? targetFolder;
            var inserted = refreshedFolder.Children
                .OfType<SavedViewpoint>()
                .LastOrDefault(item => string.Equals(item.DisplayName, viewpointName, StringComparison.OrdinalIgnoreCase));

            if (inserted == null)
                throw new InvalidOperationException("Created reset saved viewpoint was not found: " + viewpointName);

            MoveSavedViewpointToStart(document, refreshedFolder, inserted);
            return inserted;
        }

        private static void MoveSavedViewpointToStart(Document document, GroupItem folder, SavedViewpoint savedViewpoint)
        {
            if (document == null || document.SavedViewpoints == null || folder == null || savedViewpoint == null)
                return;

            var index = -1;
            for (var i = 0; i < folder.Children.Count; i++)
            {
                if (ReferenceEquals(folder.Children[i], savedViewpoint))
                {
                    index = i;
                    break;
                }
            }

            if (index > 0)
                document.SavedViewpoints.Move(folder, index, folder, 0);
        }

        private static void SaveCurrentViewWithCom(
            Document document,
            string folderPath,
            string viewpointName,
            bool captureMaterialAttributes,
            bool captureHideAttributes)
        {
            var state = ComApiBridge.State;
            if (state == null)
                throw new InvalidOperationException("Navisworks COM state is unavailable.");

            var savedView = state.ObjectFactory(ComApi.nwEObjectType.eObjectType_nwOpView) as ComApi.InwOpView;
            if (savedView == null)
                throw new InvalidOperationException("Unable to create COM saved viewpoint.");

            savedView.name = viewpointName;
            savedView.ApplyMaterialAttribs = captureMaterialAttributes;
            savedView.ApplyHideAttribs = captureHideAttributes;

            var currentView = state.CurrentView as ComApi.InwOpAnonView;
            if (currentView == null || currentView.ViewPoint == null)
                throw new InvalidOperationException("Current COM view is unavailable.");

            savedView.anonview.ViewPoint = (ComApi.InwNvViewPoint)currentView.ViewPoint.Copy();

            var segments = SplitFolderPath(folderPath);
            if (segments.Length == 0)
            {
                state.SavedViews().Add(savedView);
                return;
            }

            var folder = FindComFolder(state, segments);
            if (folder == null)
                throw new InvalidOperationException("COM saved viewpoint folder was not found: " + string.Join("/", segments));

            folder.SavedViews().Add(savedView);
        }

        private static ComApi.InwOpFolderView FindComFolder(ComApi.InwOpState state, string[] segments)
        {
            ComApi.InwOpFolderView currentFolder = null;
            var currentCollection = state.SavedViews();

            foreach (var segment in segments)
            {
                ComApi.InwOpFolderView nextFolder = null;
                foreach (ComApi.InwOpSavedView item in currentCollection)
                {
                    if (item.Type == ComApi.nwESavedViewType.eSavedViewType_Folder &&
                        string.Equals(item.name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        nextFolder = item as ComApi.InwOpFolderView;
                        break;
                    }
                }

                if (nextFolder == null)
                    return null;

                currentFolder = nextFolder;
                currentCollection = currentFolder.SavedViews();
            }

            return currentFolder;
        }

        private static GroupItem ResolveNetFolder(GroupItem rootItem, string folderPath)
        {
            if (rootItem == null)
                return null;

            var segments = SplitFolderPath(folderPath);
            if (segments.Length == 0)
                return rootItem;

            GroupItem current = rootItem;
            foreach (var segment in segments)
            {
                var next = current.Children
                    .OfType<GroupItem>()
                    .FirstOrDefault(item => string.Equals(item.DisplayName, segment, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                    return null;

                current = next;
            }

            return current;
        }

        private static SavedViewpoint FindSavedViewpointWithRetry(GroupItem targetFolder, string viewpointName)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var savedViewpoint = FindSavedViewpoint(targetFolder, viewpointName);
                if (savedViewpoint != null)
                    return savedViewpoint;

                Thread.Sleep(25);
            }

            return null;
        }

        private static SavedViewpoint FindSavedViewpoint(GroupItem targetFolder, string viewpointName)
        {
            if (targetFolder == null)
                return null;

            return targetFolder.Children
                .OfType<SavedViewpoint>()
                .LastOrDefault(item => string.Equals(item.DisplayName, viewpointName, StringComparison.OrdinalIgnoreCase));
        }

        private static string[] SplitFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return new string[0];

            return folderPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();
        }
    }
}
