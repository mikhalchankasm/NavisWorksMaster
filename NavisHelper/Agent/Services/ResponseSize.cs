using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// The bytes one part of a response takes, for paging against ResponseBudget (#427). It is
    /// an upper bound on what the MCP client finally reads: the snake_case keys are never shorter
    /// than the client's camelCase ones, and every non-ASCII character is counted as the six-byte
    /// \uXXXX escape the server's encoder still writes for some of them (U+3000, a surrogate
    /// half), although it writes most, Cyrillic among them, as two or three UTF-8 bytes.
    /// </summary>
    internal static class ResponseSize
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
            NullValueHandling = NullValueHandling.Ignore,
            StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
        };

        public static int Bytes(object value)
        {
            // All ASCII after escaping, so one character is one byte, except DEL: Newtonsoft leaves
            // it as is and the server's encoder writes it as \u007F (#427 review).
            var json = JsonConvert.SerializeObject(value, Settings);
            var bytes = json.Length;
            foreach (var c in json)
            {
                if (c == '\u007f')
                    bytes += 5;
            }

            return bytes;
        }
    }
}
