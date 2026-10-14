using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// clash_one_vs_rest: one tree node against each of its siblings. It reads only the
    /// target and its parent's direct children, never the whole tree; the naming and
    /// filtering live in <see cref="ClashOneVsRestPlanner"/>, Selection Sets are written
    /// by create_selection_set's own code, and tests by clash_tests_from_sets's.
    /// </summary>
    internal sealed class ClashOneVsRestService
    {
        private const int DefaultLimit = 200;
        private const int MaximumLimit = 500;

        private readonly SearchService _searchService;
        private readonly DocumentCommandService _commandService;
        private readonly ClashTestsFromSetsService _testsService;
        private readonly MatchSessionStore _sessionStore;

        public ClashOneVsRestService(SearchService searchService, DocumentCommandService commandService,
            ClashTestsFromSetsService testsService, MatchSessionStore sessionStore)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            _testsService = testsService ?? throw new ArgumentNullException(nameof(testsService));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        }

        public ClashOneVsRestResponse Execute(Document document, ClashOneVsRestRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashOneVsRestRequest();
            var apply = request.Apply == true;
            var overwrite = request.OverwriteExisting == true;
            var limit = Math.Max(1, Math.Min(MaximumLimit, request.Limit.GetValueOrDefault(DefaultLimit)));
            if (string.IsNullOrWhiteSpace(request.TargetMatchHandle) && string.IsNullOrWhiteSpace(request.TargetPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "targetMatchHandle or targetPath is required.");

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var target = ResolveTarget(document, request);
            var rest = GetSiblings(document, target, request.IncludeHidden.GetValueOrDefault(true));
            var bboxPrefilter = request.BboxPrefilter == true;

            ClashOneVsRestPlan plan;
            try
            {
                var tolerance = ClashOneVsRestPlanner.ResolveToleranceMm(request.ToleranceMm);
                plan = ClashOneVsRestPlanner.Plan(new ClashOneVsRestPlanInput
                {
                    Target = ToCandidate(target, bboxPrefilter),
                    Rest = rest.Select(item => ToCandidate(item, bboxPrefilter)).ToList(),
                    Mode = request.Mode,
                    IncludeNameMasks = request.IncludeNameMasks ?? new List<string>(),
                    ExcludeNameMasks = request.ExcludeNameMasks ?? new List<string>(),
                    SetFolder = request.SetFolder,
                    PairNameTemplate = request.PairNameTemplate,
                    TestType = request.TestType,
                    ToleranceMm = tolerance,
                    PairOverrides = request.PairOverrides ?? new List<ClashOneVsRestOverride>(),
                    BboxPrefilter = bboxPrefilter,
                    BboxMargin = SectionBoxHelper.MmToDocUnits(tolerance),
                });
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
            if (plan.Pairs.Count > limit)
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    "The plan has " + plan.Pairs.Count.ToString(CultureInfo.InvariantCulture) + " tests, more than limit=" +
                    limit.ToString(CultureInfo.InvariantCulture) + ". Narrow it with includeNameMasks/excludeNameMasks or raise limit (maximum 500).");

            var response = new ClashOneVsRestResponse
            {
                Applied = apply,
                Mode = plan.Mode,
                TargetName = target.DisplayName ?? string.Empty,
                TargetPath = SearchService.PrintItemPath(target),
                ParentPath = target.Parent == null ? string.Empty : SearchService.PrintItemPath(target.Parent),
                SetFolder = plan.SetFolder,
                RestCandidateCount = rest.Count,
                ExcludedByMaskCount = plan.ExcludedByMaskCount,
                BboxSkippedCount = plan.BboxSkippedNames.Count,
                BboxSkippedNames = plan.BboxSkippedNames,
                Pairs = plan.Pairs,
            };
            if (rest.Count == 0)
                response.Warnings.Add("The target has no siblings, so there is nothing to test it against.");
            if (plan.UnknownBoxCount > 0)
                response.Warnings.Add(plan.UnknownBoxCount.ToString(CultureInfo.InvariantCulture) +
                    " node(s) reported no bounding box and were kept by the prefilter.");
            if (plan.Pairs.Count == 0)
            {
                response.Message = "No test to plan: every sibling was excluded.";
                return response;
            }

            var usableSets = PrepareSets(document, plan, target, rest, apply, overwrite, response);
            MarkPairsWithUnusableSets(plan, usableSets, response);
            if (!apply)
                MarkExistingTests(clash, plan, overwrite, response);
            else
                CreateTests(document, plan, overwrite, response);

            response.PlannedTestCount = plan.Pairs.Count(pair => pair.Status == ClashOneVsRestStatuses.Planned);
            response.CreatedTestCount = plan.Pairs.Count(pair => pair.Status == ClashOneVsRestStatuses.Created || pair.Status == ClashOneVsRestStatuses.Replaced);
            response.SkippedTestCount = plan.Pairs.Count - response.PlannedTestCount - response.CreatedTestCount;
            response.CreatedSetCount = response.Sets.Count(set => set.Status == ClashOneVsRestStatuses.Created || set.Status == ClashOneVsRestStatuses.Replaced);
            response.Message = apply
                ? "Created or replaced " + response.CreatedTestCount.ToString(CultureInfo.InvariantCulture) + " test(s) and " +
                  response.CreatedSetCount.ToString(CultureInfo.InvariantCulture) + " Selection Set(s). Tests were not run."
                : "Dry-run only. Pass apply=true to create the Selection Sets and tests; tests are never run by this tool.";
            return response;
        }

        private ModelItem ResolveTarget(Document document, ClashOneVsRestRequest request)
        {
            try
            {
                return _searchService.ResolveSingleItem(document, request.TargetMatchHandle, request.TargetPath, _sessionStore);
            }
            catch (AgentCommandException ex)
            {
                throw new AgentCommandException(ex.ErrorCode, "Target: " + ex.Message);
            }
        }

        /// <summary>The parent's direct children, or every model root when the target is itself a root.</summary>
        private static List<ModelItem> GetSiblings(Document document, ModelItem target, bool includeHidden)
        {
            IEnumerable<ModelItem> candidates = target.Parent != null
                ? target.Parent.Children.Cast<ModelItem>()
                : document.Models.Cast<Model>().Where(model => model != null).Select(model => model.RootItem);
            return candidates
                .Where(item => item != null && !item.Equals(target) && (includeHidden || !item.IsHidden))
                .ToList();
        }

        private static ClashOneVsRestCandidate ToCandidate(ModelItem item, bool withBox)
        {
            return new ClashOneVsRestCandidate
            {
                DisplayName = item.DisplayName ?? string.Empty,
                Box = withBox ? ReadBox(item) : null,
            };
        }

        private static ClashOneVsRestBox ReadBox(ModelItem item)
        {
            try
            {
                var box = item.BoundingBox();
                if (box == null || box.IsEmpty)
                    return null;
                return new ClashOneVsRestBox
                {
                    MinX = box.Min.X, MinY = box.Min.Y, MinZ = box.Min.Z,
                    MaxX = box.Max.X, MaxY = box.Max.Y, MaxZ = box.Max.Z,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Plans or writes every Selection Set; returns the paths a test may bind to.</summary>
        private HashSet<string> PrepareSets(Document document, ClashOneVsRestPlan plan, ModelItem target, IList<ModelItem> rest,
            bool apply, bool overwrite, ClashOneVsRestResponse response)
        {
            var usable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var setPlan in plan.Sets)
            {
                var items = setPlan.IsTarget ? new List<ModelItem> { target } : setPlan.RestIndexes.Select(index => rest[index]).ToList();
                var set = new ClashOneVsRestSet { Name = setPlan.Name, Path = setPlan.Path, ItemCount = items.Count };
                response.Sets.Add(set);
                var setRequest = new CreateSelectionSetRequest
                {
                    Name = setPlan.Name,
                    FolderPath = plan.SetFolder,
                    MatchHandles = new List<string> { _sessionStore.Add(items) },
                    Overwrite = overwrite,
                    Apply = false,
                };
                try
                {
                    var preview = _commandService.CreateSelectionSet(document, setRequest, _sessionStore);
                    if (preview.NameConflict && !overwrite)
                    {
                        set.Status = ClashOneVsRestStatuses.Conflict;
                        set.Reason = ClashOneVsRestReasons.SetExists;
                        continue;
                    }
                    if (!apply)
                    {
                        set.Status = preview.NameConflict ? ClashOneVsRestStatuses.Exists : ClashOneVsRestStatuses.Planned;
                        usable.Add(setPlan.Path);
                        continue;
                    }
                    setRequest.Apply = true;
                    var written = _commandService.CreateSelectionSet(document, setRequest, _sessionStore);
                    set.Status = written.Overwritten == true ? ClashOneVsRestStatuses.Replaced : ClashOneVsRestStatuses.Created;
                    usable.Add(setPlan.Path);
                }
                catch (Exception ex)
                {
                    set.Status = ClashOneVsRestStatuses.Failed;
                    set.Reason = ClashOneVsRestReasons.SetFailed + ": " + ex.Message;
                    response.Warnings.Add("Selection Set '" + setPlan.Path + "': " + ex.Message);
                }
            }
            return usable;
        }

        private static void MarkPairsWithUnusableSets(ClashOneVsRestPlan plan, ISet<string> usableSets, ClashOneVsRestResponse response)
        {
            foreach (var pair in plan.Pairs)
            {
                var missing = !usableSets.Contains(pair.SetAPath) ? pair.SetAPath : !usableSets.Contains(pair.SetBPath) ? pair.SetBPath : null;
                if (missing == null)
                    continue;
                var set = response.Sets.FirstOrDefault(candidate => string.Equals(candidate.Path, missing, StringComparison.OrdinalIgnoreCase));
                pair.Status = ClashOneVsRestStatuses.Skipped;
                pair.Reason = set != null && set.Reason == ClashOneVsRestReasons.SetExists
                    ? ClashOneVsRestReasons.SetExists + ": " + missing + " (pass overwriteExisting=true to refresh it)"
                    : ClashOneVsRestReasons.SetFailed + ": " + missing;
            }
        }

        private static void MarkExistingTests(DocumentClash clash, ClashOneVsRestPlan plan, bool overwrite, ClashOneVsRestResponse response)
        {
            var existing = new HashSet<string>(ClashApiCompat.GetClashTests(clash).Select(test => test.DisplayName ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in plan.Pairs.Where(pair => pair.Status == ClashOneVsRestStatuses.Planned && existing.Contains(pair.TestName)))
            {
                if (overwrite)
                {
                    pair.Reason = "replaces the existing test";
                    continue;
                }
                pair.Status = ClashOneVsRestStatuses.Conflict;
                pair.Reason = ClashOneVsRestReasons.TestExists + " (pass overwriteExisting=true to replace it)";
            }
        }

        private void CreateTests(Document document, ClashOneVsRestPlan plan, bool overwrite, ClashOneVsRestResponse response)
        {
            var pending = plan.Pairs.Where(pair => pair.Status == ClashOneVsRestStatuses.Planned).ToList();
            if (pending.Count == 0)
                return;
            var created = _testsService.Execute(document, new ClashTestsFromSetsRequest
            {
                Apply = true,
                Pairs = pending.Select(pair => new ClashSetPair
                {
                    A = new SelectionSetReference { Path = pair.SetAPath },
                    B = new SelectionSetReference { Path = pair.SetBPath },
                    Name = pair.TestName,
                    TestType = pair.TestType,
                    ToleranceMm = pair.ToleranceMm,
                }).ToList(),
                OverwriteExisting = overwrite,
                ContinueOnError = true,
                Limit = MaximumLimit,
            });
            for (var index = 0; index < pending.Count; index++)
            {
                var pair = pending[index];
                var result = index < created.Tests.Count ? created.Tests[index] : null;
                if (result == null)
                {
                    pair.Status = ClashOneVsRestStatuses.Failed;
                    pair.Reason = "clash_tests_from_sets returned no result for this pair.";
                    continue;
                }
                pair.TestHandle = result.TestHandle;
                if (result.Status == "created" || result.Status == "replaced")
                {
                    pair.Status = result.Status == "replaced" ? ClashOneVsRestStatuses.Replaced : ClashOneVsRestStatuses.Created;
                    continue;
                }
                pair.Status = result.Status == "conflict" ? ClashOneVsRestStatuses.Conflict : ClashOneVsRestStatuses.Failed;
                pair.Reason = result.Status == "conflict" && !overwrite
                    ? ClashOneVsRestReasons.TestExists + " (pass overwriteExisting=true to replace it)"
                    : result.ErrorMessage;
            }
            response.Warnings.AddRange(created.Warnings);
        }
    }
}
