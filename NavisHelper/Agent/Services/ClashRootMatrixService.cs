using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashRootMatrixService
    {
        public ClashRootMatrixResponse Execute(Document document, ClashRootMatrixRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashRootMatrixRequest();
            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var allTests = ClashApiCompat.GetClashTests(clash).ToList();
            var tests = ResolveTests(allTests, request).ToList();
            if (tests.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "No Clash Detective tests matched the requested scope.");

            var includeAll = request.IncludeAllStatuses == true || request.StatusFilters == null || request.StatusFilters.Count == 0;
            var statuses = new HashSet<string>((request.StatusFilters ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(NormalizeStatus), StringComparer.OrdinalIgnoreCase);
            var ignoreSameFile = request.IgnoreSameFile.GetValueOrDefault(true);
            var response = new ClashRootMatrixResponse { MatchedTestCount = tests.Count };
            var pairs = new Dictionary<string, ClashRootMatrixPair>(StringComparer.Ordinal);

            foreach (var test in tests)
            {
                foreach (var result in EnumerateResults(test.Children))
                {
                    var status = NormalizeStatus(result.Status.ToString());
                    if (!includeAll && !statuses.Contains(status))
                        continue;
                    response.AnalyzedResultCount++;

                    var rootA = ResolveRoot(result.Item1);
                    var rootB = ResolveRoot(result.Item2);
                    if (rootA == null || rootB == null)
                    {
                        response.UnresolvedRootCount++;
                        continue;
                    }
                    if (string.Equals(rootA.Id, rootB.Id, StringComparison.Ordinal))
                    {
                        if (ignoreSameFile)
                        {
                            response.SameFileIgnoredCount++;
                            continue;
                        }
                    }
                    if (string.CompareOrdinal(rootA.Id, rootB.Id) > 0)
                    {
                        var swap = rootA;
                        rootA = rootB;
                        rootB = swap;
                    }

                    var key = rootA.Id + "\n" + rootB.Id;
                    ClashRootMatrixPair pair;
                    if (!pairs.TryGetValue(key, out pair))
                    {
                        pair = new ClashRootMatrixPair
                        {
                            RootAId = rootA.Id, RootAName = rootA.Name, RootASourceFile = rootA.SourceFile,
                            RootBId = rootB.Id, RootBName = rootB.Name, RootBSourceFile = rootB.SourceFile,
                        };
                        pairs.Add(key, pair);
                    }
                    pair.ClashCount++;
                    int count;
                    pair.StatusCounts.TryGetValue(status, out count);
                    pair.StatusCounts[status] = count + 1;
                }
            }

            var ordered = pairs.Values.OrderByDescending(pair => pair.ClashCount)
                .ThenBy(pair => pair.RootAName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.RootBName, StringComparer.OrdinalIgnoreCase).ToList();
            var offset = Math.Max(0, request.Offset.GetValueOrDefault());
            var limit = Math.Max(1, Math.Min(5000, request.Limit.GetValueOrDefault(500)));
            response.PlannedPairCount = ordered.Count;
            response.Offset = offset;
            response.Limit = limit;
            response.Pairs = ordered.Skip(offset).Take(limit).ToList();
            response.ReturnedPairCount = response.Pairs.Count;
            response.PairsTruncated = offset > 0 || offset + response.Pairs.Count < ordered.Count;
            response.Truncated = response.PairsTruncated;
            response.NextOffset = offset + response.Pairs.Count < ordered.Count ? (int?)(offset + response.Pairs.Count) : null;
            return response;
        }

        private static IEnumerable<ClashTest> ResolveTests(IList<ClashTest> allTests, ClashRootMatrixRequest request)
        {
            var handles = new HashSet<int>();
            foreach (var handle in request.TestHandles ?? new List<string>())
            {
                int index;
                if (ClashHandleHelper.TryParseTestHandle(handle, out index))
                    handles.Add(index);
            }
            var names = (request.TestNames ?? new List<string>()).Concat(new[] { request.TestName })
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            if (handles.Count == 0 && names.Count == 0)
            {
                foreach (var test in allTests)
                    yield return test;
                yield break;
            }
            for (var index = 0; index < allTests.Count; index++)
            {
                var test = allTests[index];
                if (handles.Contains(index + 1) || names.Any(name => string.Equals(test.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                    yield return test;
            }
        }

        private static IEnumerable<ClashResult> EnumerateResults(SavedItemCollection children)
        {
            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                    yield return result;
                var group = child as ClashResultGroup;
                if (group == null)
                    continue;
                foreach (var nested in EnumerateResults(group.Children))
                    yield return nested;
            }
        }

        private static RootIdentity ResolveRoot(ModelItem item)
        {
            if (item == null || item.Model == null)
                return null;
            var model = item.Model;
            var source = !string.IsNullOrWhiteSpace(model.SourceFileName) ? model.SourceFileName : model.FileName;
            var id = model.Guid != Guid.Empty ? "guid:" + model.Guid.ToString("D") :
                model.SourceGuid != Guid.Empty ? "source-guid:" + model.SourceGuid.ToString("D") :
                "model:" + (source ?? string.Empty) + ":" + model.GetHashCode().ToString();
            var name = model.RootItem == null ? string.Empty : model.RootItem.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = PortableFileName(source);
            return new RootIdentity { Id = id, Name = name ?? string.Empty, SourceFile = source ?? string.Empty };
        }

        private static string PortableFileName(string value)
        {
            var text = value ?? string.Empty;
            var index = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
            return index >= 0 && index + 1 < text.Length ? text.Substring(index + 1) : text;
        }

        private static string NormalizeStatus(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "_");
        }

        private sealed class RootIdentity
        {
            public string Id;
            public string Name;
            public string SourceFile;
        }
    }
}
