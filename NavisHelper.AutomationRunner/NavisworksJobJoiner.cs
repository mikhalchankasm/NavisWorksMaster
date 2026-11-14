using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace NavisHelper.AutomationRunner
{
    /// <summary>
    /// Puts the Navisworks COM server (Roamer.exe -Embedding, started by DcomLaunch, not by this
    /// runner) into the server's kill-on-close job while Activator.CreateInstance runs.
    /// Two rules keep that job's kill-on-close meaningful and its reach narrow:
    /// - The job handle is opened only around each AssignProcessToJobObject call. Kill-on-close
    ///   fires on the LAST handle, so a handle held through a hung activation would keep the job,
    ///   this runner and Navisworks alive after the server closed its own handle.
    /// - NavisHelper runners activate Navisworks one at a time (a session-wide mutex), so a new
    ///   -Embedding Roamer seen during the window is this runner's and never another export's.
    ///   An activation by a non-NavisHelper Automation client in the same seconds is not covered.
    /// </summary>
    internal sealed class NavisworksJobJoiner : IDisposable
    {
        private const uint JOB_OBJECT_ASSIGN_PROCESS = 0x0001;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_TERMINATE = 0x0001;
        private const string ActivationMutexName = @"Local\NavisHelper-Datasmith-Activation";
        private static readonly TimeSpan ActivationWait = TimeSpan.FromMinutes(5);
        private readonly string _jobName;
        private readonly HashSet<int> _existingBefore = new HashSet<int>();
        private readonly HashSet<int> _joined = new HashSet<int>();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Mutex _activation;
        private Thread _thread;
        private bool _snapshotReady;
        private bool _disposed;

        public NavisworksJobJoiner(string jobName)
        {
            _jobName = jobName;
        }

        /// <summary>Takes the activation turn, then records the Roamer processes that already run.
        /// Throws when another NavisHelper activation holds the turn longer than five minutes.</summary>
        public void Snapshot()
        {
            if (_disposed || _activation != null)
                return;
            var activation = new Mutex(false, ActivationMutexName);
            bool owned;
            try { owned = activation.WaitOne(ActivationWait); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                activation.Dispose();
                throw new InvalidOperationException("Another NavisHelper Navisworks activation did not finish within 5 minutes.");
            }
            _activation = activation;
            try
            {
                _existingBefore.Clear();
                foreach (int id in RunningIds())
                    _existingBefore.Add(id);
                _snapshotReady = true;
            }
            catch (Exception error)
            {
                _snapshotReady = false;
                Report(0, error.Message);
            }
        }

        public void Start()
        {
            if (_disposed || !_snapshotReady || _thread != null)
                return;
            _thread = new Thread(() =>
            {
                do { JoinNewProcesses(); } while (!_stop.WaitOne(100));
            }) { IsBackground = true, Name = "Navisworks job joiner" };
            _thread.Start();
        }

        public void Stop()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _stop.Set();
                if (_thread != null)
                    _thread.Join();
                if (_snapshotReady)
                    JoinNewProcesses();
            }
            finally
            {
                _stop.Dispose();
                if (_activation != null)
                {
                    // Snapshot and Stop run on the runner's main thread, which owns the mutex.
                    _activation.ReleaseMutex();
                    _activation.Dispose();
                }
            }
        }

        public void Dispose() { Stop(); }

        private static IList<int> RunningIds()
        {
            var processes = Process.GetProcessesByName("Roamer");
            try
            {
                var ids = new List<int>();
                foreach (var process in processes)
                    ids.Add(process.Id);
                return ids;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        private void JoinNewProcesses()
        {
            try
            {
                var candidates = new List<NavisworksProcessInfo>();
                foreach (int id in RunningIds())
                {
                    if (_existingBefore.Contains(id) || _joined.Contains(id))
                        continue;
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher(
                            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE ProcessId = " + id.ToString(CultureInfo.InvariantCulture)))
                        using (var rows = searcher.Get())
                        {
                            foreach (ManagementObject row in rows)
                                using (row)
                                    candidates.Add(new NavisworksProcessInfo
                                    {
                                        ProcessId = id, ImageName = "Roamer.exe",
                                        CommandLine = row["CommandLine"] as string,
                                    });
                        }
                    }
                    catch (Exception error) { Report(id, error.Message); }
                }
                foreach (int id in NavisworksProcessSelection.SelectNew(_existingBefore, candidates, _joined))
                {
                    try
                    {
                        using (var process = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, false, id))
                        {
                            if (process.IsInvalid)
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            // Held only for this call; see the class remarks.
                            using (var job = OpenJobObject(JOB_OBJECT_ASSIGN_PROCESS, false, _jobName))
                            {
                                if (job.IsInvalid || !AssignProcessToJobObject(job, process))
                                    throw new Win32Exception(Marshal.GetLastWin32Error());
                            }
                            _joined.Add(id);
                        }
                    }
                    catch (Exception error) { Report(id, error.Message); }
                }
            }
            catch (Exception error) { Report(0, error.Message); }
        }

        private static void Report(int id, string message)
        {
            try { Console.Error.WriteLine("navisworks job: " + id.ToString(CultureInfo.InvariantCulture) + " " + message.Replace('\r', ' ').Replace('\n', ' ')); }
            catch (Exception) { /* Diagnostics must not escape the background thread. */ }
        }

        private sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public NativeHandle() : base(true) { }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern NativeHandle OpenJobObject(uint desiredAccess, bool inheritHandle, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern NativeHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(NativeHandle job, NativeHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
