using System.Text.Json;
using System.Text.Json.Nodes;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpToolSchemaCompatibilityTests
{
    [Theory]
    [InlineData("https://example.invalid/schema")]
    [InlineData("#/missing")]
    public void Normalize_RejectsInvalidContentSchemaReferences(string reference)
    {
        var schema = new JsonObject
        {
            ["type"] = "string",
            ["contentMediaType"] = "application/json",
            ["contentSchema"] = new JsonObject { ["$ref"] = reference },
        };
        Assert.Throws<InvalidOperationException>(() => McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema)));
    }

    [Fact]
    public void Normalize_NormalizesContentSchemaAndPreservesIdenticallyNamedData()
    {
        var schema = JsonNode.Parse("""
            { "type": "string", "contentMediaType": "application/json",
              "default": { "contentSchema": { "$ref": "https://example.invalid/data" } },
              "definitions": { "target": { "type": "string" } },
              "contentSchema": { "properties": {
                "any": true, "none": false, "reference": { "$ref": "#/definitions/target" }
              } } }
            """)!;
        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));
        var properties = normalized.GetProperty("contentSchema").GetProperty("properties");
        Assert.Empty(properties.GetProperty("any").EnumerateObject());
        Assert.False(properties.GetProperty("none").GetBoolean());
        Assert.Equal("#/$defs/moonshotCompat1", properties.GetProperty("reference").GetProperty("$ref").GetString());
        Assert.True(JsonNode.DeepEquals(schema["default"], JsonNode.Parse(normalized.GetProperty("default").GetRawText())));
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Theory]
    [InlineData("#/$defs/missing")]
    [InlineData("#/$defs/missing~1name")]
    public void Normalize_RejectsMissingDirectDefinitions(string reference)
    {
        var schema = new JsonObject { ["$defs"] = new JsonObject(), ["$ref"] = reference };
        Assert.Throws<InvalidOperationException>(() => McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema)));
    }

    [Fact]
    public void Normalize_TerminatesForMutuallyRecursiveDirectDefinitions()
    {
        using var schema = JsonDocument.Parse("""
            { "$ref": "#/$defs/a~1b", "$defs": {
              "a/b": { "properties": { "child": { "$ref": "#/$defs/c" } } },
              "c": { "$ref": "#/$defs/a~1b" }
            } }
            """);
        var normalized = McpToolSchemaCompatibility.Normalize(schema.RootElement);
        Assert.Equal("#/$defs/a~1b", normalized.GetProperty("$ref").GetString());
        Assert.Equal(2, normalized.GetProperty("$defs").EnumerateObject().Count());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Fact]
    public void Normalize_NormalizesReferencedSchemaCopyWithoutChangingOriginalData()
    {
        var schema = JsonNode.Parse("""
            { "default": { "properties": { "any": true, "none": false },
                           "items": [true, false], "contentSchema": true },
              "properties": { "actual": { "$ref": "#/default" } } }
            """)!;
        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));
        var copy = normalized.GetProperty("$defs").GetProperty("moonshotCompat1");
        Assert.Empty(copy.GetProperty("properties").GetProperty("any").EnumerateObject());
        Assert.False(copy.GetProperty("properties").GetProperty("none").GetBoolean());
        Assert.Empty(copy.GetProperty("items")[0].EnumerateObject());
        Assert.False(copy.GetProperty("items")[1].GetBoolean());
        Assert.Empty(copy.GetProperty("contentSchema").EnumerateObject());
        Assert.True(JsonNode.DeepEquals(schema["default"], JsonNode.Parse(normalized.GetProperty("default").GetRawText())));
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Normalize_NormalizesReferencedBooleanCopyWithoutChangingData(bool value)
    {
        var schema = new JsonObject { ["default"] = value, ["$ref"] = "#/default" };
        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));
        Assert.Equal(value, normalized.GetProperty("default").GetBoolean());
        var copy = normalized.GetProperty("$defs").GetProperty("moonshotCompat1");
        if (value)
            Assert.Empty(copy.EnumerateObject());
        else
            Assert.False(copy.GetBoolean());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    public static TheoryData<string, string> LiteralReferenceCases
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (var keyword in new[] { "default", "const", "enum", "examples", "x-annotation" })
            foreach (var reference in new[] { "https://example.invalid/schema", "#/missing", "#/properties/node" })
                cases.Add(keyword, reference);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(LiteralReferenceCases))]
    public void Normalize_PreservesReferenceFieldsInInstanceData(string keyword, string reference)
    {
        var schema = JsonNode.Parse("""
            { "type": "object", "properties": {
              "node": { "type": "string" },
              "actual": { "$ref": "#/properties/node" }
            } }
            """)!.AsObject();
        var data = new JsonObject
        {
            ["$ref"] = reference,
            ["properties"] = new JsonObject { ["nested"] = new JsonObject { ["$ref"] = reference } },
            ["items"] = new JsonArray(new JsonObject { ["$ref"] = reference }),
        };
        schema[keyword] = keyword is "enum" or "examples" ? new JsonArray(data) : data;

        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));

        Assert.True(JsonNode.DeepEquals(schema[keyword], JsonNode.Parse(normalized.GetProperty(keyword).GetRawText())));
        Assert.StartsWith("#/$defs/moonshotCompat",
            normalized.GetProperty("properties").GetProperty("actual").GetProperty("$ref").GetString());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Theory]
    [InlineData("items", "value")]
    [InlineData("additionalProperties", "value")]
    [InlineData("if", "value")]
    [InlineData("items", "array")]
    [InlineData("allOf", "array")]
    [InlineData("prefixItems", "array")]
    [InlineData("properties", "map")]
    [InlineData("patternProperties", "map")]
    [InlineData("dependentSchemas", "map")]
    [InlineData("dependencies", "map")]
    [InlineData("definitions", "map")]
    [InlineData("$defs", "map")]
    public void Normalize_RewritesReferencesInSchemaSlots(string keyword, string shape)
    {
        var schema = new JsonObject { ["type"] = "object" };
        var reference = new JsonObject { ["$ref"] = "#/allOf/0" };
        schema[keyword] = shape switch
        {
            "array" => new JsonArray(reference),
            "map" => new JsonObject { ["nested"] = reference },
            _ => reference,
        };
        // The reference target is always a schema; allOf itself is also exercised.
        if (keyword == "allOf")
            schema["allOf"]!.AsArray().Insert(0, new JsonObject { ["type"] = "string" });
        else
            schema["allOf"] = new JsonArray(new JsonObject { ["type"] = "string" });

        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));
        var slot = normalized.GetProperty(keyword);
        var nested = shape switch
        {
            "array" => slot[keyword == "allOf" ? 1 : 0],
            "map" => slot.GetProperty("nested"),
            _ => slot,
        };
        Assert.Equal("#/$defs/moonshotCompat1", nested.GetProperty("$ref").GetString());
        Assert.Equal("string", normalized.GetProperty("$defs").GetProperty("moonshotCompat1").GetProperty("type").GetString());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Theory]
    [InlineData("https://example.invalid/schema")]
    [InlineData("#/missing")]
    public void Normalize_StillRejectsInvalidActualReferences(string reference)
    {
        var schema = new JsonObject { ["properties"] = new JsonObject { ["node"] = new JsonObject { ["$ref"] = reference } } };
        Assert.Throws<InvalidOperationException>(() => McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema)));
    }

    [Fact]
    public void Normalize_ReferencedDataBecomesSchemaOnlyInItsDefinitionCopy()
    {
        var schema = JsonNode.Parse("""
            { "default": { "$ref": "#/properties/node" },
              "properties": { "node": { "type": "string" }, "actual": { "$ref": "#/default" } } }
            """)!;

        var normalized = McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema));

        Assert.Equal("#/properties/node", normalized.GetProperty("default").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/moonshotCompat2", normalized.GetProperty("$defs").GetProperty("moonshotCompat1").GetProperty("$ref").GetString());
        Assert.Equal("string", normalized.GetProperty("$defs").GetProperty("moonshotCompat2").GetProperty("type").GetString());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Fact]
    public void Normalize_RejectsInvalidReferenceReachedThroughDataTarget()
    {
        using var schema = JsonDocument.Parse("""
            { "default": { "$ref": "https://example.invalid/schema" }, "$ref": "#/default" }
            """);
        Assert.Throws<InvalidOperationException>(() => McpToolSchemaCompatibility.Normalize(schema.RootElement));
    }

    [Theory]
    [InlineData("https://example.invalid/schema")]
    [InlineData("#/missing")]
    public void Normalize_RejectsInvalidReferenceReachedThroughDeepDefinitionTarget(string reference)
    {
        var schema = JsonNode.Parse("""
            { "$defs": { "x": { "default": {} } },
              "properties": { "actual": { "$ref": "#/$defs/x/default" } } }
            """)!;
        schema["$defs"]!["x"]!["default"]!["$ref"] = reference;
        Assert.Throws<InvalidOperationException>(() => McpToolSchemaCompatibility.Normalize(JsonSerializer.SerializeToElement(schema)));
    }

    [Fact]
    public void Normalize_ClonesDeepDefinitionTargetWithoutChangingItsData()
    {
        using var schema = JsonDocument.Parse("""
            { "$defs": { "x": { "default": { "$ref": "#/properties/node" } } },
              "properties": { "node": { "type": "string" }, "actual": { "$ref": "#/$defs/x/default" } } }
            """);
        var normalized = McpToolSchemaCompatibility.Normalize(schema.RootElement);
        Assert.Equal("#/properties/node", normalized.GetProperty("$defs").GetProperty("x").GetProperty("default").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/moonshotCompat1", normalized.GetProperty("properties").GetProperty("actual").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/moonshotCompat2", normalized.GetProperty("$defs").GetProperty("moonshotCompat1").GetProperty("$ref").GetString());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

    [Fact]
    public void Normalize_TerminatesForCycleThroughDataTarget()
    {
        using var schema = JsonDocument.Parse("""
            { "default": { "$ref": "#/default" }, "properties": { "actual": { "$ref": "#/default" } } }
            """);
        var normalized = McpToolSchemaCompatibility.Normalize(schema.RootElement);
        Assert.Equal("#/default", normalized.GetProperty("default").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/moonshotCompat1", normalized.GetProperty("$defs").GetProperty("moonshotCompat1").GetProperty("$ref").GetString());
        Assert.Equal(normalized.GetRawText(), McpToolSchemaCompatibility.Normalize(normalized).GetRawText());
    }

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
