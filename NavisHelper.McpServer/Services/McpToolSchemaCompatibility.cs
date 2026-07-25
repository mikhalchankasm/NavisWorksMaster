using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpToolSchemaCompatibility
{
    private static readonly HashSet<string> SchemaValueKeywords = new(StringComparer.Ordinal)
    {
        "additionalItems",
        "additionalProperties",
        "contains",
        "else",
        "if",
        "items",
        "not",
        "propertyNames",
        "then",
        "unevaluatedItems",
        "unevaluatedProperties",
    };

    private static readonly HashSet<string> SchemaArrayKeywords = new(StringComparer.Ordinal)
    {
        "allOf",
        "anyOf",
        "oneOf",
        "prefixItems",
    };

    private static readonly HashSet<string> SchemaMapKeywords = new(StringComparer.Ordinal)
    {
        "$defs",
        "definitions",
        "dependencies",
        "dependentSchemas",
        "patternProperties",
        "properties",
    };

    internal static SchemaNormalizationResult Normalize(IEnumerable<McpServerTool> tools)
    {
        var toolCount = 0;
        var rewriteCount = 0;
        foreach (var tool in tools)
        {
            toolCount++;
            var normalized = Normalize(tool.ProtocolTool.InputSchema, out var toolRewriteCount);
            tool.ProtocolTool.InputSchema = normalized;
            rewriteCount += toolRewriteCount;

            if (tool.ProtocolTool.OutputSchema is { } outputSchema)
            {
                tool.ProtocolTool.OutputSchema = Normalize(outputSchema, out var outputRewriteCount);
                rewriteCount += outputRewriteCount;
            }
        }

        if (toolCount == 0)
            throw new InvalidOperationException("No MCP tools were resolved for JSON Schema normalization.");

        return new SchemaNormalizationResult(toolCount, rewriteCount);
    }

    internal static JsonElement Normalize(JsonElement schema)
    {
        return Normalize(schema, out _);
    }

    private static JsonElement Normalize(JsonElement schema, out int rewriteCount)
    {
        var node = JsonNode.Parse(schema.GetRawText())
            ?? throw new InvalidOperationException("MCP tool input schema cannot be null.");
        if (node is not JsonObject schemaObject)
            throw new InvalidOperationException("MCP tool input schema must be an object.");

        rewriteCount = NormalizeSchemaObject(schemaObject);
        rewriteCount += NormalizeReferences(schemaObject);
        if (!schemaObject.ContainsKey("additionalProperties"))
            schemaObject["additionalProperties"] = false;
        return JsonSerializer.SerializeToElement(node);
    }

    private static int NormalizeReferences(JsonObject schema)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        CollectIncompatibleReferences(schema, references);
        if (references.Count == 0)
            return 0;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitions = schema["$defs"] as JsonObject ?? new JsonObject();
        var index = 1;

        foreach (var reference in references.OrderBy(value => value, StringComparer.Ordinal))
        {
            var definitionName = "moonshotCompat" + index++;
            while (definitions.ContainsKey(definitionName))
                definitionName = "moonshotCompat" + index++;

            var target = ResolveLocalReference(schema, reference);
            definitions[definitionName] = target.DeepClone();
            replacements[reference] = "#/$defs/" + definitionName;
        }

        schema["$defs"] = definitions;
        return ReplaceReferences(schema, replacements);
    }

    private static void CollectIncompatibleReferences(JsonNode node, ISet<string> references)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject["$ref"] is JsonValue referenceValue &&
                referenceValue.TryGetValue<string>(out var reference) &&
                !reference.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                references.Add(reference);
            }

            foreach (var property in jsonObject)
            {
                if (property.Value is not null)
                    CollectIncompatibleReferences(property.Value, references);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                    CollectIncompatibleReferences(item, references);
            }
        }
    }

    private static JsonNode ResolveLocalReference(JsonObject schema, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new InvalidOperationException("Only local MCP tool schema references can be normalized: " + reference);

        JsonNode current = schema;
        foreach (var rawSegment in reference.Substring(2).Split('/'))
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            current = current switch
            {
                JsonObject jsonObject when jsonObject[segment] is { } child => child,
                JsonArray jsonArray when int.TryParse(segment, out var arrayIndex) &&
                                         arrayIndex >= 0 &&
                                         arrayIndex < jsonArray.Count &&
                                         jsonArray[arrayIndex] is { } child => child,
                _ => throw new InvalidOperationException("Unresolvable MCP tool schema reference: " + reference),
            };
        }

        return current;
    }

    private static int ReplaceReferences(JsonNode node, IReadOnlyDictionary<string, string> replacements)
    {
        var rewriteCount = 0;
        if (node is JsonObject jsonObject)
        {
            if (jsonObject["$ref"] is JsonValue referenceValue &&
                referenceValue.TryGetValue<string>(out var reference) &&
                replacements.TryGetValue(reference, out var replacement))
            {
                jsonObject["$ref"] = replacement;
                rewriteCount++;
            }

            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is not null)
                    rewriteCount += ReplaceReferences(property.Value, replacements);
            }

            return rewriteCount;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                    rewriteCount += ReplaceReferences(item, replacements);
            }
        }

        return rewriteCount;
    }

    private static int NormalizeSchemaObject(JsonObject schema)
    {
        var rewriteCount = 0;
        foreach (var property in schema.ToList())
        {
            if (SchemaValueKeywords.Contains(property.Key))
            {
                rewriteCount += NormalizeSchemaSlot(
                    schema,
                    property.Key,
                    allowSchemaArray: property.Key == "items");
                continue;
            }

            if (SchemaArrayKeywords.Contains(property.Key) && property.Value is JsonArray schemaArray)
            {
                for (var index = 0; index < schemaArray.Count; index++)
                    rewriteCount += NormalizeSchemaSlot(schemaArray, index);
                continue;
            }

            if (SchemaMapKeywords.Contains(property.Key) && property.Value is JsonObject schemaMap)
            {
                foreach (var entry in schemaMap.ToList())
                    rewriteCount += NormalizeSchemaSlot(schemaMap, entry.Key);
            }
        }

        return rewriteCount;
    }

    private static int NormalizeSchemaSlot(
        JsonObject parent,
        string propertyName,
        bool allowSchemaArray = false)
    {
        var node = parent[propertyName];
        if (IsTrueSchema(node))
        {
            parent[propertyName] = new JsonObject();
            return 1;
        }

        if (node is JsonObject nestedSchema)
            return NormalizeSchemaObject(nestedSchema);

        if (allowSchemaArray && node is JsonArray schemaArray)
        {
            var rewriteCount = 0;
            for (var index = 0; index < schemaArray.Count; index++)
                rewriteCount += NormalizeSchemaSlot(schemaArray, index);
            return rewriteCount;
        }

        return 0;
    }

    private static int NormalizeSchemaSlot(JsonArray parent, int index)
    {
        var node = parent[index];
        if (IsTrueSchema(node))
        {
            parent[index] = new JsonObject();
            return 1;
        }

        return node is JsonObject nestedSchema ? NormalizeSchemaObject(nestedSchema) : 0;
    }

    private static bool IsTrueSchema(JsonNode node)
    {
        return node is JsonValue value &&
               value.TryGetValue<bool>(out var allowed) &&
               allowed;
    }
}

internal readonly record struct SchemaNormalizationResult(int ToolCount, int RewriteCount);
