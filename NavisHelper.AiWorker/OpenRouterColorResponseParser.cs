using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NavisHelper.AI
{
    internal sealed class OpenRouterColorParseResult
    {
        private OpenRouterColorParseResult(
            bool isSuccess,
            AiColorOutcomeKind failureKind,
            Dictionary<string, string> colors,
            string finishReason)
        {
            IsSuccess = isSuccess;
            FailureKind = failureKind;
            Colors = colors ?? new Dictionary<string, string>(
                StringComparer.Ordinal);
            FinishReason = finishReason ?? string.Empty;
        }

        internal bool IsSuccess { get; }
        internal AiColorOutcomeKind FailureKind { get; }
        internal Dictionary<string, string> Colors { get; }
        internal string FinishReason { get; }

        internal static OpenRouterColorParseResult Success(
            Dictionary<string, string> colors,
            string finishReason = null)
        {
            return new OpenRouterColorParseResult(
                true,
                AiColorOutcomeKind.Success,
                colors,
                finishReason);
        }

        internal static OpenRouterColorParseResult Failure(
            AiColorOutcomeKind kind,
            string finishReason = null)
        {
            return new OpenRouterColorParseResult(
                false,
                kind,
                null,
                finishReason);
        }
    }

    internal static class OpenRouterColorResponseParser
    {
        internal static OpenRouterColorParseResult Parse(
            string responseJson,
            IEnumerable<string> requestedObjectNames)
        {
            var requested = new HashSet<string>(
                requestedObjectNames ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            if (requested.Count == 0 || string.IsNullOrWhiteSpace(responseJson))
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.StructuredPayloadInvalid);

            JObject response;
            try
            {
                response = JObject.Parse(responseJson);
            }
            catch (JsonException)
            {
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.StructuredPayloadInvalid);
            }

            var choice = response.SelectToken("choices[0]") as JObject;
            var finishReason = (string)choice?["finish_reason"] ??
                               (string)choice?["native_finish_reason"];
            if (string.Equals(
                    finishReason,
                    "length",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    finishReason,
                    "max_tokens",
                    StringComparison.OrdinalIgnoreCase))
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.TruncatedResponse,
                    finishReason);
            if (string.Equals(
                    finishReason,
                    "content_filter",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    finishReason,
                    "safety",
                    StringComparison.OrdinalIgnoreCase))
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.ResponseRefused,
                    finishReason);

            var message = choice?["message"] as JObject;
            if (HasRefusal(message))
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.ResponseRefused,
                    finishReason);

            var content = ExtractTextContent(message?["content"]);
            if (string.IsNullOrWhiteSpace(content))
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.MissingAssistantContent,
                    finishReason);

            JObject payload;
            try
            {
                payload = JObject.Parse(content);
            }
            catch (JsonException)
            {
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.StructuredPayloadInvalid,
                    finishReason);
            }

            if (payload.Properties().Count() != 1 ||
                payload.Property("colors") == null)
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.StructuredPayloadInvalid);
            var items = payload["colors"] as JArray;
            if (items == null)
                return OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.StructuredPayloadInvalid);

            var colors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in items)
            {
                var item = token as JObject;
                if (item == null || item.Properties().Count() != 2 ||
                    item.Property("object") == null ||
                    item.Property("color") == null ||
                    item["object"].Type != JTokenType.String ||
                    item["color"].Type != JTokenType.String)
                    return OpenRouterColorParseResult.Failure(
                        AiColorOutcomeKind.StructuredPayloadInvalid,
                        finishReason);

                var objectName = (string)item["object"];
                if (string.IsNullOrEmpty(objectName) ||
                    !requested.Contains(objectName) ||
                    colors.ContainsKey(objectName))
                    return OpenRouterColorParseResult.Failure(
                        AiColorOutcomeKind.IncompleteObjectSet,
                        finishReason);

                string normalizedColor;
                if (!TryNormalizeColor(
                        (string)item["color"],
                        out normalizedColor))
                    return OpenRouterColorParseResult.Failure(
                        AiColorOutcomeKind.StructuredPayloadInvalid,
                        finishReason);
                colors[objectName] = normalizedColor;
            }

            return colors.Count == requested.Count
                ? OpenRouterColorParseResult.Success(colors, finishReason)
                : OpenRouterColorParseResult.Failure(
                    AiColorOutcomeKind.IncompleteObjectSet,
                    finishReason);
        }

        internal static bool TryParse(
            string responseJson,
            IEnumerable<string> requestedObjectNames,
            out Dictionary<string, string> colors)
        {
            var result = Parse(responseJson, requestedObjectNames);
            colors = result.Colors;
            return result.IsSuccess;
        }

        private static bool HasRefusal(JObject message)
        {
            if (message == null)
                return false;
            var refusal = message["refusal"];
            if (refusal != null && refusal.Type != JTokenType.Null &&
                !string.IsNullOrWhiteSpace(refusal.ToString()))
                return true;
            var parts = message["content"] as JArray;
            return parts != null && parts.OfType<JObject>().Any(part =>
                string.Equals(
                    (string)part["type"],
                    "refusal",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractTextContent(JToken contentToken)
        {
            if (contentToken == null)
                return null;
            if (contentToken.Type == JTokenType.String)
                return (string)contentToken;
            var parts = contentToken as JArray;
            return parts == null
                ? null
                : string.Concat(parts.OfType<JObject>()
                    .Where(part => string.Equals(
                        (string)part["type"],
                        "text",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(part => (string)part["text"]));
        }

        private static bool TryNormalizeColor(
            string value,
            out string normalized)
        {
            normalized = null;
            var parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 3)
                return false;
            var channels = new int[3];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(
                        parts[index].Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out channels[index]) ||
                    channels[index] < 0 || channels[index] > 255)
                    return false;
            }
            normalized = string.Join(",", channels.Select(channel =>
                channel.ToString(CultureInfo.InvariantCulture)));
            return true;
        }
    }
}
