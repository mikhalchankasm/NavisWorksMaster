using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>A clash location in millimetres, identified by its member ID.</summary>
    public sealed class ClashZonePoint
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    /// <summary>A sphere with centre and radius in millimetres.</summary>
    public sealed class ClashZoneSphere
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Radius { get; set; }
    }

    /// <summary>An independently processed cluster within a partition.</summary>
    public sealed class ClashZoneInputCluster
    {
        public string PartitionKey { get; set; }
        public IList<ClashZonePoint> Points { get; set; }
    }

    /// <summary>Limits on enclosing geometry and padding of displayed zones.</summary>
    public sealed class ClashZoneGeometryOptions
    {
        public double MaxZoneRadiusMm { get; set; } = 3000;
        public double MinRadiusMm { get; set; } = 150;
        public double PadMm { get; set; } = 100;
    }

    /// <summary>A numbered zone with enclosing sphere, display radius and member centroid.</summary>
    public sealed class ClashZone
    {
        public string ZoneId { get; set; }
        public string PartitionKey { get; set; }
        public IList<ClashZonePoint> Points { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }
        public double EnclosingRadiusMm { get; set; }
        public double RadiusMm { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public double CentroidZ { get; set; }
    }
}
