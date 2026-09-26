using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ViewpointSetCameraResponse
    {
        public bool Apply { get; set; }
        public bool Applied { get; set; }
        public Point3Info Position { get; set; }
        public Point3Info EffectivePosition { get; set; }
        public Point3Info Target { get; set; }
        public Point3Info Direction { get; set; }
        public Point3Info Up { get; set; }
        public double FocalDistance { get; set; }
        public string Projection { get; set; }
        public double? HeightField { get; set; }
        public BoundingBoxInfo ZoomBox { get; set; }
        public string SaveName { get; set; }
        public string SaveFolderPath { get; set; }
        public bool SaveRequested { get; set; }
        public bool SaveNameConflict { get; set; }
        public bool Saved { get; set; }
        public int? CreatedFolderCount { get; set; }
        public List<string> Warnings { get; set; }
    }
}
