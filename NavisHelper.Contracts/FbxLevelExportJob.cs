using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>Visibility and filesystem operations supplied by the export host.</summary>
    public interface IFbxExportHost
    {
        void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden);
        string Export(string path);
        bool FileExists(string path);
        long FileSize(string path);
        long FreeBytes(string folder);
    }

    /// <summary>Exports one planned item per step, restoring visibility before returning.</summary>
    public sealed class FbxLevelExportJob
    {
        public const int StatusFileLimit = 20;

        private readonly object _gate = new object();
        private readonly FbxLevelExportPlan _plan;
        private readonly string _folder;
        private readonly bool _overwrite;
        private readonly DateTime _startedAtUtc;
        private readonly List<FbxLevelExportFile> _files = new List<FbxLevelExportFile>();
        private string _state = FbxLevelExportJobStates.Running;
        private string _errorMessage;
        private DateTime? _completedAtUtc;
        private int _nextItem;
        private long _largestFileBytes;
        private bool _cancelRequested;
        private bool _stepInProgress;

        private FbxLevelExportJob(string operationId, FbxLevelExportPlan plan,
            string folder, bool overwrite, DateTime nowUtc)
        {
            OperationId = operationId;
            _plan = plan;
            _folder = folder;
            _overwrite = overwrite;
            _startedAtUtc = nowUtc;
        }

        public string OperationId { get; }

        public string State
        {
            get { lock (_gate) return _state; }
        }

        public bool IsRunning
        {
            get { lock (_gate) return _state == FbxLevelExportJobStates.Running; }
        }

        public static FbxLevelExportJob Start(string operationId, FbxLevelExportPlan plan,
            string folder, bool overwrite, IFbxExportHost host, FbxLevelExportJob previous,
            DateTime nowUtc)
        {
            if (previous != null && previous.IsRunning)
                throw new InvalidOperationException("An FBX export job is already running.");
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("operationId is required.", nameof(operationId));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            if (!overwrite)
            {
                foreach (FbxLevelExportPlanItem item in plan.Items)
                    if (host.FileExists(Path.Combine(folder, item.FileName)))
                        throw new InvalidOperationException(item.FileName
                            + " already exists; enable overwrite to replace it.");
            }

            return new FbxLevelExportJob(operationId, plan, folder, overwrite, nowUtc);
        }

        /// <summary>Exports at most one item. <paramref name="utcNow"/> is read at the start and
        /// again after the export, so a long export counts toward the completion time.</summary>
        public bool Step(IFbxExportHost host, Func<DateTime> utcNow)
        {
            if (utcNow == null)
                throw new ArgumentNullException(nameof(utcNow));
            DateTime nowUtc = utcNow();
            FbxLevelExportPlanItem item;
            long largestFileBytes;
            lock (_gate)
            {
                if (_state != FbxLevelExportJobStates.Running)
                    return false;
                // A reentrant or concurrent Step cannot export the same item twice.
                if (_stepInProgress)
                    return true;
                if (_cancelRequested)
                {
                    Finish(FbxLevelExportJobStates.Canceled, nowUtc);
                    return false;
                }
                if (_nextItem == _plan.Items.Count)
                {
                    Finish(FbxLevelExportJobStates.Completed, nowUtc);
                    return false;
                }
                item = _plan.Items[_nextItem];
                largestFileBytes = _largestFileBytes;
                _stepInProgress = true;
            }

            string error = null;
            string path = Path.Combine(_folder, item.FileName);
            long bytes = 0;
            var timer = Stopwatch.StartNew();
            try
            {
                // Start checked the targets, but a file may appear while the job runs.
                if (!_overwrite && host.FileExists(path))
                    error = "already exists; enable overwrite to replace it.";
                else if (!FbxLevelExportPlan.HasRoomForNext(host.FreeBytes(_folder), largestFileBytes))
                    error = "Insufficient free space for the next export.";
                else
                {
                    IReadOnlyList<IFbxTreeNode> hide = FbxLevelExportPlan.HideSet(item);
                    // IsHidden is live: capture the user's visibility before hiding.
                    IReadOnlyList<IFbxTreeNode> unhide = FbxLevelExportPlan.UnhideSet(hide);
                    try
                    {
                        if (hide.Count > 0)
                            host.SetHidden(hide, true);
                        error = host.Export(path);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    finally
                    {
                        if (unhide.Count > 0)
                            host.SetHidden(unhide, false);
                    }

                    if (error == null)
                        bytes = host.FileSize(path);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                timer.Stop();
            }

            DateTime finishedUtc = utcNow();
            lock (_gate)
            {
                _stepInProgress = false;
                if (error != null)
                {
                    _errorMessage = item.FileName + ": " + error;
                    Finish(FbxLevelExportJobStates.Failed, finishedUtc);
                    return false;
                }

                _files.Add(new FbxLevelExportFile
                {
                    Index = item.Index,
                    Name = item.FileName,
                    Path = path,
                    Bytes = bytes,
                    ElapsedMs = timer.ElapsedMilliseconds
                });
                if (_files.Count > StatusFileLimit)
                    _files.RemoveAt(0);
                _largestFileBytes = Math.Max(_largestFileBytes, bytes);
                _nextItem++;
                if (_nextItem == _plan.Items.Count)
                {
                    Finish(FbxLevelExportJobStates.Completed, finishedUtc);
                    return false;
                }
                return true;
            }
        }

        public bool Cancel(DateTime nowUtc)
        {
            lock (_gate)
            {
                if (_state != FbxLevelExportJobStates.Running || _cancelRequested)
                    return false;
                _cancelRequested = true;
                return true;
            }
        }

        public FbxLevelExportStatusResponse BuildStatus(DateTime nowUtc)
        {
            lock (_gate)
            {
                var files = new List<FbxLevelExportFile>(_files.Count);
                foreach (FbxLevelExportFile file in _files)
                    files.Add(new FbxLevelExportFile
                    {
                        Index = file.Index,
                        Name = file.Name,
                        Path = file.Path,
                        Bytes = file.Bytes,
                        ElapsedMs = file.ElapsedMs
                    });

                bool running = _state == FbxLevelExportJobStates.Running;
                return new FbxLevelExportStatusResponse
                {
                    OperationId = OperationId,
                    State = _state,
                    IsRunning = running,
                    Level = _plan.Level,
                    Folder = _folder,
                    TotalItems = _plan.TotalItems,
                    ExportedItems = _nextItem,
                    SkippedItems = _plan.Skipped.Count,
                    CurrentItem = running && _nextItem < _plan.Items.Count
                        ? _plan.Items[_nextItem].FileName : null,
                    Files = files,
                    ErrorMessage = _errorMessage,
                    StartedAtUtc = _startedAtUtc,
                    CompletedAtUtc = _completedAtUtc,
                    ElapsedMs = (long)((_completedAtUtc ?? nowUtc) - _startedAtUtc).TotalMilliseconds
                };
            }
        }

        // Called only while holding _gate.
        private void Finish(string state, DateTime nowUtc)
        {
            _state = state;
            _completedAtUtc = nowUtc;
        }
    }
}
