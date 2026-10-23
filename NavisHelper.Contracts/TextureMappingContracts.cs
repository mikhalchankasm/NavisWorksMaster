using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class TextureMappingRules
    {
        public const int DefaultMaxItems = 20;
        public const int MaxItems = 100;

        public static int ClampMaxItems(int? value)
        {
            return Math.Max(1, Math.Min(MaxItems, value ?? DefaultMaxItems));
        }

        public static string MappingName(string native)
        {
            switch (native)
            {
                case "eMAPPING_BOX": return "box";
                case "eMAPPING_PLANE": return "plane";
                case "eMAPPING_CYLINDER": return "cylinder";
                case "eMAPPING_SPHERE": return "sphere";
                case "eMAPPING_EXPLICIT": return "explicit";
                default: return null;
            }
        }

        public static string AxisName(string native)
        {
            switch (native)
            {
                case "eDIRECTION_PLUSX": return "+x";
                case "eDIRECTION_MINUSX": return "-x";
                case "eDIRECTION_PLUSY": return "+y";
                case "eDIRECTION_MINUSY": return "-y";
                case "eDIRECTION_PLUSZ": return "+z";
                case "eDIRECTION_MINUSZ": return "-z";
                default: return null;
            }
        }
    }

    public sealed class GetItemTextureMappingRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public int? MaxItems { get; set; }
    }

    public sealed class GetItemTextureMappingResponse
    {
        public int ItemCount { get; set; }
        public int ReadCount { get; set; }
        public bool Truncated { get; set; }
        public bool SelectionRestored { get; set; }
        public List<ItemTextureMapping> Items { get; set; } = new List<ItemTextureMapping>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ItemTextureMapping
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string Mapping { get; set; }
        public bool? HasTextureCoordinates { get; set; }
        public TextureVector Translation { get; set; }
        public TextureVector Scale { get; set; }
        public TextureRotation Rotation { get; set; }
        public TextureVector DomainMin { get; set; }
        public TextureVector DomainMax { get; set; }
        public List<TextureFaceDirection> FaceDirections { get; set; } = new List<TextureFaceDirection>();
        public bool? CapEnabled { get; set; }
        public double? CapThreshold { get; set; }
        public string Error { get; set; }
    }

    public sealed class TextureVector
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public sealed class TextureRotation
    {
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public double D { get; set; }
    }

    public sealed class TextureFaceDirection
    {
        public string Face { get; set; }
        public string U { get; set; }
        public string V { get; set; }
    }
}
