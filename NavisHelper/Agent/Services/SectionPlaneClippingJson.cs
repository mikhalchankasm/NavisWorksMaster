using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed class ParsedSectionPlanes
    {
        public string Mode { get; set; } = "unreadable";
        public bool Readable { get; set; }
        public bool Enabled { get; set; }
        public bool Linked { get; set; }
        public List<SectionPlaneState> Planes { get; set; } = new List<SectionPlaneState>();
    }

    internal static class SectionPlaneClippingJson
    {
        public static ParsedSectionPlanes Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ParsedSectionPlanes { Readable = true, Mode = "off" };
            try
            {
                var root = JObject.Parse(json);
                var parsed = new ParsedSectionPlanes
                {
                    Readable = true,
                    Enabled = (bool?)root["Enabled"] ?? false,
                    Linked = (bool?)root["Linked"] ?? false,
                };
                var planes = root["Planes"] as JArray;
                if (root["Planes"] != null && planes == null) return new ParsedSectionPlanes();
                if (planes != null)
                {
                    for (var index = 0; index < planes.Count; index++)
                    {
                        var plane = (JObject)planes[index];
                        if (plane == null) return new ParsedSectionPlanes();
                        var normal = plane["Normal"] as JArray;
                        if (normal == null || normal.Count != 3) return new ParsedSectionPlanes();
                        var state = new SectionPlaneState
                        {
                            Index = index + 1, Enabled = (bool?)plane["Enabled"] ?? false,
                            Normal = new BoxVector3 { X = (double)normal[0], Y = (double)normal[1], Z = (double)normal[2] },
                            Distance = (double)plane["Distance"],
                        };
                        if (!Finite(state.Normal.X) || !Finite(state.Normal.Y) || !Finite(state.Normal.Z) || !Finite(state.Distance))
                            return new ParsedSectionPlanes();
                        parsed.Planes.Add(state);
                    }
                }
                parsed.Mode = !parsed.Enabled ? "off" : parsed.Planes.Count > 0 ? "planes" :
                    root["OrientedBox"] is JObject ? "box" : "off";
                return parsed;
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException ||
                ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                return new ParsedSectionPlanes();
            }
        }

        public static string Disable(string currentJson)
        {
            var root = new JObject();
            if (!string.IsNullOrWhiteSpace(currentJson))
            {
                using (var reader = new JsonTextReader(new StringReader(currentJson)) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);
            }
            root["Enabled"] = false;
            return root.ToString(Formatting.None);
        }

        public static string Write(string currentJson, IList<SectionPlaneState> planes, bool linked, bool enabled)
        {
            var states = Read(currentJson).Planes.ToDictionary(plane => plane.Index);
            foreach (var plane in planes ?? new List<SectionPlaneState>())
            {
                if (plane == null || plane.Index < 1 || plane.Index > SectionPlaneRules.MaxPlanes)
                    throw new ArgumentException("Specify index 1-6.", nameof(planes));
                states[plane.Index] = plane;
            }
            var entries = new JArray();
            for (var index = 1; index <= SectionPlaneRules.MaxPlanes; index++)
            {
                SectionPlaneState plane;
                if (!states.TryGetValue(index, out plane))
                    plane = new SectionPlaneState { Normal = new BoxVector3 { Z = 1 } };
                entries.Add(new JObject
                {
                    ["Type"] = "ClipPlane", ["Version"] = 1,
                    ["Normal"] = new JArray(plane.Normal.X, plane.Normal.Y, plane.Normal.Z),
                    ["Distance"] = plane.Distance, ["Enabled"] = plane.Enabled,
                });
            }
            return new JObject
            {
                ["Type"] = "ClipPlaneSet", ["Version"] = 1, ["Planes"] = entries,
                ["Linked"] = linked, ["Enabled"] = enabled,
            }.ToString(Formatting.None);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
