using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.AI
{
    internal sealed class AIConfigData
    {
        internal string ModelName { get; set; }
        internal double Temperature { get; set; }
        internal int ColorScheme { get; set; }
    }

    internal static class AIConfigJsonSerializer
    {
        internal static string Serialize(AIConfigData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return new JObject
            {
                ["ModelName"] = data.ModelName ?? string.Empty,
                ["Temperature"] = data.Temperature,
                ["ColorScheme"] = data.ColorScheme
            }.ToString(Formatting.Indented);
        }

        internal static AIConfigData Parse(
            string json,
            AIConfigData defaults)
        {
            if (defaults == null)
                throw new ArgumentNullException(nameof(defaults));
            if (string.IsNullOrWhiteSpace(json))
                return defaults;

            try
            {
                var root = JObject.Parse(json);
                return new AIConfigData
                {
                    ModelName =
                        (string)root["ModelName"] ?? defaults.ModelName,
                    Temperature =
                        (double?)root["Temperature"] ?? defaults.Temperature,
                    ColorScheme =
                        (int?)root["ColorScheme"] ?? defaults.ColorScheme
                };
            }
            catch (JsonException)
            {
                return defaults;
            }
        }
    }
}
