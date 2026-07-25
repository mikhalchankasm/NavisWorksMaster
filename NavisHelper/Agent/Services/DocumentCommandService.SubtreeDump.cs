using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int MaxSynchronousDumpItems = 5000;
        private const int MaxSynchronousDumpElapsedMs = 25000;
        private const int MaxDumpSubtreeNameJobs = 8;
        private const int MaxDumpPendingItems = 250000;
        private readonly Dictionary<string, DumpSubtreeNamesJob> _dumpSubtreeNamesJobs =
            new Dictionary<string, DumpSubtreeNamesJob>(StringComparer.OrdinalIgnoreCase);

        public DumpSubtreeNamesResponse DumpSubtreeNames(Document document, DumpSubtreeNamesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var outputPath = (request.OutputPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath is required.");

            var format = NormalizeDumpSubtreeNamesFormat(request.Format);
            var rootItem = ResolveDumpRootItem(document, request);
            var includePath = request.IncludePath.GetValueOrDefault(true);
            var includeSourceFile = request.IncludeSourceFile.GetValueOrDefault(false);
            var includeHidden = request.IncludeHidden.GetValueOrDefault(true);
            var partialOutputPath = outputPath + ".partial";

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            EnsureNoRunningDumpJobForOutput(outputPath, partialOutputPath);

            if (File.Exists(outputPath) && request.Overwrite != true)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Output file already exists. Pass overwrite=true to replace it.");
            if (File.Exists(partialOutputPath))
            {
                if (request.Overwrite != true)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Partial output file already exists. Pass overwrite=true to replace it.");
                File.Delete(partialOutputPath);
            }

            var writtenCount = 0;
            var skippedHiddenCount = 0;
            var processedCount = 0;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var writer = new StreamWriter(partialOutputPath, false, new UTF8Encoding(true)))
                {
                    if (string.Equals(format, SubtreeDumpOutputFormatter.CsvFormat, StringComparison.OrdinalIgnoreCase))
                        WriteDumpSubtreeNamesCsvHeader(writer, includePath, includeSourceFile);

                    var pending = new Stack<DumpTraversalFrame>();
                    pending.Push(new DumpTraversalFrame(rootItem, 0));
                    while (pending.Count > 0)
                    {
                        if (processedCount >= MaxSynchronousDumpItems || stopwatch.ElapsedMilliseconds >= MaxSynchronousDumpElapsedMs)
                            throw new AgentCommandException(
                                ErrorCodes.CommandFailed,
                                "Synchronous dump limit exceeded. Use start_subtree_names_dump and poll dump_subtree_names_status for large roots.");

                        var frame = pending.Pop();
                        if (!includeHidden && frame.Item.IsHidden)
                        {
                            skippedHiddenCount++;
                        }
                        else
                        {
                            if (string.Equals(format, SubtreeDumpOutputFormatter.CsvFormat, StringComparison.OrdinalIgnoreCase))
                                WriteDumpSubtreeNamesCsvRow(writer, frame.Item, includePath, includeSourceFile, frame.Depth);
                            else
                                WriteDumpSubtreeNamesJsonlRow(writer, frame.Item, includePath, includeSourceFile, frame.Depth);

                            writtenCount++;
                        }

                        PushDumpChildren(pending, frame.Item, frame.Depth + 1);
                        processedCount++;
                    }
                }

                CommitDumpOutputFile(partialOutputPath, outputPath, request.Overwrite == true);
            }
            catch
            {
                TryDeleteFile(partialOutputPath);
                throw;
            }

            var fileInfo = new FileInfo(outputPath);
            return new DumpSubtreeNamesResponse
            {
                OutputPath = outputPath,
                Format = format,
                RootName = GetItemDisplayName(rootItem),
                RootPath = BuildItemPath(rootItem),
                RootSourceFile = TryGetSourceFile(rootItem) ?? string.Empty,
                ItemCount = writtenCount,
                SkippedHiddenItemCount = skippedHiddenCount,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            };
        }

        public DumpSubtreeNamesJobStatusResponse StartSubtreeNamesDump(Document document, DumpSubtreeNamesRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            CleanupCompletedDumpJobs();

            var outputPath = (request.OutputPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "outputPath is required.");

            var format = NormalizeDumpSubtreeNamesFormat(request.Format);
            var rootItem = ResolveDumpRootItem(document, request);
            var includePath = request.IncludePath.GetValueOrDefault(true);
            var includeSourceFile = request.IncludeSourceFile.GetValueOrDefault(false);
            var includeHidden = request.IncludeHidden.GetValueOrDefault(true);
            var partialOutputPath = outputPath + ".partial";

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            EnsureNoRunningDumpJobForOutput(outputPath, partialOutputPath);

            if (File.Exists(outputPath) && request.Overwrite != true)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Output file already exists. Pass overwrite=true to replace it.");
            if (File.Exists(partialOutputPath))
            {
                if (request.Overwrite != true)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Partial output file already exists. Pass overwrite=true to replace it.");
                File.Delete(partialOutputPath);
            }

            var writer = new StreamWriter(partialOutputPath, false, new UTF8Encoding(true));
            try
            {
                if (string.Equals(format, SubtreeDumpOutputFormatter.CsvFormat, StringComparison.OrdinalIgnoreCase))
                    WriteDumpSubtreeNamesCsvHeader(writer, includePath, includeSourceFile);

                var job = new DumpSubtreeNamesJob
                {
                    JobId = "dump-" + Guid.NewGuid().ToString("N"),
                    State = DumpSubtreeNamesJobStates.Running,
                    OutputPath = outputPath,
                    PartialOutputPath = partialOutputPath,
                    Format = format,
                    RootName = GetItemDisplayName(rootItem),
                    RootPath = BuildItemPath(rootItem),
                    RootSourceFile = TryGetSourceFile(rootItem) ?? string.Empty,
                    IncludePath = includePath,
                    IncludeSourceFile = includeSourceFile,
                    IncludeHidden = includeHidden,
                    Overwrite = request.Overwrite == true,
                    Writer = writer,
                    StartedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    DocumentKey = BuildDumpDocumentKey(document),
                };
                job.Pending.Push(new DumpTraversalFrame(rootItem, 0));

                lock (_dumpSubtreeNamesJobs)
                {
                    TrimDumpJobsForCapacity();
                    if (_dumpSubtreeNamesJobs.Count >= MaxDumpSubtreeNameJobs)
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Too many active dump jobs. Wait for existing jobs or cancel one.");

                    _dumpSubtreeNamesJobs[job.JobId] = job;
                }

                writer = null;
                return BuildDumpJobStatus(job);
            }
            finally
            {
                if (writer != null)
                {
                    writer.Dispose();
                    try
                    {
                        if (File.Exists(partialOutputPath))
                            File.Delete(partialOutputPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to delete partial subtree dump file after job start failure: " + ex.Message, "SubtreeDumpMcp");
                    }
                }
            }
        }

        public DumpSubtreeNamesJobStatusResponse DumpSubtreeNamesStatus(Document document, DumpSubtreeNamesStatusRequest request)
        {
            CleanupCompletedDumpJobs();

            if (request == null || string.IsNullOrWhiteSpace(request.JobId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "jobId is required.");

            DumpSubtreeNamesJob job;
            lock (_dumpSubtreeNamesJobs)
                job = GetDumpJobLocked(request.JobId);

            lock (job.SyncRoot)
            {
                if (string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (document == null)
                            throw new AgentCommandException(ErrorCodes.NoActiveDocument, "There is no active document.");

                        var currentDocumentKey = BuildDumpDocumentKey(document);
                        if (!string.Equals(currentDocumentKey, job.DocumentKey, StringComparison.Ordinal))
                            throw new AgentCommandException(ErrorCodes.CommandFailed, "Active document changed while dump job was running.");

                        AdvanceDumpSubtreeNamesJob(job, SubtreeDumpJobPolicy.NormalizeMaxItemsPerPoll(request.MaxItemsPerPoll), SubtreeDumpJobPolicy.NormalizeMaxElapsedMs(request.MaxElapsedMs));
                    }
                    catch (Exception ex)
                    {
                        FailDumpSubtreeNamesJob(job, ex.Message);
                    }
                }

                return BuildDumpJobStatus(job);
            }
        }

        private void EnsureNoRunningDumpJobForOutput(string outputPath, string partialOutputPath)
        {
            lock (_dumpSubtreeNamesJobs)
            {
                var conflictingJob = _dumpSubtreeNamesJobs.Values.FirstOrDefault(job =>
                    string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase) &&
                    (DumpPathsEqual(job.OutputPath, outputPath) || DumpPathsEqual(job.PartialOutputPath, partialOutputPath)));

                if (conflictingJob != null)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "A subtree dump job is already writing to this output path. Poll or cancel jobId=" + conflictingJob.JobId + ".");
                }
            }
        }

        private static bool DumpPathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            try
            {
                left = Path.GetFullPath(left);
                right = Path.GetFullPath(right);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to normalize subtree dump paths for conflict comparison: " + ex.Message, "SubtreeDumpMcp");
            }

            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public DumpSubtreeNamesJobStatusResponse CancelSubtreeNamesDump(CancelSubtreeNamesDumpRequest request)
        {
            CleanupCompletedDumpJobs();

            if (request == null || string.IsNullOrWhiteSpace(request.JobId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "jobId is required.");

            DumpSubtreeNamesJob job;
            lock (_dumpSubtreeNamesJobs)
                job = GetDumpJobLocked(request.JobId);

            lock (job.SyncRoot)
            {
                if (string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                    CancelDumpSubtreeNamesJob(job);

                return BuildDumpJobStatus(job);
            }
        }

        private static string NormalizeDumpSubtreeNamesFormat(string format)
        {
            try
            {
                return SubtreeDumpOutputFormatter.NormalizeFormat(format);
            }
            catch (ArgumentException)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, SubtreeDumpOutputFormatter.InvalidFormatMessage);
            }
        }

        public void FailRunningSubtreeNameDumps(string reason)
        {
            List<DumpSubtreeNamesJob> jobs;
            lock (_dumpSubtreeNamesJobs)
            {
                jobs = _dumpSubtreeNamesJobs.Values
                    .Where(job => string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            foreach (var job in jobs)
            {
                lock (job.SyncRoot)
                {
                    if (string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                        FailDumpSubtreeNamesJob(job, reason);
                }
            }
        }

        private DumpSubtreeNamesJob GetDumpJobLocked(string jobId)
        {
            DumpSubtreeNamesJob job;
            if (_dumpSubtreeNamesJobs.TryGetValue(jobId.Trim(), out job))
                return job;

            throw new AgentCommandException(ErrorCodes.CommandFailed, "Dump job was not found.");
        }

        private void CleanupCompletedDumpJobs()
        {
            lock (_dumpSubtreeNamesJobs)
            {
                if (_dumpSubtreeNamesJobs.Count == 0)
                    return;

                var nowUtc = DateTime.UtcNow;
                var removableJobIds = new List<string>();
                foreach (var pair in _dumpSubtreeNamesJobs.ToList())
                {
                    var job = pair.Value;
                    lock (job.SyncRoot)
                    {
                        if (SubtreeDumpJobPolicy.IsCompletedJobExpired(job.CompletedAtUtc, nowUtc))
                        {
                            removableJobIds.Add(pair.Key);
                        }
                        else if (SubtreeDumpJobPolicy.IsRunningJobExpired(job.State, job.UpdatedAtUtc, nowUtc))
                        {
                            FailDumpSubtreeNamesJob(job, "Dump job expired without polling.");
                            removableJobIds.Add(pair.Key);
                        }
                    }
                }

                foreach (var jobId in removableJobIds)
                    _dumpSubtreeNamesJobs.Remove(jobId);
            }
        }

        private void TrimDumpJobsForCapacity()
        {
            if (_dumpSubtreeNamesJobs.Count < MaxDumpSubtreeNameJobs)
                return;

            var removableJobIds = _dumpSubtreeNamesJobs
                .Where(pair => !string.Equals(pair.Value.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Value.CompletedAtUtc ?? pair.Value.UpdatedAtUtc)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var jobId in removableJobIds)
            {
                if (_dumpSubtreeNamesJobs.Count < MaxDumpSubtreeNameJobs)
                    break;

                _dumpSubtreeNamesJobs.Remove(jobId);
            }
        }

        private static string BuildDumpDocumentKey(Document document)
        {
            if (document == null)
                return string.Empty;

            return (document.FileName ?? string.Empty).Trim();
        }

        private static void AdvanceDumpSubtreeNamesJob(DumpSubtreeNamesJob job, int maxItemsPerPoll, int maxElapsedMs)
        {
            if (job == null || !string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                return;

            var stopwatch = Stopwatch.StartNew();
            var processedThisPoll = 0;
            while (job.Pending.Count > 0 && processedThisPoll < maxItemsPerPoll && stopwatch.ElapsedMilliseconds < maxElapsedMs)
            {
                var frame = job.Pending.Pop();
                WriteDumpSubtreeNamesJobItem(job, frame.Item, frame.Depth);
                PushDumpChildren(job.Pending, frame.Item, frame.Depth + 1);
                job.ProcessedItemCount++;
                processedThisPoll++;
            }

            job.UpdatedAtUtc = DateTime.UtcNow;
            FlushDumpJobWriter(job);

            if (job.Pending.Count == 0)
                CompleteDumpSubtreeNamesJob(job);
        }

        private static void WriteDumpSubtreeNamesJobItem(DumpSubtreeNamesJob job, ModelItem item, int depth)
        {
            if (job == null || item == null)
                return;

            if (!job.IncludeHidden && item.IsHidden)
            {
                job.SkippedHiddenItemCount++;
                return;
            }

            if (string.Equals(job.Format, SubtreeDumpOutputFormatter.CsvFormat, StringComparison.OrdinalIgnoreCase))
                WriteDumpSubtreeNamesCsvRow(job.Writer, item, job.IncludePath, job.IncludeSourceFile, depth);
            else
                WriteDumpSubtreeNamesJsonlRow(job.Writer, item, job.IncludePath, job.IncludeSourceFile, depth);

            job.ItemCount++;
        }

        private static void PushDumpChildren(Stack<DumpTraversalFrame> pending, ModelItem item, int childDepth)
        {
            if (pending == null || item == null || item.Children == null)
                return;

            var children = item.Children.Cast<ModelItem>().ToList();
            for (var index = children.Count - 1; index >= 0; index--)
            {
                var child = children[index];
                pending.Push(new DumpTraversalFrame(child, childDepth));
                if (pending.Count > MaxDumpPendingItems)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Dump traversal queue is too large. Use a more specific root item.");
            }
        }

        private static void CompleteDumpSubtreeNamesJob(DumpSubtreeNamesJob job)
        {
            if (job == null || !string.Equals(job.State, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase))
                return;

            CloseDumpJobWriter(job);
            CommitDumpOutputFile(job.PartialOutputPath, job.OutputPath, job.Overwrite);
            job.State = DumpSubtreeNamesJobStates.Done;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.UpdatedAtUtc = job.CompletedAtUtc.Value;
            job.FileSizeBytes = GetDumpJobFileSize(job);
            ClearDumpJobPending(job);
        }

        private static void FailDumpSubtreeNamesJob(DumpSubtreeNamesJob job, string errorMessage)
        {
            if (job == null)
                return;

            job.State = DumpSubtreeNamesJobStates.Failed;
            job.ErrorMessage = errorMessage ?? string.Empty;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.UpdatedAtUtc = job.CompletedAtUtc.Value;
            CloseDumpJobWriter(job);
            ClearDumpJobPending(job);
            TryDeleteFile(job.PartialOutputPath);
            job.FileSizeBytes = GetDumpJobFileSize(job);
        }

        private static void CancelDumpSubtreeNamesJob(DumpSubtreeNamesJob job)
        {
            job.State = DumpSubtreeNamesJobStates.Cancelled;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.UpdatedAtUtc = job.CompletedAtUtc.Value;
            CloseDumpJobWriter(job);
            ClearDumpJobPending(job);
            TryDeleteFile(job.PartialOutputPath);
            job.FileSizeBytes = GetDumpJobFileSize(job);
        }

        private static void ClearDumpJobPending(DumpSubtreeNamesJob job)
        {
            if (job != null && job.Pending != null)
                job.Pending.Clear();
        }

        private static void CommitDumpOutputFile(string partialOutputPath, string outputPath, bool overwrite)
        {
            if (File.Exists(outputPath))
            {
                if (!overwrite)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Output file already exists at completion time.");

                File.Replace(partialOutputPath, outputPath, null, true);
            }
            else
            {
                File.Move(partialOutputPath, outputPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to delete subtree dump file '" + (path ?? string.Empty) + "': " + ex.Message, "SubtreeDumpMcp");
            }
        }

        private static void FlushDumpJobWriter(DumpSubtreeNamesJob job)
        {
            if (job == null || job.Writer == null)
                return;

            job.Writer.Flush();
            job.FileSizeBytes = GetDumpJobFileSize(job);
        }

        private static void CloseDumpJobWriter(DumpSubtreeNamesJob job)
        {
            if (job == null || job.Writer == null)
                return;

            try
            {
                job.Writer.Flush();
            }
            finally
            {
                job.Writer.Dispose();
                job.Writer = null;
            }
        }

        private static long GetDumpJobFileSize(DumpSubtreeNamesJob job)
        {
            if (job == null)
                return 0;

            var path = string.Equals(job.State, DumpSubtreeNamesJobStates.Done, StringComparison.OrdinalIgnoreCase)
                ? job.OutputPath
                : job.PartialOutputPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            return new FileInfo(path).Length;
        }

        private static DumpSubtreeNamesJobStatusResponse BuildDumpJobStatus(DumpSubtreeNamesJob job)
        {
            var nowUtc = DateTime.UtcNow;
            return SubtreeDumpJobPolicy.BuildStatus(new SubtreeDumpJobStatusValues
            {
                JobId = job.JobId,
                State = job.State,
                OutputPath = job.OutputPath,
                PartialOutputPath = job.PartialOutputPath,
                Format = job.Format,
                RootName = job.RootName,
                RootPath = job.RootPath,
                RootSourceFile = job.RootSourceFile,
                ItemCount = job.ItemCount,
                SkippedHiddenItemCount = job.SkippedHiddenItemCount,
                ProcessedItemCount = job.ProcessedItemCount,
                PendingItemCount = job.Pending == null ? 0 : job.Pending.Count,
                FileSizeBytes = GetDumpJobFileSize(job),
                StartedAtUtc = job.StartedAtUtc,
                UpdatedAtUtc = job.UpdatedAtUtc,
                CompletedAtUtc = job.CompletedAtUtc,
                ErrorMessage = job.ErrorMessage,
            }, nowUtc);
        }

        private static ModelItem ResolveDumpRootItem(Document document, DumpSubtreeNamesRequest request)
        {
            var rootName = (request.RootName ?? string.Empty).Trim();
            var sourceFile = (request.SourceFile ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rootName) && string.IsNullOrWhiteSpace(sourceFile))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "rootName or sourceFile is required.");

            var matches = BuildDumpRootCandidates(document, rootName, sourceFile)
                .Select(candidate => candidate.Item)
                .Distinct()
                .ToList();
            matches = PreferMostSpecificDumpRootMatches(matches);

            if (matches.Count == 0)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Root item was not found by rootName/sourceFile.");

            if (matches.Count > 1)
            {
                var preview = string.Join("; ", matches.Take(10).Select(BuildItemPath).ToArray());
                throw new AgentCommandException(ErrorCodes.CommandFailed, "More than one root item matched. Use a more specific rootName or sourceFile. Matches: " + preview);
            }

            return matches[0];
        }

        private static List<ModelItem> PreferMostSpecificDumpRootMatches(IList<ModelItem> matches)
        {
            if (matches == null || matches.Count <= 1)
                return matches == null ? new List<ModelItem>() : matches.ToList();

            return matches
                .Where(candidate => !matches.Any(other => !ReferenceEquals(candidate, other) && IsDumpRootAncestorOf(candidate, other)))
                .ToList();
        }

        private static bool IsDumpRootAncestorOf(ModelItem ancestor, ModelItem item)
        {
            var current = item == null ? null : item.Parent;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = current.Parent;
            }

            return false;
        }

        private static List<DumpRootCandidate> BuildDumpRootCandidates(Document document, string rootName, string sourceFile)
        {
            var result = new List<DumpRootCandidate>();
            if (document == null || document.Models == null)
                return result;

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Model model in document.Models)
            {
                if (model == null || model.RootItem == null)
                    continue;

                AddMatchingDumpRootCandidate(result, model.RootItem, seenPaths, rootName, sourceFile);
                foreach (ModelItem child in model.RootItem.Children)
                    AddMatchingDumpRootCandidate(result, child, seenPaths, rootName, sourceFile);
            }

            if (result.Count > 0)
                return result;

            foreach (Model model in document.Models)
            {
                if (model == null || model.RootItem == null)
                    continue;

                AddDumpRootCandidatesRecursive(result, model.RootItem, seenPaths, rootName, sourceFile);
            }

            return result;
        }

        private static bool AddMatchingDumpRootCandidate(
            ICollection<DumpRootCandidate> candidates,
            ModelItem item,
            ISet<string> seenPaths,
            string rootName,
            string sourceFile)
        {
            if (item == null || !IsDumpRootCandidate(item))
                return false;

            var candidate = CreateDumpRootCandidate(item);
            if ((!string.IsNullOrWhiteSpace(rootName) && DumpRootCandidateMatches(candidate, rootName)) ||
                (!string.IsNullOrWhiteSpace(sourceFile) && DumpRootCandidateMatches(candidate, sourceFile)))
            {
                AddDumpRootCandidate(candidates, candidate, seenPaths);
                return true;
            }

            return false;
        }

        private static void AddDumpRootCandidatesRecursive(
            ICollection<DumpRootCandidate> candidates,
            ModelItem item,
            ISet<string> seenPaths,
            string rootName,
            string sourceFile)
        {
            if (item == null)
                return;

            if (AddMatchingDumpRootCandidate(candidates, item, seenPaths, rootName, sourceFile))
                return;

            foreach (ModelItem child in item.Children)
                AddDumpRootCandidatesRecursive(candidates, child, seenPaths, rootName, sourceFile);
        }

        private static bool IsDumpRootCandidate(ModelItem item)
        {
            if (item == null)
                return false;

            var ownSourceFile = TryGetOwnSourceFile(item);
            return SubtreeDumpRootRules.IsCandidate(ownSourceFile, item.DisplayName);
        }

        private static DumpRootCandidate CreateDumpRootCandidate(ModelItem item)
        {
            var sourceFile = TryGetOwnSourceFile(item);
            return new DumpRootCandidate
            {
                Item = item,
                Aliases = SubtreeDumpRootRules.BuildAliases(item.DisplayName, sourceFile),
            };
        }

        private static void AddDumpRootCandidate(ICollection<DumpRootCandidate> candidates, DumpRootCandidate candidate, ISet<string> seenPaths)
        {
            if (candidates == null || candidate == null || candidate.Item == null)
                return;

            var path = BuildItemPath(candidate.Item);
            if (seenPaths != null && !seenPaths.Add(path))
                return;

            candidates.Add(candidate);
        }

        private static bool DumpRootCandidateMatches(DumpRootCandidate candidate, string value)
        {
            if (candidate == null)
                return false;

            return SubtreeDumpRootRules.Matches(candidate.Aliases, value);
        }

        private static string TryGetOwnSourceFile(ModelItem item)
        {
            var sourceFileProperty = TryFindSourceFileProperty(item);
            return sourceFileProperty == null ? string.Empty : GetPropertyDisplayValue(sourceFileProperty);
        }

        private static void WriteDumpSubtreeNamesCsvHeader(TextWriter writer, bool includePath, bool includeSourceFile)
        {
            writer.WriteLine(SubtreeDumpOutputFormatter.BuildCsvHeader(includePath, includeSourceFile));
        }

        private static void WriteDumpSubtreeNamesCsvRow(TextWriter writer, ModelItem item, bool includePath, bool includeSourceFile, int depth)
        {
            var row = CreateDumpOutputRow(item, includePath, includeSourceFile, depth);
            writer.WriteLine(SubtreeDumpOutputFormatter.BuildCsvRow(row, includePath, includeSourceFile));
        }

        private static void WriteDumpSubtreeNamesJsonlRow(TextWriter writer, ModelItem item, bool includePath, bool includeSourceFile, int depth)
        {
            var row = CreateDumpOutputRow(item, includePath, includeSourceFile, depth);
            writer.WriteLine(JsonConvert.SerializeObject(
                SubtreeDumpOutputFormatter.BuildJsonRow(row, includePath, includeSourceFile),
                Formatting.None));
        }

        private static SubtreeDumpOutputRow CreateDumpOutputRow(ModelItem item, bool includePath, bool includeSourceFile, int depth)
        {
            return new SubtreeDumpOutputRow
            {
                Name = GetItemDisplayName(item),
                DisplayName = item.DisplayName ?? string.Empty,
                Depth = depth,
                IsHidden = item.IsHidden,
                Path = includePath ? BuildItemPath(item) : null,
                SourceFile = includeSourceFile ? TryGetSourceFile(item) ?? string.Empty : null,
            };
        }

        private sealed class DumpRootCandidate
        {
            public ModelItem Item { get; set; }
            public List<string> Aliases { get; set; }
        }

        private sealed class DumpTraversalFrame
        {
            public DumpTraversalFrame(ModelItem item, int depth)
            {
                Item = item;
                Depth = depth;
            }

            public ModelItem Item { get; private set; }
            public int Depth { get; private set; }
        }

        private sealed class DumpSubtreeNamesJob
        {
            public string JobId { get; set; }
            public string State { get; set; }
            public string OutputPath { get; set; }
            public string PartialOutputPath { get; set; }
            public string Format { get; set; }
            public string RootName { get; set; }
            public string RootPath { get; set; }
            public string RootSourceFile { get; set; }
            public bool IncludePath { get; set; }
            public bool IncludeSourceFile { get; set; }
            public bool IncludeHidden { get; set; }
            public bool Overwrite { get; set; }
            public string DocumentKey { get; set; }
            public object SyncRoot { get; private set; } = new object();
            public Stack<DumpTraversalFrame> Pending { get; private set; } = new Stack<DumpTraversalFrame>();
            public StreamWriter Writer { get; set; }
            public int ItemCount { get; set; }
            public int SkippedHiddenItemCount { get; set; }
            public int ProcessedItemCount { get; set; }
            public long FileSizeBytes { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}
