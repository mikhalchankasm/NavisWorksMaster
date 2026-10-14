using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>An axis-aligned box in document units.</summary>
    public sealed class ClashOneVsRestBox
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
    }

    public sealed class ClashOneVsRestCandidate
    {
        public string DisplayName { get; set; }

        /// <summary>Null when Navisworks reports no geometry extent; such a node is never skipped by the prefilter.</summary>
        public ClashOneVsRestBox Box { get; set; }
    }

    public sealed class ClashOneVsRestPlanInput
    {
        public ClashOneVsRestCandidate Target { get; set; }

        /// <summary>The target's siblings, target excluded, in tree order.</summary>
        public List<ClashOneVsRestCandidate> Rest { get; set; } = new List<ClashOneVsRestCandidate>();

        public string Mode { get; set; }
        public List<string> IncludeNameMasks { get; set; } = new List<string>();
        public List<string> ExcludeNameMasks { get; set; } = new List<string>();
        public string SetFolder { get; set; }
        public string PairNameTemplate { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
        public List<ClashOneVsRestOverride> PairOverrides { get; set; } = new List<ClashOneVsRestOverride>();
        public bool BboxPrefilter { get; set; }

        /// <summary>The tolerance converted to document units; boxes closer than this still count as touching.</summary>
        public double BboxMargin { get; set; }
    }

    /// <summary>One Selection Set the plan needs. <see cref="RestIndexes"/> holds indexes into the input's rest list; the target set has none and <see cref="IsTarget"/> set.</summary>
    public sealed class ClashOneVsRestSetPlan
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsTarget { get; set; }
        public List<int> RestIndexes { get; set; } = new List<int>();
    }

    public sealed class ClashOneVsRestPlan
    {
        public string Mode { get; set; }
        public string SetFolder { get; set; }
        public int ExcludedByMaskCount { get; set; }
        public List<string> BboxSkippedNames { get; set; } = new List<string>();
        public int UnknownBoxCount { get; set; }
        public List<ClashOneVsRestSetPlan> Sets { get; set; } = new List<ClashOneVsRestSetPlan>();
        public List<ClashOneVsRestPair> Pairs { get; set; } = new List<ClashOneVsRestPair>();
    }

    /// <summary>
    /// The Navisworks-free half of <c>clash_one_vs_rest</c>: which siblings take part,
    /// what every Selection Set and test is called, which type and tolerance each pair
    /// gets, and which pairs the bounding-box prefilter drops.
    ///
    /// Sets are explicit, never Search Sets. A search set is re-evaluated whenever the
    /// model changes, so two runs over successive NWD iterations would no longer test
    /// the same thing; an explicit set is a frozen record of what was tested
    /// (the same decision as HorizunGroup/naviscoord-mcp, MIT).
    /// </summary>
    public static class ClashOneVsRestPlanner
    {
        public const string ModePairs = "pairs";
        public const string ModeSingle = "single";
        public const string DefaultPairNameTemplate = "{a} vs {b}";
        public const string DefaultSetFolderRoot = "Clash one vs rest";
        public const double DefaultToleranceMm = 1.0;
        public const string RestSetSuffix = " - rest";

        public static string NormalizeMode(string value)
        {
            var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (mode.Length == 0 || mode == ModePairs)
                return ModePairs;
            if (mode == ModeSingle)
                return ModeSingle;
            throw new ArgumentException("mode must be 'pairs' or 'single'.");
        }

        /// <summary>
        /// A display name usable as a Selection Set name and readable in a test name.
        /// "/" separates Selection Set folders, so RVM names such as "/12470-14-1400/PI"
        /// lose the leading slash and keep the rest as spaces: "12470-14-1400 PI".
        /// </summary>
        public static string Label(string displayName)
        {
            var text = (displayName ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
            text = text.Replace('/', ' ');
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length == 0 ? "unnamed" : text;
        }

        /// <summary>Case-insensitive wildcard match: '*' any run, '?' one character. Tried against the raw name and its label.</summary>
        public static bool MatchesMask(string displayName, string mask)
        {
            if (string.IsNullOrWhiteSpace(mask))
                return false;
            var pattern = "^" + Regex.Escape(mask.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var raw = displayName ?? string.Empty;
            return Regex.IsMatch(raw, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                   Regex.IsMatch(Label(raw), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>No include masks means every name is included; an exclude mask always wins.</summary>
        public static bool IsIncluded(string displayName, IList<string> includeMasks, IList<string> excludeMasks)
        {
            var include = (includeMasks ?? new List<string>()).Where(mask => !string.IsNullOrWhiteSpace(mask)).ToList();
            if (include.Count > 0 && !include.Any(mask => MatchesMask(displayName, mask)))
                return false;
            return !(excludeMasks ?? new List<string>()).Any(mask => MatchesMask(displayName, mask));
        }

        public static string FormatTestName(string template, string a, string b, int index)
        {
            var text = string.IsNullOrWhiteSpace(template) ? DefaultPairNameTemplate : template.Trim();
            return text
                .Replace("{index}", index.ToString(CultureInfo.InvariantCulture))
                .Replace("{a}", a ?? string.Empty)
                .Replace("{b}", b ?? string.Empty)
                .Trim();
        }

        public static string NormalizeFolder(string folder, string targetLabel)
        {
            var text = (folder ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
            if (text.Length == 0)
                return DefaultSetFolderRoot + "/" + targetLabel;
            var segments = text.Split('/').Select(segment => segment.Trim()).Where(segment => segment.Length > 0);
            return string.Join("/", segments.ToArray());
        }

        /// <summary>Two siblings may share a label; the later one gets " (2)", " (3)" so set and test names stay unique.</summary>
        public static string MakeUnique(string name, ISet<string> taken)
        {
            if (taken.Add(name))
                return name;
            for (var suffix = 2; ; suffix++)
            {
                var candidate = name + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
                if (taken.Add(candidate))
                    return candidate;
            }
        }

        /// <summary>Boxes overlap, or come within <paramref name="margin"/>, on all three axes.</summary>
        public static bool BoxesOverlap(ClashOneVsRestBox a, ClashOneVsRestBox b, double margin)
        {
            if (a == null || b == null)
                return true;
            var m = Math.Max(0, margin);
            return a.MinX <= b.MaxX + m && b.MinX <= a.MaxX + m &&
                   a.MinY <= b.MaxY + m && b.MinY <= a.MaxY + m &&
                   a.MinZ <= b.MaxZ + m && b.MinZ <= a.MaxZ + m;
        }

        public static ClashOneVsRestOverride FindOverride(string displayName, IList<ClashOneVsRestOverride> overrides)
        {
            return (overrides ?? new List<ClashOneVsRestOverride>())
                .FirstOrDefault(candidate => candidate != null && MatchesMask(displayName, candidate.NameMask));
        }

        public static string ResolveTestType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ClashTestTypeHelper.Hard;
            var normalized = ClashTestTypeHelper.NormalizeTestType(value);
            if (normalized == null)
                throw new ArgumentException("Unsupported testType: " + value);
            return normalized;
        }

        public static double ResolveToleranceMm(double? value)
        {
            if (!value.HasValue)
                return DefaultToleranceMm;
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0)
                throw new ArgumentException("toleranceMm must be a finite non-negative number.");
            return value.Value;
        }

        public static ClashOneVsRestPlan Plan(ClashOneVsRestPlanInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (input.Target == null)
                throw new ArgumentException("A target is required.");

            var mode = NormalizeMode(input.Mode);
            var defaultType = ResolveTestType(input.TestType);
            var defaultTolerance = ResolveToleranceMm(input.ToleranceMm);
            foreach (var entry in input.PairOverrides ?? new List<ClashOneVsRestOverride>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.NameMask))
                    throw new ArgumentException("Every pair override needs a nameMask.");
                ResolveTestType(entry.TestType);
                if (entry.ToleranceMm.HasValue)
                    ResolveToleranceMm(entry.ToleranceMm);
            }

            var targetLabel = Label(input.Target.DisplayName);
            var plan = new ClashOneVsRestPlan
            {
                Mode = mode,
                SetFolder = NormalizeFolder(input.SetFolder, targetLabel),
            };

            var rest = input.Rest ?? new List<ClashOneVsRestCandidate>();
            var included = new List<int>();
            for (var index = 0; index < rest.Count; index++)
            {
                var candidate = rest[index];
                if (candidate == null)
                    continue;
                if (!IsIncluded(candidate.DisplayName, input.IncludeNameMasks, input.ExcludeNameMasks))
                {
                    plan.ExcludedByMaskCount++;
                    continue;
                }
                if (input.BboxPrefilter)
                {
                    if (input.Target.Box == null || candidate.Box == null)
                        plan.UnknownBoxCount++;
                    else if (!BoxesOverlap(input.Target.Box, candidate.Box, input.BboxMargin))
                    {
                        plan.BboxSkippedNames.Add(candidate.DisplayName ?? string.Empty);
                        continue;
                    }
                }
                included.Add(index);
            }

            var setNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetSet = AddSet(plan, MakeUnique(targetLabel, setNames), true, new List<int>());
            var testNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (mode == ModeSingle)
            {
                if (included.Count == 0)
                    return plan;
                var restSet = AddSet(plan, MakeUnique(targetLabel + RestSetSuffix, setNames), false, included);
                plan.Pairs.Add(BuildPair(1, targetSet, restSet, targetLabel, targetLabel + RestSetSuffix,
                    input.PairNameTemplate, defaultType, defaultTolerance, null, testNames));
                return plan;
            }

            var pairIndex = 1;
            foreach (var index in included)
            {
                var candidate = rest[index];
                var label = Label(candidate.DisplayName);
                var set = AddSet(plan, MakeUnique(label, setNames), false, new List<int> { index });
                var profile = FindOverride(candidate.DisplayName, input.PairOverrides);
                plan.Pairs.Add(BuildPair(pairIndex++, targetSet, set, targetLabel, label,
                    input.PairNameTemplate, defaultType, defaultTolerance, profile, testNames));
            }
            return plan;
        }

        private static ClashOneVsRestSetPlan AddSet(ClashOneVsRestPlan plan, string name, bool isTarget, List<int> restIndexes)
        {
            var set = new ClashOneVsRestSetPlan
            {
                Name = name,
                Path = plan.SetFolder + "/" + name,
                IsTarget = isTarget,
                RestIndexes = restIndexes,
            };
            plan.Sets.Add(set);
            return set;
        }

        private static ClashOneVsRestPair BuildPair(int index, ClashOneVsRestSetPlan a, ClashOneVsRestSetPlan b,
            string aLabel, string bLabel, string template, string defaultType, double defaultTolerance,
            ClashOneVsRestOverride profile, ISet<string> testNames)
        {
            var testType = profile != null && !string.IsNullOrWhiteSpace(profile.TestType) ? ResolveTestType(profile.TestType) : defaultType;
            var tolerance = profile != null && profile.ToleranceMm.HasValue ? profile.ToleranceMm.Value : defaultTolerance;
            var name = FormatTestName(template, aLabel, bLabel, index);
            if (name.Length == 0)
                throw new ArgumentException("pairNameTemplate produced an empty test name.");
            return new ClashOneVsRestPair
            {
                Index = index,
                AName = a.Name,
                BName = b.Name,
                TestName = MakeUnique(name, testNames),
                TestType = testType,
                ToleranceMm = tolerance,
                SetAPath = a.Path,
                SetBPath = b.Path,
                Status = ClashOneVsRestStatuses.Planned,
            };
        }
    }
}
