using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerStyles
    {
        public const string Target = "target";
        public const string Cross = "cross";
        public const string Circle = "circle";
        public const string Pin = "pin";
        public const string Pole = "pole";
        public const string Box = "box";
    }

    public sealed class WorldMarkerCreateRequest
    {
        public List<WorldMarkerSpec> Markers { get; set; } = new List<WorldMarkerSpec>();
        public string DocumentUnits { get; set; }
        public string DocumentFilePath { get; set; }
        public string ArtifactDirectory { get; set; }
        public bool? ReplaceExisting { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class WorldMarkerSpec
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double? Z { get; set; }
        public string Style { get; set; }
        public double? Size { get; set; }
        public WorldMarkerColor Color { get; set; }
        public string Label { get; set; }
        public WorldMarkerPole Pole { get; set; }
    }

    public sealed class WorldMarkerColor
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
    }

    public sealed class WorldMarkerPole
    {
        public bool? Enabled { get; set; }
        public double? BaseZ { get; set; }
        public double? TopZ { get; set; }
    }

    public sealed class WorldMarkerBatchPlan
    {
        public string DocumentUnits { get; set; }
        public bool ReplaceExisting { get; set; }
        public bool Apply { get; set; }
        public List<WorldMarkerPlanItem> Markers { get; set; } = new List<WorldMarkerPlanItem>();
    }

    public sealed class WorldMarkerPlanItem
    {
        public string MarkerId { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Style { get; set; }
        public double Size { get; set; }
        public WorldMarkerColor Color { get; set; }
        public string Label { get; set; }
        public bool PoleEnabled { get; set; }
        public double PoleBaseZ { get; set; }
        public double PoleTopZ { get; set; }
    }

    public sealed class WorldMarkerArtifactRoot
    {
        public string Path { get; set; }
        public bool IsPortableWithDocument { get; set; }
        public string Warning { get; set; }
    }

    public sealed class WorldMarkerModelDescriptor
    {
        public int ModelIndex { get; set; }
        public string MarkerId { get; set; }
        public string ArtifactPath { get; set; }
        public bool Hidden { get; set; }
    }

    public sealed class WorldMarkerLifecyclePlan
    {
        public string Operation { get; set; }
        public string MarkerId { get; set; }
        public string NewArtifactPath { get; set; }
        public bool AppendNewBeforeDeletingExisting { get; set; }
        /// <summary>Desired hidden state for visibility plans; null for non-visibility operations.</summary>
        public bool? TargetHidden { get; set; }
        public List<WorldMarkerModelDescriptor> TargetModels { get; set; } = new List<WorldMarkerModelDescriptor>();
        public List<int> DeleteModelIndices { get; set; } = new List<int>();
        public List<string> CleanupArtifactPaths { get; set; } = new List<string>();
        public List<string> MissingMarkerIds { get; set; } = new List<string>();
    }
}
