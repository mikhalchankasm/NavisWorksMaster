using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ExportSelectionGeometryRequest
    {
        public string OutputPath { get; set; }
        public string Scope { get; set; }
        public List<string> MatchHandles { get; set; } = new List<string>();
        public string Format { get; set; }
        public string CoordinateSpace { get; set; }
        public string GroupBy { get; set; }
        public int? ItemLimit { get; set; }
        public int? MaxTriangles { get; set; }
        public bool? Apply { get; set; }
        public bool? Overwrite { get; set; }
    }

    public sealed class ExportSelectionGeometryResponse
    {
        public bool Applied { get; set; }
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public string CoordinateSpace { get; set; }
        public string Units { get; set; }
        public string DocumentUnits { get; set; }
        public int InputItemCount { get; set; }
        public int GeometryItemCount { get; set; }
        public int FragmentCount { get; set; }
        public int TriangleCount { get; set; }
        public int LineCount { get; set; }
        public int PointCount { get; set; }
        public int SnapPointCount { get; set; }
        public long FileSizeBytes { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class GeometryFragmentInfo
    {
        public int ItemId { get; set; }
        public int FragmentId { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public int TriangleCount { get; set; }
        public double[] FragmentToWorld { get; set; }
        public double[] OutputToWorld { get; set; }
        public bool ReversesOrientation { get; set; }
    }
}
