using System;

namespace NavisHelper.Agent.Contracts
{
    [Flags]
    public enum ItemExportFormats
    {
        None = 0,
        Fbx = 1,
        Datasmith = 2
    }

    /// <summary>Publishes isolated copies and exports them without retargeting the open document.</summary>
    public interface IItemExportHost : IFbxExportHost
    {
        string TempFolder { get; }
        /// <summary>Returns null on success, otherwise an error message.</summary>
        string PublishCopy(string path);
        /// <summary>Returns null on success, otherwise an error message.</summary>
        string ExportDatasmith(string sourcePath, string outputPath);
        void DeleteFile(string path);
    }
}
