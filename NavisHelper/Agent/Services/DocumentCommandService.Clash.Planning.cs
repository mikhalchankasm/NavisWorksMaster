using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public ClashBboxPairPlanResponse ClashBboxPairPlan(Document document, ClashBboxPairPlanRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashBboxPairPlanRequest();
            var apply = request.Apply == true;
            var stopwatch = Stopwatch.StartNew();
            var rootMode = NormalizeClashBboxRootMode(request.RootMode);
            var sourceMode = string.IsNullOrWhiteSpace(request.SourceMode)
                ? ClashBboxOptionHelper.RootModeTopLevelFiles
                : request.SourceMode.Trim().ToLowerInvariant();
            if (sourceMode != ClashBboxOptionHelper.RootModeTopLevelFiles && sourceMode != "selection")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "sourceMode must be top_level_files or selection.");
            var refineDepth = NormalizeClashBboxRefineDepth(request.RefineDepth);
            var toleranceMm = NormalizeNonNegativeDouble(request.BboxToleranceMm.GetValueOrDefault(0), "bboxToleranceMm");
            var toleranceUnits = SectionBoxHelper.MmToDocUnits(toleranceMm);
            var maxRootItems = ClampClashBboxRootItems(request.MaxRootItems);
            var maxCandidatePairs = ClampClashBboxCandidatePairs(request.MaxCandidatePairs);
            var previewLimit = ClampClashBboxPreviewLimit(request.PreviewLimit);
            var includeRejected = request.IncludeRejected == true;
            var targetRootName = (request.TargetRootName ?? string.Empty).Trim();
            var roots = BuildClashBboxRootCandidates(document, request, maxRootItems);
            var targetRootMatchCount = string.IsNullOrWhiteSpace(targetRootName)
                ? 0
                : roots.Items.Count(root => MatchesClashBboxTargetRoot(root == null ? null : root.Info, targetRootName));
            if (!string.IsNullOrWhiteSpace(targetRootName) && targetRootMatchCount == 0)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "targetRootName did not match any included root" +
                    (roots.Truncated ? " in the truncated root list" : string.Empty) +
                    ": " + targetRootName);
            }

            var fullResponse = new ClashBboxPairPlanResponse
            {
                Applied = apply,
                RootMode = rootMode,
                SourceMode = sourceMode,
                TargetRootName = targetRootName,
                TargetRootMatchCount = targetRootMatchCount,
                RefineDepth = refineDepth,
                BboxToleranceMm = toleranceMm,
                TotalRootItems = roots.TotalCount,
                ReturnedRootItems = roots.Items.Count,
                RootItemsTruncated = roots.Truncated,
            };
            fullResponse.RootItems.AddRange(roots.Items.Select(root => root.Info));
            fullResponse.Warnings.AddRange(roots.Warnings);

            var stopByCandidateLimit = false;
            for (var i = 0; i < roots.Items.Count; i++)
            {
                for (var j = i + 1; j < roots.Items.Count; j++)
                {
                    fullResponse.RootPairCount++;
                    var a = roots.Items[i];
                    var b = roots.Items[j];
                    if (!string.IsNullOrWhiteSpace(targetRootName) &&
                        !MatchesClashBboxTargetRoot(a == null ? null : a.Info, targetRootName) &&
                        !MatchesClashBboxTargetRoot(b == null ? null : b.Info, targetRootName))
                    {
                        fullResponse.TargetFilteredPairCount++;
                        continue;
                    }
                    if (IsAncestorOrDescendant(a == null ? null : a.Item, b == null ? null : b.Item))
                    {
                        fullResponse.SkippedPairCount++;
                        IncrementCount(fullResponse.SkippedReasonCounts, "ancestor_descendant");
                        if (includeRejected)
                        {
                            fullResponse.RejectedPairs.Add(new ClashBboxRejectedPair
                            {
                                Index = fullResponse.SkippedPairCount,
                                A = a == null ? null : a.Info,
                                B = b == null ? null : b.Info,
                                Reason = "ancestor_descendant",
                            });
                        }

                        continue;
                    }

                    string reason;
                    string warning;
                    ClashBboxPairRefineStats stats;
                    var accepted = EvaluateClashBboxPair(a, b, refineDepth, toleranceUnits, out reason, out warning, out stats);
                    if (accepted)
                    {
                        fullResponse.CandidatePairCount++;
                        fullResponse.CandidatePairs.Add(new ClashBboxCandidatePair
                        {
                            Index = fullResponse.CandidatePairCount,
                            A = a.Info,
                            B = b.Info,
                            CheckedChildPairCount = stats.CheckedPairCount,
                            ChildIntersectingPairCount = stats.IntersectingPairCount,
                            Reason = reason,
                        });

                        if (fullResponse.CandidatePairCount >= maxCandidatePairs)
                        {
                            stopByCandidateLimit = true;
                            fullResponse.CandidatePairsTruncated = true;
                            fullResponse.Warnings.Add("Candidate pair limit reached; increase maxCandidatePairs to continue scanning.");
                            break;
                        }
                    }
                    else
                    {
                        fullResponse.SkippedPairCount++;
                        IncrementCount(fullResponse.SkippedReasonCounts, reason);
                        if (!string.IsNullOrWhiteSpace(warning))
                            fullResponse.Warnings.Add(warning);
                        if (includeRejected)
                        {
                            fullResponse.RejectedPairs.Add(new ClashBboxRejectedPair
                            {
                                Index = fullResponse.SkippedPairCount,
                                A = a.Info,
                                B = b.Info,
                                Reason = reason,
                                Warning = warning,
                            });
                        }
                    }
                }

                if (stopByCandidateLimit)
                    break;
            }

            stopwatch.Stop();
            fullResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
                fullResponse.OutputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath.Trim()));
            fullResponse.Pairs = fullResponse.CandidatePairs;
            if (apply)
                WriteClashBboxPlanOutput(fullResponse, fullResponse.OutputPath, request.OverwriteExisting == true);

            var response = ClashBboxPlanHelper.BuildPreview(fullResponse, previewLimit, includeRejected);
            response.Pairs = response.CandidatePairs;
            response.Applied = apply;
            return response;
        }

        private static bool MatchesClashBboxTargetRoot(ClashBboxRootItem root, string target)
        {
            if (root == null || string.IsNullOrWhiteSpace(target))
                return false;
            return string.Equals(root.Name, target, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.Path, target, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.SourceFile, target, StringComparison.OrdinalIgnoreCase);
        }
    }
}
