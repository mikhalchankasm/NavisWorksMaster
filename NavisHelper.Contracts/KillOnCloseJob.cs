using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NavisHelper.Agent.Contracts
{
    public sealed class KillOnCloseJob : IDisposable
    {
        private readonly JobHandle _handle;
        public string Name { get; }

        public KillOnCloseJob(string namePrefix)
        {
            Name = namePrefix + Guid.NewGuid().ToString("N");
            _handle = CreateJobObject(IntPtr.Zero, Name);
            if (_handle.IsInvalid)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                _handle.Dispose();
                throw error;
            }
            var limits = new ExtendedLimitInformation
            {
                BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x2000 },
            };
            if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimitInformation))))
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                _handle.Dispose();
                throw error;
            }
        }

        public void Assign(Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));
            if (!AssignProcessToJobObject(_handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() => _handle.Dispose();

        private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public JobHandle() : base(true) { }
            protected override bool ReleaseHandle() => CloseHandle(handle);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern JobHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(JobHandle job, int informationClass, ref ExtendedLimitInformation information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
