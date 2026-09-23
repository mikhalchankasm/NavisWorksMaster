using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    private static bool TryGetParameterReference(JsonElement value, out string parameterName)
    {
        parameterName = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var properties = value.EnumerateObject().ToList();
        if (properties.Count != 1 || !string.Equals(properties[0].Name, "$parameter", StringComparison.Ordinal))
            return false;
        if (properties[0].Value.ValueKind != JsonValueKind.String)
            return false;
        parameterName = properties[0].Value.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetStepResultReference(JsonElement value, out string reference)
    {
        return TryGetSingleStringReference(value, "$stepResult", out reference);
    }

    private static bool TryGetLoopReference(JsonElement value, out string reference)
    {
        return TryGetSingleStringReference(value, "$loop", out reference);
    }

    private static bool TryGetSingleStringReference(JsonElement value, string propertyName, out string reference)
    {
        reference = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var properties = value.EnumerateObject().ToList();
        if (properties.Count != 1 ||
            !string.Equals(properties[0].Name, propertyName, StringComparison.Ordinal) ||
            properties[0].Value.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        reference = properties[0].Value.GetString() ?? string.Empty;
        return true;
    }

    private static void ValidateStepResultReference(
        string reference,
        IReadOnlyDictionary<string, ToolDescriptor> priorSteps,
        Type targetType,
        ScenarioValidationResult result)
    {
        var dot = (reference ?? string.Empty).IndexOf('.');
        if (dot <= 0 || dot == reference.Length - 1)
        {
            result.Errors.Add("$stepResult must be previousStepId.outputPath: " + reference);
            return;
        }
        var stepId = reference.Substring(0, dot);
        var outputPath = reference.Substring(dot + 1);
        if (!priorSteps.TryGetValue(stepId, out var source))
        {
            result.Errors.Add("$stepResult references an unknown or later stepId: " + stepId);
            return;
        }
        if (!source.OutputProjections.Any(projection =>
                string.Equals(outputPath, projection, StringComparison.Ordinal) ||
                outputPath.StartsWith(projection + ".", StringComparison.Ordinal) ||
                outputPath.StartsWith(projection + "[", StringComparison.Ordinal)))
        {
            result.Errors.Add("$stepResult output is not allowlisted for " + stepId + ": " + outputPath);
            return;
        }

        if (targetType != null && outputPath.IndexOfAny(new[] { '.', '[' }) < 0)
        {
            var responseType = source.Method.ReturnType;
            if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Task<>))
                responseType = responseType.GetGenericArguments()[0];
            var outputProperty = responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(property => string.Equals(property.Name, outputPath, StringComparison.OrdinalIgnoreCase));
            if (outputProperty != null && !ToolTypesAreCompatible(outputProperty.PropertyType, targetType))
                result.Errors.Add("$stepResult type does not match target argument for " + reference);
        }
    }

    private static bool ToolTypesAreCompatible(Type sourceType, Type targetType)
    {
        sourceType = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (targetType.IsAssignableFrom(sourceType))
            return true;
        if (sourceType.IsGenericType && targetType.IsGenericType &&
            sourceType.GetGenericTypeDefinition() == typeof(List<>) &&
            targetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return targetType.GetGenericArguments()[0].IsAssignableFrom(sourceType.GetGenericArguments()[0]);
        }
        return false;
    }

    private static bool ContainsNestedParameterReference(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (string.Equals(property.Name, "$parameter", StringComparison.Ordinal) ||
                    ContainsNestedParameterReference(property.Value))
                    return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().Any(ContainsNestedParameterReference);
        }
        return false;
    }

    private static bool ValueMatchesParameterType(JsonElement value, string type)
    {
        return type switch
        {
            "string" or "enum" or "filePath" or "directoryPath" => value.ValueKind == JsonValueKind.String,
            "int" or "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "bool" or "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "stringList" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
            _ => false,
        };
    }

    private static bool ParameterTypeMatchesToolType(string parameterType, Type targetType)
    {
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return parameterType switch
        {
            "string" or "enum" or "filePath" or "directoryPath" => targetType == typeof(string),
            "int" or "integer" => targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(short),
            "number" => targetType == typeof(float) || targetType == typeof(double) || targetType == typeof(decimal),
            "bool" or "boolean" => targetType == typeof(bool),
            "stringList" => targetType == typeof(List<string>) || targetType == typeof(string[]),
            _ => false,
        };
    }

    private static bool ParameterValueMeetsConstraints(
        ScenarioParameterDefinition parameter,
        JsonElement value,
        out string error)
    {
        error = string.Empty;
        if (parameter == null)
            return true;

        if ((parameter.Enum?.Count ?? 0) > 0 &&
            !parameter.Enum.Any(candidate => string.Equals(candidate.GetRawText(), value.GetRawText(), StringComparison.Ordinal)))
        {
            error = "значение не входит в enum";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parameter.Pattern) && value.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!Regex.IsMatch(
                        value.GetString() ?? string.Empty,
                        parameter.Pattern,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(250)))
                {
                    error = "значение не соответствует pattern";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                error = "pattern некорректен";
                return false;
            }
        }

        return true;
    }

    private static bool CanDeserialize(JsonElement value, Type targetType)
    {
        try
        {
            JsonSerializer.Deserialize(value.GetRawText(), targetType, ToolInputJsonOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFileSystemPathArgument(string name)
    {
        return FileSystemPathArguments.Contains(name ?? string.Empty);
    }

    private static bool ContainsForbiddenPropertyName(JsonElement value, out string propertyName)
    {
        propertyName = string.Empty;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (RuntimeIdentityArguments.Contains(property.Name) || IsCredentialName(property.Name))
                {
                    propertyName = property.Name;
                    return true;
                }
                if (ContainsForbiddenPropertyName(property.Value, out propertyName))
                    return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (ContainsForbiddenPropertyName(item, out propertyName))
                    return true;
            }
        }
        return false;
    }

    private static void ValidateSectionBoxLiteral(JsonElement value, ScenarioValidationResult result)
    {
        if (ContainsScenarioReferenceProperty(value, out var referenceProperty))
        {
            result.Errors.Add("isolate_by_box box must be literal geometry and cannot contain " + referenceProperty);
            return;
        }
        if (ContainsForbiddenPropertyName(value, out var forbiddenProperty))
        {
            result.Errors.Add("runtime identity or credential property cannot be stored: " + forbiddenProperty);
            return;
        }

        try
        {
            var geometry = JsonSerializer.Deserialize<SectionBoxGeometry>(
                value.GetRawText(),
                SectionBoxLiteralJsonOptions);
            SectionBoxGeometryRules.Validate(geometry);
        }
        catch (Exception exception) when (
            exception is JsonException ||
            exception is NotSupportedException ||
            exception is ArgumentException)
        {
            result.Errors.Add("isolate_by_box box must be valid literal canonical geometry: " + SafeMessage(exception));
            return;
        }

        ValidateJsonStrings(value, "argument box", result);
    }

    private static void ValidateSectionBoxTraversalLimit(JsonElement value, ScenarioValidationResult result)
    {
        if (ContainsScenarioReferenceProperty(value, out var referenceProperty))
        {
            result.Errors.Add("isolate_by_box maxScannedItems must be a literal integer and cannot contain " + referenceProperty);
            return;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var limit) ||
            limit < 1 || limit > SectionBoxIsolationLimits.MaximumMaxScannedItems)
        {
            result.Errors.Add(
                "isolate_by_box maxScannedItems must be a literal integer between 1 and " +
                SectionBoxIsolationLimits.MaximumMaxScannedItems);
        }
    }

    private static void ValidateSectionBoxDurationLimit(JsonElement value, ScenarioValidationResult result)
    {
        if (ContainsScenarioReferenceProperty(value, out var referenceProperty))
        {
            result.Errors.Add("isolate_by_box maxDurationSeconds must be a literal integer and cannot contain " + referenceProperty);
            return;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var limit) ||
            limit < 1 || limit > SectionBoxIsolationLimits.MaximumMaxDurationSeconds)
        {
            result.Errors.Add(
                "isolate_by_box maxDurationSeconds must be a literal integer between 1 and " +
                SectionBoxIsolationLimits.MaximumMaxDurationSeconds);
        }
    }

    private static void ValidateSectionBoxDurationSafetyEnvelope(
        ScenarioStepDefinition step,
        ScenarioStepSafetyLimit limit,
        ScenarioValidationResult result)
    {
        if (!limit.MaxDurationSeconds.HasValue)
        {
            result.Errors.Add("exactReplay safety maxDurationSeconds is required for " + step.StepId);
            return;
        }
        if (limit.MaxDurationSeconds.Value < 1 ||
            limit.MaxDurationSeconds.Value > SectionBoxIsolationLimits.MaximumMaxDurationSeconds)
        {
            result.Errors.Add(
                "exactReplay safety maxDurationSeconds must be between 1 and " +
                SectionBoxIsolationLimits.MaximumMaxDurationSeconds + " for " + step.StepId);
            return;
        }
        if (!step.Arguments.TryGetValue("maxDurationSeconds", out var argument) ||
            argument.ValueKind != JsonValueKind.Number ||
            !argument.TryGetInt32(out var literalDuration) ||
            literalDuration != limit.MaxDurationSeconds.Value)
        {
            result.Errors.Add(
                "exactReplay safety maxDurationSeconds must equal the literal isolate_by_box argument for " +
                step.StepId);
        }
    }

    private static bool ContainsScenarioReferenceProperty(JsonElement value, out string propertyName)
    {
        propertyName = string.Empty;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "$stepResult" or "$parameter" or "$loop")
                {
                    propertyName = property.Name;
                    return true;
                }
                if (ContainsScenarioReferenceProperty(property.Value, out propertyName))
                    return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (ContainsScenarioReferenceProperty(item, out propertyName))
                    return true;
            }
        }
        return false;
    }

    private static void ValidateFixedPath(JsonElement value, string parameterName, ScenarioValidationResult result)
    {
        var path = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || (!Path.IsPathFullyQualified(path) && !path.StartsWith("\\\\", StringComparison.Ordinal)))
            result.Errors.Add("exactReplay path must be absolute: " + parameterName);
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            result.Errors.Add("device paths are forbidden: " + parameterName);
        if (path.Contains("%", StringComparison.Ordinal) || path.Contains("$", StringComparison.Ordinal))
            result.Errors.Add("environment-variable paths are forbidden: " + parameterName);
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == ".."))
            result.Errors.Add("path traversal is forbidden: " + parameterName);
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var authorityEnd = path.IndexOf('\\', 2);
            var authority = authorityEnd > 2 ? path.Substring(2, authorityEnd - 2) : string.Empty;
            if (authority.Contains('@'))
                result.Errors.Add("credentials in UNC paths are forbidden: " + parameterName);
        }
    }

    private static void ValidateContext(ScenarioContext context, ScenarioValidationResult result)
    {
        if (context == null)
            return;
        context.NavisworksVersions ??= new List<string>();
        context.RootFilePatterns ??= new List<string>();
        if (context.NavisworksVersions.Count > 4)
            result.Errors.Add("context.navisworksVersions cannot contain more than 4 items");
        if (context.RootFilePatterns.Count > 32)
            result.Errors.Add("context.rootFilePatterns cannot contain more than 32 items");
        foreach (var version in context.NavisworksVersions)
        {
            if (version is not ("2024" or "2025" or "2026" or "2027"))
                result.Errors.Add("unsupported Navisworks version: " + version);
        }
        foreach (var pattern in context.RootFilePatterns)
            ValidateDisplayText(pattern, "root file pattern", 1, 260, result);
        ValidateDisplayText(context.ProjectLabel, "project label", 0, 200, result);
    }

    private static void ValidateDisplayText(string value, string field, int minimum, int maximum, ScenarioValidationResult result)
    {
        value ??= string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length < minimum || trimmed.Length > maximum)
            result.Errors.Add(field + " length must be between " + minimum + " and " + maximum);
        if (value.Any(IsForbiddenTextCharacter))
            result.Errors.Add(field + " contains control or bidi characters");
    }

    private static void ValidateJsonStrings(JsonElement value, string field, ScenarioValidationResult result)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            ValidateDisplayText(value.GetString(), field, 0, 4096, result);
            var text = value.GetString() ?? string.Empty;
            if (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
                result.Errors.Add(field + " contains a credential-like literal");
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                ValidateJsonStrings(item, field, result);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (IsCredentialName(property.Name))
                    result.Errors.Add(field + " contains a credential-shaped property");
                ValidateJsonStrings(property.Value, field, result);
            }
        }
    }

    private static bool IsForbiddenTextCharacter(char character)
    {
        return char.IsControl(character) || character == '\u001b' ||
               character is '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or
                   '\u2066' or '\u2067' or '\u2068' or '\u2069';
    }
}
