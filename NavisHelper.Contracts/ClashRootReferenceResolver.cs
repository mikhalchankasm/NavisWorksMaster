using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashRootResolutionDiagnostic
    {
        public string Side { get; set; }
        public string Status { get; set; }
        public string MatchStrategy { get; set; }
        public int MatchCount { get; set; }
        public string ProvidedPath { get; set; }
        public string ProvidedName { get; set; }
        public string ProvidedSourceFile { get; set; }
        public string Message { get; set; }
        public List<ClashBboxRootItem> Candidates { get; set; } = new List<ClashBboxRootItem>();
    }

    public sealed class ClashRootResolutionResult
    {
        public ClashBboxRootItem Root { get; set; }
        public ClashRootResolutionDiagnostic Diagnostic { get; set; }
        public bool Resolved { get { return Root != null && Diagnostic != null && Diagnostic.Status == "resolved"; } }
    }

    public static class ClashRootReferenceResolver
    {
        public static ClashRootResolutionResult Resolve(IEnumerable<ClashBboxRootItem> roots, ClashBboxRootItem reference, string side, int candidateLimit)
        {
            var available = (roots ?? Enumerable.Empty<ClashBboxRootItem>()).Where(root => root != null).ToList();
            var diagnostic = new ClashRootResolutionDiagnostic
            {
                Side = side,
                ProvidedPath = reference == null ? null : reference.Path,
                ProvidedName = reference == null ? null : reference.Name,
                ProvidedSourceFile = reference == null ? null : reference.SourceFile,
            };
            if (reference == null)
                return Failed(diagnostic, "not_found", "Pair side is missing.", available, candidateLimit);

            List<ClashBboxRootItem> matches;
            if (!string.IsNullOrWhiteSpace(reference.Path))
            {
                matches = available.Where(root => string.Equals(root.Path, reference.Path.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 0)
                    return Finish(diagnostic, matches, "path", candidateLimit);
            }
            if (!string.IsNullOrWhiteSpace(reference.Name))
            {
                matches = available.Where(root => string.Equals(root.Name, reference.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1)
                    return Finish(diagnostic, matches, "name", candidateLimit);
                if (matches.Count > 1)
                {
                    if (!string.IsNullOrWhiteSpace(reference.SourceFile))
                    {
                        var requestedSource = reference.SourceFile.Trim();
                        var narrowed = matches.Where(root => SourceIdentityEquals(root.SourceFile, requestedSource)).ToList();
                        if (narrowed.Count > 0)
                            return Finish(diagnostic, narrowed, "name+source_file", candidateLimit);
                    }
                    return Finish(diagnostic, matches, "name", candidateLimit);
                }
            }
            if (!string.IsNullOrWhiteSpace(reference.SourceFile))
            {
                var requested = reference.SourceFile.Trim();
                matches = available.Where(root => SourceIdentityEquals(root.SourceFile, requested)).ToList();
                if (matches.Count > 0)
                    return Finish(diagnostic, matches, "source_file", candidateLimit);
            }

            return Failed(diagnostic, "not_found", "No exact root candidate matched the provided path, name, or sourceFile.", available, candidateLimit);
        }

        private static ClashRootResolutionResult Finish(ClashRootResolutionDiagnostic diagnostic, List<ClashBboxRootItem> matches, string strategy, int candidateLimit)
        {
            diagnostic.MatchStrategy = strategy;
            diagnostic.MatchCount = matches.Count;
            diagnostic.Candidates = matches.Take(Math.Max(0, candidateLimit)).ToList();
            if (matches.Count == 1)
            {
                diagnostic.Status = "resolved";
                diagnostic.Message = "Resolved by exact " + strategy + ".";
                return new ClashRootResolutionResult { Root = matches[0], Diagnostic = diagnostic };
            }
            diagnostic.Status = "ambiguous";
            diagnostic.Message = "Exact " + strategy + " matched " + matches.Count + " root candidates; no candidate was selected.";
            return new ClashRootResolutionResult { Diagnostic = diagnostic };
        }

        private static ClashRootResolutionResult Failed(ClashRootResolutionDiagnostic diagnostic, string status, string message, List<ClashBboxRootItem> available, int candidateLimit)
        {
            diagnostic.Status = status;
            diagnostic.Message = message;
            diagnostic.MatchCount = 0;
            var query = diagnostic.ProvidedName ?? diagnostic.ProvidedSourceFile ?? diagnostic.ProvidedPath ?? string.Empty;
            diagnostic.Candidates = available
                .Where(root => string.IsNullOrWhiteSpace(query) ||
                               (root.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (root.SourceFile ?? string.Empty).IndexOf(Path.GetFileName(query), StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(Math.Max(0, candidateLimit))
                .ToList();
            return new ClashRootResolutionResult { Diagnostic = diagnostic };
        }

        private static bool SourceIdentityEquals(string candidate, string requested)
        {
            if (string.Equals(candidate ?? string.Empty, requested ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(requested))
                return false;
            if (!string.Equals(Path.GetFileName(requested), requested, StringComparison.Ordinal))
                return false;
            return string.Equals(Path.GetFileName(candidate), Path.GetFileName(requested), StringComparison.OrdinalIgnoreCase);
        }
    }
}
