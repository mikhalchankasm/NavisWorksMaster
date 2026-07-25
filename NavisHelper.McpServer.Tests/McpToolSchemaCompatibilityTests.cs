using System.Text.Json;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpToolSchemaCompatibilityTests
{
    [Fact]
    public void Normalize_ReplacesTrueOnlyInSchemaPositions()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "default": true,
              "enum": [true, false],
              "uniqueItems": true,
              "properties": {
                "anyValue": true,
                "forbidden": false,
                "nested": {
                  "oneOf": [true, { "type": "string" }]
                },
                "tuple": {
                  "items": [true, { "type": "number" }]
                }
              }
            }
            """);

        var normalized = McpToolSchemaCompatibility.Normalize(document.RootElement);

        Assert.True(normalized.GetProperty("default").GetBoolean());
        Assert.Equal(JsonValueKind.True, normalized.GetProperty("enum")[0].ValueKind);
        Assert.Equal(JsonValueKind.False, normalized.GetProperty("enum")[1].ValueKind);
        Assert.True(normalized.GetProperty("uniqueItems").GetBoolean());
        Assert.Equal(JsonValueKind.Object, normalized.GetProperty("properties").GetProperty("anyValue").ValueKind);
        Assert.Empty(normalized.GetProperty("properties").GetProperty("anyValue").EnumerateObject());
        Assert.Equal(JsonValueKind.False, normalized.GetProperty("properties").GetProperty("forbidden").ValueKind);
        Assert.Equal(
            JsonValueKind.Object,
            normalized.GetProperty("properties").GetProperty("nested").GetProperty("oneOf")[0].ValueKind);
        Assert.Equal(
            JsonValueKind.Object,
            normalized.GetProperty("properties").GetProperty("tuple").GetProperty("items")[0].ValueKind);
        Assert.False(normalized.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Normalize_MovesLocalReferencesIntoDefinitions()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "node": {
                  "type": "object",
                  "properties": {
                    "children": {
                      "type": "array",
                      "items": { "$ref": "#/properties/node" }
                    }
                  }
                },
                "alreadyCompatible": { "$ref": "#/$defs/existing" }
              },
              "$defs": {
                "existing": { "type": "string" }
              }
            }
            """);

        var normalized = McpToolSchemaCompatibility.Normalize(document.RootElement);
        var rewrittenReference = normalized
            .GetProperty("properties")
            .GetProperty("node")
            .GetProperty("properties")
            .GetProperty("children")
            .GetProperty("items")
            .GetProperty("$ref")
            .GetString();

        Assert.StartsWith("#/$defs/moonshotCompat", rewrittenReference, StringComparison.Ordinal);
        Assert.Equal(
            "#/$defs/existing",
            normalized.GetProperty("properties").GetProperty("alreadyCompatible").GetProperty("$ref").GetString());

        var definitionName = rewrittenReference!["#/$defs/".Length..];
        var recursiveReference = normalized
            .GetProperty("$defs")
            .GetProperty(definitionName)
            .GetProperty("properties")
            .GetProperty("children")
            .GetProperty("items")
            .GetProperty("$ref")
            .GetString();
        Assert.Equal(rewrittenReference, recursiveReference);
    }

    [Fact]
    public void UnknownArgument_IsRejectedBeforeExecution_WithSuggestion()
    {
        McpToolArgumentValidationFilter.InitializeForTests("clash_manage_tests", "operation", "apply");

        var error = McpToolArgumentValidationFilter.Validate("clash_manage_tests", new[] { "action", "apply" });

        Assert.NotNull(error);
        Assert.Contains("action", error, StringComparison.Ordinal);
        Assert.Contains("operation", error, StringComparison.Ordinal);
        Assert.Contains("not executed", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(McpToolArgumentValidationFilter.Validate("clash_manage_tests", new[] { "operation", "apply" }));
    }
}
