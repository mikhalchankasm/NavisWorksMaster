using System;
using System.Runtime.InteropServices;

namespace NavisHelper.Core
{
    /// <summary>
    /// Современный диалог выбора папки (Vista-style, выглядит как Open File).
    /// </summary>
    public static class FolderPickerDialog
    {
        public static string Show(string title = "Выберите папку", string initialPath = null)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();

            try
            {
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST);

                if (!string.IsNullOrEmpty(title))
                    dialog.SetTitle(title);

                if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
                {
                    SHCreateItemFromParsingName(initialPath, IntPtr.Zero,
                        typeof(IShellItem).GUID, out IShellItem folder);
                    if (folder != null)
                        dialog.SetFolder(folder);
                }

                var hr = dialog.Show(IntPtr.Zero);
                if (hr != 0) return null; // Cancel

                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                return path;
            }
            catch
            {
                // Fallback на старый диалог
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = title };
                if (!string.IsNullOrEmpty(initialPath))
                    dlg.SelectedPath = initialPath;
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dlg.SelectedPath : null;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        #region COM Interop

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show([In] IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex([In] uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise();
            void Unadvise();
            void SetOptions([In] FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid([In] ref Guid guid);
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName([In] SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_PATHMUSTEXIST = 0x00000800,
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In] string pszPath, [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        #endregion
    }
}
