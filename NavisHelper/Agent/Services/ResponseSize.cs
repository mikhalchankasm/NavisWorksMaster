using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// The bytes one part of a response takes, for paging against ResponseBudget (#427). The
    /// settings are the host's wire settings; their snake_case keys are never shorter than the
    /// camelCase keys the MCP client finally reads, so the count is an upper bound.
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
        };

        public static int Bytes(object value)
        {
            return Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(value, Settings));
        }
    }
}
