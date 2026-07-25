using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Core
{
    internal static class RedlineJsonSanitizer
    {
        private const string EmptyCollection = "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}";

        public static void SetSupportedRedlines(Autodesk.Navisworks.Api.View view, string json, ICollection<string> warnings)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            view.SetRedlines(FilterUnsupportedTypes(json, warnings));
        }

        public static string FilterUnsupportedTypes(string json, ICollection<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(json))
                return EmptyCollection;

            var root = JObject.Parse(json);
            if (!string.Equals((string)root["Type"], "RedlineCollection", StringComparison.Ordinal))
                throw new ArgumentException("Redline JSON must have Type=RedlineCollection.", nameof(json));
            var sourceValues = root["Values"] as JArray;
            if (sourceValues == null)
                throw new ArgumentException("Redline JSON must contain a Values array.", nameof(json));

            var filteredValues = new JArray();
            var warnedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in sourceValues)
            {
                var item = token as JObject;
                var type = item == null ? string.Empty : (string)item["Type"];
                if (item != null && RedlineTypeWhitelistHelper.IsSupportedForSetRedlines(type))
                {
                    filteredValues.Add(item.DeepClone());
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(type) ? "<missing>" : type;
                if (warnings != null && warnedTypes.Add(label))
                    warnings.Add("Skipped unsupported SetRedlines primitive type: " + label + ". Supported types are RedlineLine and RedlineEllipse.");
            }

            root["Values"] = filteredValues;
            return root.ToString(Formatting.None);
        }
    }
}
