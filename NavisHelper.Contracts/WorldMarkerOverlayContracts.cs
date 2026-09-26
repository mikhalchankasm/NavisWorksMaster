using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerOverlayModes
    {
        public const string Upsert = "upsert";
        public const string ReplaceAll = "replace_all";
    }

    public static class WorldMarkerOverlayOperations
    {
        public const string Hide = "hide";
        public const string Show = "show";
        public const string Delete = "delete";
        public const string Clear = "clear";
    }

    public sealed class WorldMarkerOverlaySpec
    {
        public string Name { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public string Style { get; set; }

        /// <summary>Optional figure size in document units; null means only the pixel head is drawn.</summary>
        public double? Size { get; set; }

        public int? SizePx { get; set; }

        public WorldMarkerColor Color { get; set; }

        /// <summary>Optional alpha channel, 0-255; null means opaque.</summary>
        public int? Alpha { get; set; }

        public string Label { get; set; }
        public WorldMarkerPole Pole { get; set; }

        public string Group { get; set; }
    }

    /// <summary>A stored marker; frozen at creation, so a snapshot can never be edited through it.</summary>
    public sealed class WorldMarkerOverlayMarker
    {
        internal WorldMarkerOverlayMarker(string markerId, string name, double x, double y, double z, string style,
            int sizePx, double? worldSize, WorldMarkerColor color, int? alpha, string label, bool poleEnabled,
            double poleBaseZ, double poleTopZ, string group, bool visible)
        {
            MarkerId = markerId; Name = name; X = x; Y = y; Z = z; Style = style; SizePx = sizePx;
            WorldSize = worldSize; Alpha = alpha; Label = label; PoleEnabled = poleEnabled;
            PoleBaseZ = poleBaseZ; PoleTopZ = poleTopZ; Group = group; Visible = visible;
            _color = new WorldMarkerColor { R = color.R, G = color.G, B = color.B };
        }

        public string MarkerId { get; }
        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string Style { get; }
        public int SizePx { get; }
        public double? WorldSize { get; }
        private readonly WorldMarkerColor _color;
        // A copy on every read: WorldMarkerColor is mutable, the stored marker is not.
        public WorldMarkerColor Color => new WorldMarkerColor { R = _color.R, G = _color.G, B = _color.B };
        public int? Alpha { get; }
        public string Label { get; }
        public bool PoleEnabled { get; }
        public double PoleBaseZ { get; }
        public double PoleTopZ { get; }
        public string Group { get; }
        public bool Visible { get; }
    }

    public sealed class WorldMarkerOverlaySnapshot
    {
        public WorldMarkerOverlaySnapshot(IEnumerable<WorldMarkerOverlayMarker> markers)
        {
            if (markers == null)
                throw new ArgumentNullException(nameof(markers));
            Markers = new List<WorldMarkerOverlayMarker>(markers).AsReadOnly();
        }

        public static WorldMarkerOverlaySnapshot Empty { get; } = new WorldMarkerOverlaySnapshot(new WorldMarkerOverlayMarker[0]);

        public IReadOnlyList<WorldMarkerOverlayMarker> Markers { get; }

        public int Count => Markers.Count;
    }

    public sealed class WorldMarkerOverlaySetRequest
    {
        /// <summary>One of <see cref="WorldMarkerOverlayModes"/>; null means upsert.</summary>
        public string Mode { get; set; }

        public List<WorldMarkerOverlaySpec> Markers { get; set; } = new List<WorldMarkerOverlaySpec>();

        /// <summary>Dry run by default; the store changes only when this is true.</summary>
        public bool? Apply { get; set; }
    }

    public sealed class WorldMarkerOverlaySetResponse
    {
        /// <summary>True when the request asked to change the store; false for a dry run.</summary>
        public bool Apply { get; set; }

        /// <summary>True when a new snapshot was installed; false when refused or dry-run.</summary>
        public bool Applied { get; set; }

        public WorldMarkerOverlaySetResult Result { get; set; }
    }

    public sealed class WorldMarkerOverlaySetResult
    {
        /// <summary>False when the request was refused; the snapshot is then left unchanged.</summary>
        public bool Accepted { get; set; }

        /// <summary>True when the request was refused because of the 500-marker cap.</summary>
        public bool CapExceeded { get; set; }

        public string RefusalReason { get; set; }
        public int Created { get; set; }
        public int Replaced { get; set; }
        public int MarkerCount { get; set; }
    }

    public sealed class WorldMarkerOverlayManageRequest
    {
        /// <summary>One of <see cref="WorldMarkerOverlayOperations"/>.</summary>
        public string Operation { get; set; }

        public List<string> Names { get; set; } = new List<string>();
        public List<string> Ids { get; set; } = new List<string>();

        /// <summary>Selects every marker that carries this group key.</summary>
        public string Group { get; set; }

        /// <summary>Dry run by default; the store changes only when this is true.</summary>
        public bool? Apply { get; set; }
    }

    public sealed class WorldMarkerOverlayManageResponse
    {
        /// <summary>True when the request asked to change the store; false for a dry run.</summary>
        public bool Apply { get; set; }

        /// <summary>True when a new snapshot was installed; false when refused or dry-run.</summary>
        public bool Applied { get; set; }

        public WorldMarkerOverlayManageResult Result { get; set; }
    }

    /// <summary>Read-only listing of the stored overlay markers; never changes the store.</summary>
    public sealed class WorldMarkerOverlayListRequest
    {
        /// <summary>Optional marker-name filter, matched like planner names; blank entries are ignored.</summary>
        public List<string> Names { get; set; } = new List<string>();

        /// <summary>Optional group filter; null or blank means no group restriction.</summary>
        public string Group { get; set; }
    }

    /// <summary>One stored marker as listed by world_markers_list.</summary>
    public sealed class WorldMarkerOverlayListItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Style { get; set; }
        public int SizePx { get; set; }
        public double? WorldSize { get; set; }
        public WorldMarkerColor Color { get; set; }
        public int? Alpha { get; set; }
        public string Label { get; set; }
        public bool PoleEnabled { get; set; }
        public double PoleBaseZ { get; set; }
        public double PoleTopZ { get; set; }
        public string Group { get; set; }
        public bool Visible { get; set; }
    }

    public sealed class WorldMarkerOverlayListResponse
    {
        /// <summary>The markers that passed the filters, in snapshot order.</summary>
        public List<WorldMarkerOverlayListItem> Markers { get; set; } = new List<WorldMarkerOverlayListItem>();

        public int MarkerCount { get; set; }

        /// <summary>The document the store is bound to; empty when nothing is stored.</summary>
        public string DocumentFileName { get; set; }

        /// <summary>True when the stored snapshot belongs to the active document.</summary>
        public bool BoundToActiveDocument { get; set; }

        public long Version { get; set; }
        public int StoredMarkerCount { get; set; }
        public int VisibleMarkerCount { get; set; }
        public int OverlayRenderCount { get; set; }
        public int RenderBoundsCount { get; set; }

        /// <summary>The marker count of the last draw pass; -1 before the first one.</summary>
        public int LastDrawMarkerCount { get; set; }

        public DateTime? LastDrawUtc { get; set; }
        public string LastError { get; set; }

        /// <summary>The overlay store lives in memory only and never survives a restart.</summary>
        public bool Persistent { get; set; }
    }

    public sealed class WorldMarkerOverlayManageResult
    {
        /// <summary>False when the request was refused; the snapshot is then left unchanged.</summary>
        public bool Accepted { get; set; } = true;

        public string RefusalReason { get; set; }
        public string Operation { get; set; }
        public int Hidden { get; set; }
        public int Shown { get; set; }
        public int Deleted { get; set; }
        public int MarkerCount { get; set; }
        public List<string> MissingNames { get; set; } = new List<string>();
        public List<string> MissingIds { get; set; } = new List<string>();
    }

    /// <summary>Union world bounds of the visible markers, for the render bounding box.</summary>
    public sealed class WorldMarkerOverlayBounds
    {
        internal WorldMarkerOverlayBounds(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        /// <summary>The empty sentinel; every coordinate is NaN until a visible marker is included.</summary>
        public static WorldMarkerOverlayBounds Empty { get; } =
            new WorldMarkerOverlayBounds(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

        public bool IsEmpty => double.IsNaN(MinX);
        public double MinX { get; }
        public double MinY { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double MaxZ { get; }
    }
}
