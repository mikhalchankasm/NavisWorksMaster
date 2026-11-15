using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ModelItemFbxNode : IFbxTreeNode
    {
        internal ModelItemFbxNode(ModelItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        internal ModelItem Item { get; }
        public string Name => Item.DisplayName;
        public string ClassName => Item.ClassDisplayName;
        public bool IsHidden => Item.IsHidden;
        public IFbxTreeNode Parent => Item.Parent == null ? null : new ModelItemFbxNode(Item.Parent);

        public override bool Equals(object obj) => obj is ModelItemFbxNode other && Item.Equals(other.Item);
        public override int GetHashCode() => Item.GetHashCode();

        public IEnumerable<IFbxTreeNode> Children
        {
            get
            {
                // The plan retains these wrappers and their ModelItems for later steps.
                foreach (ModelItem child in Item.Children)
                    yield return new ModelItemFbxNode(child);
            }
        }
    }

    internal sealed class NavisworksFbxExportHost : IFbxExportHost
    {
        private readonly Document _document;

        public NavisworksFbxExportHost(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden)
        {
            var items = new ModelItemCollection();
            foreach (IFbxTreeNode node in nodes)
                items.Add(((ModelItemFbxNode)node).Item);
            _document.Models.SetHidden(items, hidden);
        }

        public string Export(string path)
        {
            var state = ComApiBridge.State;
            var options = state.GetIOPluginOptions("LcFbxExporterPlugin");
            var status = state.DriveIOPlugin("LcFbxExporterPlugin", path, options);
            if (status != nwEExportStatus.eExport_OK)
                return status.ToString();
            return File.Exists(path) ? null : "The FBX exporter wrote no file.";
        }

        public bool FileExists(string path) => File.Exists(path);
        public long FileSize(string path) => new FileInfo(path).Length;

        public long FreeBytes(string folder)
        {
            ulong available, total, free;
            if (!GetDiskFreeSpaceEx(folder, out available, out total, out free))
                return long.MaxValue;
            return available > (ulong)long.MaxValue ? long.MaxValue : (long)available;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directoryName,
            out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
    }
}
