using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ApplicationParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService : IDisposable
    {

        private static string BuildSuccessResponseJson(string requestId, long elapsedMs, object payload, out bool responseTruncated)
        {
            responseTruncated = false;
            var payloadToken = payload == null ? JValue.CreateNull() : JToken.FromObject(payload, JsonDeserializer);
            var response = BuildSuccessResponseObject(requestId, elapsedMs, payloadToken);
            var json = response.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(json) <= MaxFrameLengthBytes)
                return json;

            responseTruncated = true;
            MarkPayloadAsTruncated(payloadToken, "Response exceeded the named-pipe frame limit; large arrays were truncated after the command completed.");
            while (Encoding.UTF8.GetByteCount(json) > MaxFrameLengthBytes && TrimLargestArray(payloadToken))
            {
                response = BuildSuccessResponseObject(requestId, elapsedMs, payloadToken);
                json = response.ToString(Formatting.None);
            }

            if (Encoding.UTF8.GetByteCount(json) <= MaxFrameLengthBytes)
                return json;

            payloadToken = new JObject
            {
                ["truncated"] = true,
                ["message"] = "Response exceeded the named-pipe frame limit after the command completed. Use last_operation_status with this request_id to confirm execution state and rerun with a smaller limit/page size if data is needed.",
            };
            response = BuildSuccessResponseObject(requestId, elapsedMs, payloadToken);
            json = response.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(json) <= MaxFrameLengthBytes)
                return json;

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Truncated response still exceeds the maximum of " + MaxFrameLengthBytes + " bytes.");
        }

        private static JObject BuildSuccessResponseObject(string requestId, long elapsedMs, JToken payloadToken)
        {
            return new JObject
            {
                ["protocol_version"] = ProtocolConstants.CurrentProtocolVersion,
                ["request_id"] = requestId,
                ["ok"] = true,
                ["error_code"] = null,
                ["error_message"] = null,
                ["elapsed_ms"] = elapsedMs,
                ["payload"] = payloadToken ?? JValue.CreateNull(),
            };
        }

        private static void MarkPayloadAsTruncated(JToken payloadToken, string message)
        {
            var payloadObject = payloadToken as JObject;
            if (payloadObject == null)
                return;

            payloadObject["truncated"] = true;
            var warnings = payloadObject["warnings"] as JArray;
            if (warnings == null)
            {
                warnings = new JArray();
                payloadObject["warnings"] = warnings;
            }

            warnings.Add(message);
        }

        private static bool TrimLargestArray(JToken token)
        {
            var largest = FindLargestArray(token);
            if (largest == null || largest.Count == 0)
                return false;

            var removeCount = Math.Max(1, largest.Count / 2);
            for (var index = 0; index < removeCount && largest.Count > 0; index++)
                largest.RemoveAt(largest.Count - 1);

            MarkArrayAsTruncated(token as JObject, largest);

            return true;
        }

        private static void MarkArrayAsTruncated(JObject payload, JArray array)
        {
            if (payload == null || array == null)
                return;

            var property = array.Parent as JProperty;
            var fieldName = property == null ? string.Empty : property.Name;
            if (string.IsNullOrWhiteSpace(fieldName))
                return;

            var fields = payload["truncated_fields"] as JArray;
            if (fields == null)
            {
                fields = new JArray();
                payload["truncated_fields"] = fields;
            }
            if (!fields.Any(item => string.Equals((string)item, fieldName, StringComparison.OrdinalIgnoreCase)))
                fields.Add(fieldName);

            if (string.Equals(fieldName, "groups", StringComparison.OrdinalIgnoreCase))
            {
                payload["groups_truncated"] = true;
                payload["returned_group_count"] = array.Count;
                var offset = payload.Value<int?>("group_offset").GetValueOrDefault();
                payload["has_more_groups"] = true;
                payload["next_group_offset"] = offset + array.Count;
            }
        }

        private static JArray FindLargestArray(JToken token)
        {
            JArray largest = null;
            FindLargestArray(token, ref largest);
            return largest;
        }

        private static void FindLargestArray(JToken token, ref JArray largest)
        {
            if (token == null)
                return;

            var array = token as JArray;
            if (array != null)
            {
                if (largest == null || array.Count > largest.Count)
                    largest = array;
            }

            foreach (var child in token.Children())
                FindLargestArray(child, ref largest);
        }

        private void WriteError(Stream stream, string requestId, string errorCode, string errorMessage, long elapsedMs)
        {
            var response = new JObject
            {
                ["protocol_version"] = ProtocolConstants.CurrentProtocolVersion,
                ["request_id"] = requestId == null ? (JToken)JValue.CreateNull() : new JValue(requestId),
                ["ok"] = false,
                ["error_code"] = errorCode,
                ["error_message"] = errorMessage,
                ["elapsed_ms"] = Math.Max(0, elapsedMs),
                ["payload"] = JValue.CreateNull(),
            };

            try
            {
                WriteFrame(stream, response.ToString(Formatting.None));
            }
            catch
            {
            }
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy(),
                },
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            };
        }
    }
}
