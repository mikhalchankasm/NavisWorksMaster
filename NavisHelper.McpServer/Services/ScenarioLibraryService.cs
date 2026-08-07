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

internal sealed class ScenarioLibraryService
{
    internal const int CurrentSchemaVersion = 2;
    internal const int MaxScenarioFiles = 500;
    internal const long MaxScenarioFileBytes = 256 * 1024;

    private static readonly object ProcessLock = new();
    private static readonly Mutex CrossProcessStoreMutex = new(false, "NavisHelperScenarioLibraryStore");
    private static readonly Regex NameRegex = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ParameterTypes = new(StringComparer.Ordinal)
    {
        "string", "int", "integer", "number", "bool", "boolean", "enum", "filePath", "directoryPath", "stringList",
    };
    private static readonly HashSet<string> RuntimeIdentityArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "instanceId", "navisworksVersion", "pid", "documentId", "documentHandle",
        "matchHandle", "matchHandles", "scopeHandle", "itemId", "itemIds", "testHandle", "testHandles",
        "resultHandle", "resultHandles", "groupHandle", "groupHandles", "viewpointHandle",
        "viewpointHandles",
    };
    private static readonly HashSet<string> FileSystemPathArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "filePath", "outputPath", "outputDirectory", "planOutputPath",
    };
    private static readonly HashSet<string> ReviewedWriteBehaviorArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "overwrite", "overwriteExisting", "runTests", "runAfterCreate", "append", "removePreviousGenerated",
    };
    private static readonly string[] CredentialNameTerms =
    {
        "secret", "password", "credential", "apikey", "accesstoken", "bearertoken", "clientsecret", "authorization",
    };
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> ToolDescriptors = CreateToolDescriptors();

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions ToolInputJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
    };

    private readonly string _rootPath;

    public ScenarioLibraryService()
        : this(GetDefaultRootPath())
    {
    }

    internal ScenarioLibraryService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Scenario root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    internal string RootPath => _rootPath;

    public ScenarioListResponse List(
        string query,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 20);
        var response = new ScenarioListResponse
        {
            ScenarioDirectory = _rootPath,
            AllowedTools = ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
        };
        var records = ReadAll(response.Warnings);

        IEnumerable<ScenarioStoredRecord> filtered = records;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(record =>
                record.Scenario.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (record.Scenario.Description ?? string.Empty).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        response.Scenarios = filtered
            .Select(record =>
            {
                var match = MatchContext(record.Scenario.Context, navisworksVersion, rootFileNames, projectLabel);
                return new ScenarioListItem
                {
                    ScenarioId = record.Scenario.ScenarioId,
                    Name = record.Scenario.Name,
                    Description = record.Scenario.Description,
                    ExecutionMode = record.Scenario.ExecutionMode,
                    UpdatedUtc = record.Scenario.UpdatedUtc,
                    Sha256 = record.Sha256,
                    MatchGrade = match.Grade,
                    MatchReasons = match.Reasons,
                };
            })
            .OrderBy(item => MatchRank(item.MatchGrade))
            .ThenByDescending(item => item.UpdatedUtc)
            .Take(limit)
            .ToList();
        response.Count = response.Scenarios.Count;
        return response;
    }

    public ScenarioGetResponse Get(string scenarioId)
    {
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
        {
            return new ScenarioGetResponse
            {
                Ok = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        return new ScenarioGetResponse
        {
            Ok = true,
            Scenario = record.Scenario,
            Sha256 = record.Sha256,
            FilePath = record.FilePath,
            Warnings = record.Warnings,
        };
    }

    public ScenarioMutationResponse Save(
        ScenarioDraft draft,
        string scenarioId,
        string expectedSha256,
        bool apply,
        bool confirmSave,
        bool confirmExactReplay)
    {
        if (draft != null)
        {
            draft.Name = (draft.Name ?? string.Empty).Trim();
            draft.Description = (draft.Description ?? string.Empty).Trim();
        }
        var response = new ScenarioMutationResponse
        {
            Applied = false,
            ScenarioName = draft?.Name ?? string.Empty,
            ExecutionMode = draft?.ExecutionMode ?? string.Empty,
            AllowedTools = ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
        };

        var validation = ValidateDraft(draft, verifyExactFingerprint: false);
        if (validation.IsValid && IsExactReplay(draft))
        {
            draft.ExactReplay.SafetyEnvelope.PreviewFingerprint = "sha256:" + ComputeExactReplayFingerprint(draft);
            validation = ValidateDraft(draft, verifyExactFingerprint: true);
        }
        response.Errors.AddRange(validation.Errors);
        response.Warnings.AddRange(validation.Warnings);
        if (!validation.IsValid)
            return Fail(response, "scenario_invalid", "Сценарий не прошёл проверку.");

        var isUpdate = !string.IsNullOrWhiteSpace(scenarioId);
        ScenarioStoredRecord existing = null;
        if (isUpdate)
        {
            existing = ReadById(scenarioId, out var errorCode, out var errorMessage);
            if (existing == null)
                return Fail(response, errorCode, errorMessage);
            if (string.IsNullOrWhiteSpace(expectedSha256) ||
                !string.Equals(existing.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(response, "scenario_conflict", "Сценарий изменился после чтения. Получите актуальную версию и повторите сохранение.");
            }
        }

        var duplicate = FindExactNameCollision(draft.Name, isUpdate ? existing.Scenario.ScenarioId : string.Empty);
        if (IsExactReplay(draft) && duplicate != null)
            return Fail(response, "scenario_name_conflict", "Точный сценарий с таким именем уже существует. Используйте уникальное имя.");

        var now = DateTime.UtcNow;
        var document = CopyDraft(draft);
        document.ScenarioId = existing?.Scenario.ScenarioId ?? Guid.NewGuid().ToString("D").ToLowerInvariant();
        document.CreatedUtc = existing?.Scenario.CreatedUtc ?? now;
        document.UpdatedUtc = now;
        response.ScenarioId = document.ScenarioId;
        response.UpdatedUtc = document.UpdatedUtc;
        response.FilePath = GetScenarioPath(document.ScenarioId);
        response.PlannedWrites.Add((isUpdate ? "replace " : "create ") + response.FilePath);

        if (!apply)
        {
            response.Ok = true;
            return response;
        }

        if (!confirmSave)
            return Fail(response, "scenario_save_confirmation_required", "Для записи сценария укажите confirm_save=true после проверки preview.");
        if (IsExactReplay(draft) && !confirmExactReplay)
            return Fail(response, "scenario_exact_replay_confirmation_required", "Для точного сценария требуется confirm_exact_replay=true.");

        lock (ProcessLock)
        {
            using var storeLease = AcquireStoreMutex();
            EnsureSafeRoot(create: true);
            if (!isUpdate && CountScenarioFiles() >= MaxScenarioFiles)
                return Fail(response, "scenario_store_full", "Достигнут лимит в 500 пользовательских сценариев.");

            duplicate = FindExactNameCollision(draft.Name, isUpdate ? existing.Scenario.ScenarioId : string.Empty);
            if (IsExactReplay(draft) && duplicate != null)
                return Fail(response, "scenario_name_conflict", "Точный сценарий с таким именем уже существует. Используйте уникальное имя.");

            if (isUpdate)
            {
                var current = ReadById(document.ScenarioId, out var errorCode, out var errorMessage);
                if (current == null)
                    return Fail(response, errorCode, errorMessage);
                if (!string.Equals(current.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
                    return Fail(response, "scenario_conflict", "Сценарий изменился перед записью. Изменения не применены.");
            }

            var json = JsonSerializer.Serialize(document, StoreJsonOptions) + Environment.NewLine;
            var byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > MaxScenarioFileBytes)
                return Fail(response, "scenario_too_large", "Размер сценария превышает 256 КиБ.");

            AtomicWrite(response.FilePath, json);
            response.Sha256 = ComputeSha256(Encoding.UTF8.GetBytes(json));
        }

        response.Ok = true;
        response.Applied = true;
        return response;
    }

    public ScenarioMutationResponse Delete(
        string scenarioId,
        string expectedSha256,
        bool apply,
        bool confirmDelete)
    {
        var response = new ScenarioMutationResponse { ScenarioId = scenarioId ?? string.Empty };
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
            return Fail(response, errorCode, errorMessage);

        response.ScenarioName = record.Scenario.Name;
        response.ExecutionMode = record.Scenario.ExecutionMode;
        response.Sha256 = record.Sha256;
        response.FilePath = record.FilePath;
        response.UpdatedUtc = record.Scenario.UpdatedUtc;
        response.PlannedWrites.Add("delete " + record.FilePath);
        if (!apply)
        {
            response.Ok = true;
            return response;
        }

        if (!confirmDelete)
            return Fail(response, "scenario_delete_confirmation_required", "Для удаления сценария укажите confirm_delete=true после проверки preview.");
        if (string.IsNullOrWhiteSpace(expectedSha256) ||
            !string.Equals(record.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
        {
            return Fail(response, "scenario_conflict", "Сценарий изменился после чтения. Удаление отменено.");
        }

        lock (ProcessLock)
        {
            using var storeLease = AcquireStoreMutex();
            var current = ReadById(scenarioId, out errorCode, out errorMessage);
            if (current == null)
                return Fail(response, errorCode, errorMessage);
            if (!string.Equals(current.Sha256, NormalizeSha(expectedSha256), StringComparison.OrdinalIgnoreCase))
                return Fail(response, "scenario_conflict", "Сценарий изменился перед удалением. Удаление отменено.");

            File.Delete(current.FilePath);
        }

        response.Ok = true;
        response.Applied = true;
        return response;
    }

    public ScenarioResolveResponse Resolve(
        string scenarioId,
        IReadOnlyDictionary<string, JsonElement> parameterValues,
        string executionIntent,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel)
    {
        var record = ReadById(scenarioId, out var errorCode, out var errorMessage);
        if (record == null)
        {
            return new ScenarioResolveResponse
            {
                Ok = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
        }

        var scenario = record.Scenario;
        var exact = IsExactReplay(scenario);
        var exactRequested = string.Equals(executionIntent, "exact_replay", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(executionIntent, "preview", StringComparison.OrdinalIgnoreCase) && !exactRequested)
            return ResolveFail(record, "scenario_execution_intent_invalid", "execution_intent должен быть preview или exact_replay.");
        if (exactRequested && !exact)
            return ResolveFail(record, "scenario_not_exact_replay", "Этот сценарий сохранён как шаблон, а не как точное повторение.");
        if (exact && parameterValues != null && parameterValues.Count > 0)
            return ResolveFail(record, "scenario_exact_override_forbidden", "Параметры точного сценария нельзя переопределять при запуске.");

        var contextMatch = MatchContext(scenario.Context, navisworksVersion, rootFileNames, projectLabel);
        if (exactRequested && !string.Equals(contextMatch.Grade, "strong", StringComparison.Ordinal))
        {
            var failed = ResolveFail(record, "scenario_context_mismatch", "Контекст точного сценария не подтверждён. Выполнение должно быть остановлено без уточняющих вопросов.");
            failed.ContextMatch = contextMatch.Grade;
            failed.ContextReasons = contextMatch.Reasons;
            return failed;
        }

        var valueErrors = new List<string>();
        var values = exact
            ? scenario.ExactReplay.FixedParameters
            : MergeTemplateValues(scenario, parameterValues, out valueErrors);
        if (!exact && valueErrors.Count > 0)
        {
            var failed = ResolveFail(record, "scenario_parameters_required", "Не заданы или неверно заданы параметры сценария.");
            failed.Warnings.AddRange(valueErrors);
            return failed;
        }

        var response = new ScenarioResolveResponse
        {
            Ok = true,
            ScenarioId = scenario.ScenarioId,
            ScenarioName = scenario.Name,
            ExecutionMode = scenario.ExecutionMode,
            ScenarioSha256 = record.Sha256,
            ContextMatch = contextMatch.Grade,
            ContextReasons = contextMatch.Reasons,
            SafetyEnvelope = exact ? scenario.ExactReplay.SafetyEnvelope : null,
            AgentInstruction = exactRequested
                ? "Exact replay was explicitly requested. Execute each returned preview call in order, substitute allowlisted $stepResult projections from actual prior responses, enforce foreach.maxIterations, verify the plan stays inside safety_envelope, then execute the same tool with apply=true and only already-approved scale gates. Do not ask follow-up questions. Stop and report on the first mismatch or failure."
                : exact
                    ? "Preview only. Show the saved exact-replay plan and safety envelope. Do not execute or apply any step unless the user directly requests execution_intent=exact_replay."
                    : "Show this resolved plan and all writes to the user. Execute steps in order, substituting each $stepResult from the allowlisted projection of the actual prior response and enforcing foreach.maxIterations before its body. Execute preview calls first. Apply mutating steps only after the user's current confirmation.",
        };

        foreach (var step in scenario.Steps)
        {
            if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
            {
                response.Steps.Add(new ScenarioResolvedStep
                {
                    StepId = step.StepId,
                    Control = step.Control,
                    Over = step.Over?.Clone(),
                    As = step.As,
                    MaxIterations = step.MaxIterations,
                    Body = ResolveControlBody(step.Body, values, exactRequested),
                });
                response.PlannedWrites.Add(
                    step.StepId + ": bounded foreach, maximum " +
                    step.MaxIterations.GetValueOrDefault().ToString() + " iteration(s)");
                foreach (var bodyStep in step.Body ?? new List<ScenarioStepDefinition>())
                {
                    var bodyDescriptor = GetToolDescriptor(bodyStep?.Tool);
                    if (bodyDescriptor?.MutatesModel == true)
                        response.PlannedWrites.Add(step.StepId + "/" + bodyStep.StepId + ": Navisworks document changes via " + bodyStep.Tool);
                    if (bodyDescriptor?.WritesFiles == true)
                        response.PlannedWrites.Add(step.StepId + "/" + bodyStep.StepId + ": file output via " + bodyStep.Tool);
                }
                continue;
            }

            var descriptor = GetToolDescriptor(step.Tool);
            if (descriptor == null)
                return ResolveFail(record, "scenario_tool_unavailable", "Операция сценария больше не поддерживается: " + step.Tool);
            if (!descriptor.SupportedContractVersions.Contains(step.ScenarioContractVersion))
                return ResolveFail(record, "scenario_tool_contract_changed", "Контракт операции изменился: " + step.Tool);

            var resolvedArguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in step.Arguments)
            {
                if (TryGetParameterReference(argument.Value, out var parameterName))
                {
                    if (!values.TryGetValue(parameterName, out var value))
                        return ResolveFail(record, "scenario_parameter_missing", "Не задан параметр: " + parameterName);
                    resolvedArguments[argument.Key] = value.Clone();
                }
                else
                {
                    resolvedArguments[argument.Key] = argument.Value.Clone();
                }
            }

            if (descriptor.HasApply)
                resolvedArguments[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            if (exact && scenario.ExactReplay?.SafetyEnvelope?.StepLimits != null &&
                scenario.ExactReplay.SafetyEnvelope.StepLimits.TryGetValue(step.StepId, out var runtimeLimit) &&
                runtimeLimit != null && runtimeLimit.MaxMatchedItems > 0)
            {
                ClampIntegerArgument(
                    resolvedArguments,
                    descriptor,
                    "maxMatchedItems",
                    runtimeLimit.MaxMatchedItems);
                ClampIntegerArgument(
                    resolvedArguments,
                    descriptor,
                    "maxSelectedItems",
                    runtimeLimit.MaxMatchedItems);
            }

            var applyOverrides = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (descriptor.HasApply && exactRequested)
            {
                applyOverrides[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(true);
                foreach (var argumentName in descriptor.AuthorizationArguments)
                    applyOverrides[argumentName] = JsonSerializer.SerializeToElement(true);

                var approvedScaleGates = scenario.ExactReplay?.SafetyEnvelope?.StepLimits != null &&
                                         scenario.ExactReplay.SafetyEnvelope.StepLimits.TryGetValue(step.StepId, out var stepLimit)
                    ? stepLimit.ApprovedScaleGates
                    : null;
                foreach (var argumentName in descriptor.ScaleAuthorizationArguments)
                {
                    if (approvedScaleGates?.Contains(argumentName, StringComparer.OrdinalIgnoreCase) == true)
                        applyOverrides[argumentName] = JsonSerializer.SerializeToElement(true);
                }
            }

            var planSha = ComputePlanSha(step.Tool, resolvedArguments);
            response.Steps.Add(new ScenarioResolvedStep
            {
                StepId = step.StepId,
                Tool = step.Tool,
                ScenarioContractVersion = step.ScenarioContractVersion,
                PreviewArguments = resolvedArguments,
                ApplyArgumentOverrides = applyOverrides,
                PlanSha256 = planSha,
                MutatesModel = descriptor.MutatesModel,
                WritesFiles = descriptor.WritesFiles,
                ChangesSelection = descriptor.ChangesSelection,
                ReviewedWrites = step.ReviewedWrites.ToList(),
            });

            if (descriptor.MutatesModel)
                response.PlannedWrites.Add(step.StepId + ": Navisworks document changes via " + step.Tool);
            if (descriptor.WritesFiles)
                response.PlannedWrites.Add(step.StepId + ": file output via " + step.Tool);
            if (descriptor.ChangesSelection)
                response.PlannedWrites.Add(step.StepId + ": current Navisworks selection changes via " + step.Tool);
            foreach (var reviewedWrite in step.ReviewedWrites)
                response.PlannedWrites.Add(step.StepId + ": reviewed write behavior '" + reviewedWrite + "' will be previewed and requires the normal apply gate");
        }

        return response;
    }

    private static void ClampIntegerArgument(
        IDictionary<string, JsonElement> arguments,
        ToolDescriptor descriptor,
        string argumentName,
        int maximum)
    {
        if (descriptor == null || maximum < 1 ||
            descriptor.Method.GetParameters().All(parameter =>
                !string.Equals(parameter.Name, argumentName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var value = maximum;
        if (arguments.TryGetValue(argumentName, out var existing) &&
            existing.ValueKind == JsonValueKind.Number &&
            existing.TryGetInt32(out var existingValue) &&
            existingValue > 0)
        {
            value = Math.Min(existingValue, maximum);
        }
        arguments[argumentName] = JsonSerializer.SerializeToElement(value);
    }

    internal ScenarioValidationResult ValidateDraft(ScenarioDraft draft, bool verifyExactFingerprint = true)
    {
        var result = new ScenarioValidationResult();
        if (draft == null)
        {
            result.Errors.Add("scenario is required");
            return result;
        }

        try
        {
            NormalizeDraftCollections(draft);
        }
        catch (ArgumentException)
        {
            result.Errors.Add("dictionary keys must be unique ignoring case");
            return result;
        }

        if (draft.SchemaVersion != 1 && draft.SchemaVersion != CurrentSchemaVersion)
            result.Errors.Add("schemaVersion must be 1 or 2");
        if (!string.Equals(draft.ExecutionMode, "template", StringComparison.Ordinal) &&
            !string.Equals(draft.ExecutionMode, "exactReplay", StringComparison.Ordinal))
            result.Errors.Add("executionMode must be template or exactReplay");
        ValidateDisplayText(draft.Name, "name", 1, 120, result);
        ValidateDisplayText(draft.Description, "description", 0, 1000, result);
        ValidateContext(draft.Context, result);

        draft.Parameters ??= new List<ScenarioParameterDefinition>();
        draft.Steps ??= new List<ScenarioStepDefinition>();
        if (draft.Parameters.Count > 32)
            result.Errors.Add("parameters cannot contain more than 32 items");
        if (draft.Steps.Count < 1 || draft.Steps.Count > 32)
            result.Errors.Add("steps must contain 1 to 32 items");

        var parameters = new Dictionary<string, ScenarioParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in draft.Parameters)
        {
            if (parameter == null || !NameRegex.IsMatch(parameter.Name ?? string.Empty))
            {
                result.Errors.Add("parameter name is invalid");
                continue;
            }
            if (!parameters.TryAdd(parameter.Name, parameter))
                result.Errors.Add("parameter names must be unique ignoring case: " + parameter.Name);
            if (!ParameterTypes.Contains(parameter.Type ?? string.Empty))
                result.Errors.Add("unsupported parameter type for " + parameter.Name);
            ValidateDisplayText(parameter.Title, "parameter title", 0, 200, result);
            ValidateDisplayText(parameter.Description, "parameter description", 0, 500, result);
            if (parameter.Default.HasValue)
            {
                if (parameter.Type is "filePath" or "directoryPath")
                    result.Errors.Add("path parameter defaults are forbidden: " + parameter.Name);
                else if (!ValueMatchesParameterType(parameter.Default.Value, parameter.Type))
                    result.Errors.Add("default value type does not match parameter " + parameter.Name);
            }
            parameter.Enum ??= new List<JsonElement>();
            if (parameter.Enum.Count > 0 || string.Equals(parameter.Type, "enum", StringComparison.Ordinal))
            {
                if (draft.SchemaVersion < 2)
                    result.Errors.Add("parameter enum is supported only in schema version 2: " + parameter.Name);
                if (parameter.Enum.Count < 1 || parameter.Enum.Count > 100)
                    result.Errors.Add("parameter enum must contain 1 to 100 values: " + parameter.Name);
                if (parameter.Enum.Any(value => value.ValueKind != JsonValueKind.String))
                    result.Errors.Add("parameter enum values must be strings: " + parameter.Name);
                if (parameter.Default.HasValue && !ParameterValueMeetsConstraints(parameter, parameter.Default.Value, out _))
                    result.Errors.Add("default value is outside enum/pattern constraints: " + parameter.Name);
            }
            if (!string.IsNullOrWhiteSpace(parameter.Pattern))
            {
                if (draft.SchemaVersion < 2)
                    result.Errors.Add("parameter pattern is supported only in schema version 2: " + parameter.Name);
                if (parameter.Type is not ("string" or "enum"))
                    result.Errors.Add("parameter pattern is valid only for string or enum: " + parameter.Name);
                if (parameter.Pattern.Length > 500)
                    result.Errors.Add("parameter pattern cannot exceed 500 characters: " + parameter.Name);
                else
                {
                    try { _ = new Regex(parameter.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
                    catch (ArgumentException) { result.Errors.Add("parameter pattern is invalid: " + parameter.Name); }
                }
            }
        }

        var exact = IsExactReplay(draft);
        if (exact)
            ValidateExactReplay(draft, parameters, result);
        else if (draft.ExactReplay != null)
            result.Errors.Add("exactReplay object is allowed only in exactReplay mode");

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var priorSteps = new Dictionary<string, ToolDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in draft.Steps)
        {
            ValidateStep(step, draft.SchemaVersion, exact, parameters, referenced, stepIds, priorSteps, result, allowForeach: true);
            if (step != null && !string.IsNullOrWhiteSpace(step.StepId) && GetToolDescriptor(step.Tool) is { } stepDescriptor)
                priorSteps[step.StepId] = stepDescriptor;
        }

        if (exact && draft.ExactReplay?.SafetyEnvelope != null)
        {
            var limits = draft.ExactReplay.SafetyEnvelope.StepLimits ?? new Dictionary<string, ScenarioStepSafetyLimit>();
            foreach (var step in draft.Steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.StepId))
                    continue;
                if (!limits.ContainsKey(step.StepId))
                    result.Errors.Add("exactReplay safety limit is missing for step: " + step.StepId);
            }
            foreach (var limitName in limits.Keys)
            {
                if (!stepIds.Contains(limitName))
                    result.Errors.Add("exactReplay safety limit references an unknown step: " + limitName);
            }
            foreach (var step in draft.Steps.Where(item => item != null && !string.IsNullOrWhiteSpace(item.StepId)))
            {
                if (!limits.TryGetValue(step.StepId, out var limit) || limit == null)
                    continue;
                var descriptor = GetToolDescriptor(step.Tool);
                if (descriptor == null)
                    continue;
                foreach (var gate in limit.ApprovedScaleGates)
                {
                    if (!descriptor.ScaleAuthorizationArguments.Contains(gate, StringComparer.OrdinalIgnoreCase))
                        result.Errors.Add("unknown scale authorization gate for " + step.StepId + ": " + gate);
                }
            }

            if (verifyExactFingerprint && result.Errors.Count == 0)
            {
                var expectedFingerprint = "sha256:" + ComputeExactReplayFingerprint(draft);
                if (!string.Equals(
                        expectedFingerprint,
                        draft.ExactReplay.SafetyEnvelope.PreviewFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("exactReplay safety previewFingerprint does not match the canonical resolved plan");
                }
            }
        }

        foreach (var parameter in parameters.Values.Where(item => item.Required))
        {
            if (!referenced.Contains(parameter.Name))
                result.Errors.Add("required parameter is not referenced by any step: " + parameter.Name);
        }

        return result;
    }

    private void ValidateExactReplay(
        ScenarioDraft draft,
        IReadOnlyDictionary<string, ScenarioParameterDefinition> parameters,
        ScenarioValidationResult result)
    {
        if (draft.ExactReplay == null)
        {
            result.Errors.Add("exactReplay object is required");
            return;
        }

        if (!string.Equals(draft.ExactReplay.ContextPolicy, "strict", StringComparison.Ordinal))
            result.Errors.Add("exactReplay.contextPolicy must be strict");
        if (!string.Equals(draft.ExactReplay.WritePolicy, "repeatReviewedWrites", StringComparison.Ordinal))
            result.Errors.Add("exactReplay.writePolicy must be repeatReviewedWrites");
        if (draft.Context == null ||
            ((draft.Context.NavisworksVersions?.Count ?? 0) == 0 &&
             (draft.Context.RootFilePatterns?.Count ?? 0) == 0 &&
             string.IsNullOrWhiteSpace(draft.Context.ProjectLabel)))
        {
            result.Errors.Add("exactReplay requires at least one strict context hint");
        }

        draft.ExactReplay.FixedParameters ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters.Values)
        {
            if (!draft.ExactReplay.FixedParameters.TryGetValue(parameter.Name, out var fixedValue))
            {
                result.Errors.Add("exactReplay fixed value is missing: " + parameter.Name);
                continue;
            }
            if (!ValueMatchesParameterType(fixedValue, parameter.Type))
                result.Errors.Add("exactReplay fixed value type is invalid: " + parameter.Name);
            if (parameter.Type is "filePath" or "directoryPath")
                ValidateFixedPath(fixedValue, parameter.Name, result);
        }

        foreach (var fixedName in draft.ExactReplay.FixedParameters.Keys)
        {
            if (!parameters.ContainsKey(fixedName))
                result.Errors.Add("exactReplay has an undeclared fixed parameter: " + fixedName);
        }

        if (draft.ExactReplay.SafetyEnvelope == null)
        {
            result.Errors.Add("exactReplay.safetyEnvelope is required");
        }
        else
        {
            var envelope = draft.ExactReplay.SafetyEnvelope;
            if (!Regex.IsMatch(envelope.PreviewFingerprint ?? string.Empty, "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                result.Errors.Add("exactReplay safety previewFingerprint must be sha256:<64 hex characters>");
            envelope.StepLimits ??= new Dictionary<string, ScenarioStepSafetyLimit>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in envelope.StepLimits)
            {
                if (item.Value == null || item.Value.MaxMatchedItems < 0 || item.Value.MaxModelWrites < 0 || item.Value.MaxFileWrites < 0)
                    result.Errors.Add("exactReplay safety limits must be non-negative: " + item.Key);
                if (item.Value != null)
                {
                    item.Value.ApprovedScaleGates ??= new List<string>();
                    foreach (var gate in item.Value.ApprovedScaleGates)
                        ValidateDisplayText(gate, "approved scale gate", 1, 100, result);
                }
            }
        }
    }

    private void ValidateStep(
        ScenarioStepDefinition step,
        int schemaVersion,
        bool exact,
        IReadOnlyDictionary<string, ScenarioParameterDefinition> parameters,
        ISet<string> referenced,
        ISet<string> stepIds,
        IReadOnlyDictionary<string, ToolDescriptor> priorSteps,
        ScenarioValidationResult result,
        bool allowForeach)
    {
        if (step == null)
        {
            result.Errors.Add("step cannot be null");
            return;
        }
        if (!NameRegex.IsMatch(step.StepId ?? string.Empty))
            result.Errors.Add("stepId has invalid format (expected ^[A-Za-z][A-Za-z0-9_]{0,63}$): " + step.StepId);
        else if (!stepIds.Add(step.StepId))
            result.Errors.Add("stepId is duplicated ignoring case: " + step.StepId);

        if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
        {
            if (schemaVersion < 2)
                result.Errors.Add("foreach is supported only in schema version 2: " + step.StepId);
            if (!allowForeach)
                result.Errors.Add("nested foreach is forbidden: " + step.StepId);
            if (!step.Over.HasValue || !TryGetStepResultReference(step.Over.Value, out var overReference))
                result.Errors.Add("foreach over must be a $stepResult reference: " + step.StepId);
            else
                ValidateStepResultReference(overReference, priorSteps, null, result);
            if (!NameRegex.IsMatch(step.As ?? string.Empty))
                result.Errors.Add("foreach as is invalid: " + step.StepId);
            if (!step.MaxIterations.HasValue || step.MaxIterations.Value < 1 || step.MaxIterations.Value > 1000)
                result.Errors.Add("foreach maxIterations must be between 1 and 1000: " + step.StepId);
            if (step.Body == null || step.Body.Count < 1 || step.Body.Count > 16)
                result.Errors.Add("foreach body must contain 1 to 16 steps: " + step.StepId);
            else
            {
                foreach (var bodyStep in step.Body)
                    ValidateStep(bodyStep, schemaVersion, exact, parameters, referenced, stepIds, priorSteps, result, allowForeach: false);
            }
            if (!string.IsNullOrWhiteSpace(step.Tool) || (step.Arguments?.Count ?? 0) > 0)
                result.Errors.Add("foreach control step cannot also define tool/arguments: " + step.StepId);
            return;
        }
        if (!string.IsNullOrWhiteSpace(step.Control))
        {
            result.Errors.Add("unsupported control value: " + step.Control);
            return;
        }

        var descriptor = GetToolDescriptor(step.Tool);
        if (descriptor == null)
        {
            result.Errors.Add(
                "tool is not scenario-allowlisted: " + step.Tool +
                "; allowed: [" +
                string.Join(", ", ToolDescriptors.Keys.OrderBy(name => name, StringComparer.Ordinal)) +
                "]");
            return;
        }
        if (!descriptor.SupportedContractVersions.Contains(step.ScenarioContractVersion))
            result.Errors.Add("scenarioContractVersion is stale for " + step.Tool);

        step.Arguments ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var methodParameters = descriptor.Method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var argument in step.Arguments)
        {
            if (IsAuthorizationArgument(argument.Key))
            {
                result.Errors.Add("authorization argument cannot be stored: " + argument.Key);
                continue;
            }
            if (RuntimeIdentityArguments.Contains(argument.Key) || IsCredentialName(argument.Key))
            {
                result.Errors.Add("runtime identity or credential argument cannot be stored: " + argument.Key);
                continue;
            }
            if (!methodParameters.TryGetValue(argument.Key, out var methodParameter))
            {
                result.Errors.Add("unknown tool argument " + argument.Key + " for " + step.Tool);
                continue;
            }
            if (ReviewedWriteBehaviorArguments.Contains(argument.Key) && !exact &&
                !(schemaVersion >= 2 && step.ReviewedWrites.Contains(argument.Key, StringComparer.OrdinalIgnoreCase)))
            {
                result.Errors.Add("reviewed write behavior requires reviewedWrites declaration in schema version 2 template: " + argument.Key);
                continue;
            }

            if (TryGetParameterReference(argument.Value, out var parameterName))
            {
                if (!parameters.TryGetValue(parameterName, out var definition) ||
                    !string.Equals(definition.Name, parameterName, StringComparison.Ordinal))
                {
                    result.Errors.Add("parameter reference is undeclared or has wrong case: " + parameterName);
                    continue;
                }
                if (!ParameterTypeMatchesToolType(definition.Type, methodParameter.ParameterType))
                    result.Errors.Add("parameter type does not match tool argument " + argument.Key);
                referenced.Add(definition.Name);
                continue;
            }

            if (TryGetStepResultReference(argument.Value, out var stepResultReference))
            {
                if (schemaVersion < 2)
                    result.Errors.Add("$stepResult is supported only in schema version 2: " + argument.Key);
                else
                    ValidateStepResultReference(stepResultReference, priorSteps, methodParameter.ParameterType, result);
                continue;
            }
            if (TryGetLoopReference(argument.Value, out _))
            {
                if (allowForeach)
                    result.Errors.Add("$loop is allowed only inside foreach body: " + argument.Key);
                continue;
            }

            if (ContainsNestedParameterReference(argument.Value))
            {
                result.Errors.Add("nested parameter references are not supported in schema version 1: " + argument.Key);
                continue;
            }
            if (ContainsForbiddenPropertyName(argument.Value, out var forbiddenProperty))
            {
                result.Errors.Add("runtime identity or credential property cannot be stored: " + forbiddenProperty);
                continue;
            }
            if (IsFileSystemPathArgument(argument.Key) && argument.Value.ValueKind == JsonValueKind.String)
            {
                result.Errors.Add("path arguments must reference a declared path parameter: " + argument.Key);
                continue;
            }
            if (!CanDeserialize(argument.Value, methodParameter.ParameterType))
                result.Errors.Add("literal value does not match tool argument type: " + argument.Key);
            ValidateJsonStrings(argument.Value, "argument " + argument.Key, result);
        }

        foreach (var requiredArgument in descriptor.RequiredArguments)
        {
            if (!step.Arguments.Keys.Contains(requiredArgument, StringComparer.OrdinalIgnoreCase))
                result.Errors.Add("required tool argument is missing: " + requiredArgument);
        }

        step.ReviewedWrites ??= new List<string>();
        foreach (var reviewedWrite in step.ReviewedWrites)
        {
            if (!ReviewedWriteBehaviorArguments.Contains(reviewedWrite))
                result.Errors.Add("unknown reviewedWrites entry for " + step.StepId + ": " + reviewedWrite);
            else if (!methodParameters.ContainsKey(reviewedWrite))
                result.Errors.Add("reviewedWrites entry is not an argument of " + step.Tool + ": " + reviewedWrite);
            else if (!step.Arguments.ContainsKey(reviewedWrite))
                result.Errors.Add("reviewedWrites entry has no stored argument in " + step.Tool + ": " + reviewedWrite);
        }
    }

    private List<ScenarioStoredRecord> ReadAll(List<string> warnings)
    {
        var result = new List<ScenarioStoredRecord>();
        if (!Directory.Exists(_rootPath))
            return result;

        try
        {
            EnsureSafeRoot(create: false);
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParse(fileName, out _))
                    continue;
                try
                {
                    result.Add(ReadRecord(path));
                }
                catch (Exception ex)
                {
                    warnings.Add(Path.GetFileName(path) + ": " + SafeMessage(ex));
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(SafeMessage(ex));
        }

        return result;
    }

    private ScenarioStoredRecord ReadById(string scenarioId, out string errorCode, out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;
        if (!Guid.TryParse(scenarioId, out var id))
        {
            errorCode = "scenario_id_invalid";
            errorMessage = "Некорректный идентификатор сценария.";
            return null;
        }

        var path = GetScenarioPath(id.ToString("D").ToLowerInvariant());
        if (!File.Exists(path))
        {
            errorCode = "scenario_not_found";
            errorMessage = "Сценарий не найден.";
            return null;
        }

        try
        {
            EnsureSafeRoot(create: false);
            return ReadRecord(path);
        }
        catch (Exception ex)
        {
            errorCode = "scenario_invalid";
            errorMessage = "Файл сценария повреждён или небезопасен: " + SafeMessage(ex);
            return null;
        }
    }

    private ScenarioStoredRecord ReadRecord(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("reparse-point files are forbidden");
        if (info.Length > MaxScenarioFileBytes)
            throw new InvalidDataException("scenario file exceeds 256 KiB");

        var bytes = File.ReadAllBytes(path);
        var scenario = JsonSerializer.Deserialize<ScenarioDocument>(bytes, StoreJsonOptions)
            ?? throw new InvalidDataException("scenario document is empty");
        NormalizeDocumentCollections(scenario);
        var fileId = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(fileId, scenario.ScenarioId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("filename does not match scenarioId");
        var validation = ValidateDraft(scenario);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join("; ", validation.Errors.Take(5)));

        return new ScenarioStoredRecord
        {
            Scenario = scenario,
            Sha256 = ComputeSha256(bytes),
            FilePath = path,
            Warnings = validation.Warnings,
        };
    }

    private ScenarioStoredRecord FindExactNameCollision(string name, string excludedId)
    {
        return ReadAll(new List<string>()).FirstOrDefault(record =>
            IsExactReplay(record.Scenario) &&
            !string.Equals(record.Scenario.ScenarioId, excludedId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Scenario.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, JsonElement> MergeTemplateValues(
        ScenarioDocument scenario,
        IReadOnlyDictionary<string, JsonElement> provided,
        out List<string> errors)
    {
        errors = new List<string>();
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        provided ??= new Dictionary<string, JsonElement>();

        foreach (var parameter in scenario.Parameters)
        {
            if (provided.TryGetValue(parameter.Name, out var value))
            {
                if (!ValueMatchesParameterType(value, parameter.Type))
                    errors.Add("Неверный тип параметра: " + parameter.Name);
                else if (!ParameterValueMeetsConstraints(parameter, value, out var constraintError))
                    errors.Add("Параметр " + parameter.Name + ": " + constraintError);
                else
                {
                    values[parameter.Name] = value.Clone();
                    if (parameter.Type is "filePath" or "directoryPath")
                    {
                        var pathValidation = new ScenarioValidationResult();
                        ValidateFixedPath(value, parameter.Name, pathValidation);
                        errors.AddRange(pathValidation.Errors.Select(error => "Небезопасный путь параметра " + parameter.Name + ": " + error));
                    }
                }
            }
            else if (parameter.Default.HasValue)
            {
                values[parameter.Name] = parameter.Default.Value.Clone();
            }
            else if (parameter.Required)
            {
                errors.Add("Требуется параметр: " + parameter.Name);
            }
        }

        foreach (var name in provided.Keys)
        {
            if (!scenario.Parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
                errors.Add("Неизвестный параметр: " + name);
        }

        return values;
    }

    private static ScenarioDocument CopyDraft(ScenarioDraft draft)
    {
        var json = JsonSerializer.Serialize(draft, StoreJsonOptions);
        var document = JsonSerializer.Deserialize<ScenarioDocument>(json, StoreJsonOptions)
            ?? throw new InvalidOperationException("Could not clone scenario draft.");
        NormalizeDocumentCollections(document);
        return document;
    }

    private static ScenarioResolveResponse ResolveFail(ScenarioStoredRecord record, string code, string message)
    {
        return new ScenarioResolveResponse
        {
            Ok = false,
            ErrorCode = code,
            ErrorMessage = message,
            ScenarioId = record.Scenario.ScenarioId,
            ScenarioName = record.Scenario.Name,
            ExecutionMode = record.Scenario.ExecutionMode,
            ScenarioSha256 = record.Sha256,
        };
    }

    private static void NormalizeDocumentCollections(ScenarioDocument scenario)
    {
        NormalizeDraftCollections(scenario);
    }

    private static void NormalizeDraftCollections(ScenarioDraft scenario)
    {
        scenario.Parameters ??= new List<ScenarioParameterDefinition>();
        scenario.Steps ??= new List<ScenarioStepDefinition>();
        foreach (var step in scenario.Steps)
        {
            if (step == null)
                continue;
            step.Arguments = new Dictionary<string, JsonElement>(
                step.Arguments ?? new Dictionary<string, JsonElement>(),
                StringComparer.OrdinalIgnoreCase);
            step.ReviewedWrites ??= new List<string>();
            step.Body ??= new List<ScenarioStepDefinition>();
            foreach (var bodyStep in step.Body)
            {
                if (bodyStep == null)
                    continue;
                bodyStep.Arguments = new Dictionary<string, JsonElement>(
                    bodyStep.Arguments ?? new Dictionary<string, JsonElement>(),
                    StringComparer.OrdinalIgnoreCase);
                bodyStep.ReviewedWrites ??= new List<string>();
                bodyStep.Body ??= new List<ScenarioStepDefinition>();
            }
        }

        if (scenario.ExactReplay == null)
            return;
        scenario.ExactReplay.FixedParameters = new Dictionary<string, JsonElement>(
            scenario.ExactReplay.FixedParameters ?? new Dictionary<string, JsonElement>(),
            StringComparer.OrdinalIgnoreCase);
        if (scenario.ExactReplay.SafetyEnvelope == null)
            return;
        scenario.ExactReplay.SafetyEnvelope.StepLimits = new Dictionary<string, ScenarioStepSafetyLimit>(
            scenario.ExactReplay.SafetyEnvelope.StepLimits ?? new Dictionary<string, ScenarioStepSafetyLimit>(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static ScenarioMutationResponse Fail(ScenarioMutationResponse response, string code, string message)
    {
        response.Ok = false;
        response.ErrorCode = code;
        response.ErrorMessage = message;
        return response;
    }

    private static ToolDescriptor GetToolDescriptor(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;
        return ToolDescriptors.TryGetValue(toolName, out var descriptor) ? descriptor : null;
    }

    private static IReadOnlyDictionary<string, ToolDescriptor> CreateToolDescriptors()
    {
        var descriptors = new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            ["mcp_health_check"] = CreateToolDescriptor(nameof(NavisworksTools.McpHealthCheck), 1, false, false, Array.Empty<string>()),
            ["active_model_context"] = CreateToolDescriptor(nameof(NavisworksTools.ActiveModelContext), 1, false, false, Array.Empty<string>()),
            ["list_root_items"] = CreateToolDescriptor(nameof(NavisworksTools.ListRootItems), 1, false, false, Array.Empty<string>()),
            ["find_root_items_by_name"] = CreateToolDescriptor(nameof(NavisworksTools.FindRootItemsByName), 1, false, false, Array.Empty<string>()),
            ["find_items"] = CreateToolDescriptor(nameof(NavisworksTools.FindItems), 2, false, false, Array.Empty<string>()),
            ["find_items_by_bbox"] = CreateToolDescriptor(nameof(NavisworksTools.FindItemsByBbox), 1, false, false, Array.Empty<string>()),
            ["selection_status"] = CreateToolDescriptor(nameof(NavisworksTools.SelectionStatus), 1, false, false, Array.Empty<string>()),
            ["selected_items_preview"] = CreateToolDescriptor(nameof(NavisworksTools.SelectedItemsPreview), 1, false, false, Array.Empty<string>()),
            ["item_properties_by_handle"] = CreateToolDescriptor(nameof(NavisworksTools.ItemPropertiesByHandle), 1, false, false, Array.Empty<string>()),
            ["current_viewpoint_info"] = CreateToolDescriptor(nameof(NavisworksTools.CurrentViewpointInfo), 1, false, false, Array.Empty<string>()),
            ["list_selection_sets"] = CreateToolDescriptor(nameof(NavisworksTools.ListSelectionSets), 1, false, false, Array.Empty<string>()),
            ["selection_property_report"] = CreateToolDescriptor(typeof(NavisworksSelectionReportTools), nameof(NavisworksSelectionReportTools.SelectionPropertyReport), 1, false, false, Array.Empty<string>()),
            ["selection_distinct_property_values"] = CreateToolDescriptor(typeof(NavisworksSelectionReportTools), nameof(NavisworksSelectionReportTools.SelectionDistinctPropertyValues), 1, false, false, Array.Empty<string>()),
            ["model_color_scheme"] = CreateToolDescriptor(typeof(NavisworksModelColorSchemeTools), nameof(NavisworksModelColorSchemeTools.ModelColorScheme), 1, true, false, Array.Empty<string>()),
            ["select_selection_set"] = CreateToolDescriptor(nameof(NavisworksTools.SelectSelectionSet), 1, false, false, new[] { "pathOrName" }),
            ["isolate_selected"] = CreateToolDescriptor(nameof(NavisworksTools.IsolateSelected), 1, true, false, Array.Empty<string>()),
            ["isolate_by_box"] = CreateToolDescriptor(typeof(NavisworksSectionBoxTools), nameof(NavisworksSectionBoxTools.IsolateByBox), 1, true, false, new[] { "box" }),
            ["zoom_to_selection"] = CreateToolDescriptor(nameof(NavisworksTools.ZoomToSelection), 1, false, false, Array.Empty<string>()),
            ["clash_list_tests"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashListTests), 1, false, false, Array.Empty<string>()),
            ["clash_list_results"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashListResults), 1, false, false, Array.Empty<string>()),
            ["clash_list_clusters"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashListClusters), 1, false, false, Array.Empty<string>()),
            ["clash_bbox_pair_plan"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashBboxPairPlan), 3, false, true, Array.Empty<string>()),
            ["clash_pair_tests_create"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashPairTestsCreate), 2, true, false, Array.Empty<string>()),
            ["clash_manage_tests"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashManageTests), 2, true, false, new[] { "operation" }),
            ["clash_group_results"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashGroupResults), 1, true, false, Array.Empty<string>()),
            ["clash_root_matrix"] = CreateToolDescriptor(typeof(NavisworksClashRootMatrixTools), nameof(NavisworksClashRootMatrixTools.ClashRootMatrix), 1, false, false, Array.Empty<string>()),
            ["clash_group_by_proximity"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashGroupByProximity), 1, true, false, Array.Empty<string>()),
            ["clash_ignore_rules"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashIgnoreRules), 1, true, false, Array.Empty<string>()),
            ["clash_export_points"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashExportPoints), 1, false, true, Array.Empty<string>()),
            ["clash_renumber_results"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashRenumberResults), 1, true, false, Array.Empty<string>()),
            ["clash_create_matrix_from_selection"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashCreateMatrixFromSelection), 2, true, false, Array.Empty<string>()),
            ["clash_tests_from_sets"] = CreateToolDescriptor(typeof(NavisworksClashSetTools), nameof(NavisworksClashSetTools.ClashTestsFromSets), 1, true, false, Array.Empty<string>()),
            ["clash_tests_export"] = CreateToolDescriptor(typeof(NavisworksClashTransferTools), nameof(NavisworksClashTransferTools.ClashTestsExport), 1, false, true, Array.Empty<string>()),
            ["clash_batchtest_import"] = CreateToolDescriptor(typeof(NavisworksClashTransferTools), nameof(NavisworksClashTransferTools.ClashBatchtestImport), 1, true, false, new[] { "inputPath" }),
            ["clash_run_batch"] = CreateToolDescriptor(typeof(NavisworksClashSetTools), nameof(NavisworksClashSetTools.ClashRunBatch), 1, true, false, Array.Empty<string>()),
            ["select_by_search"] = CreateToolDescriptor(typeof(NavisworksScenarioWorkflowTools), nameof(NavisworksScenarioWorkflowTools.SelectBySearch), 1, false, false, new[] { "conditions" }),
            ["clash_report_status"] = CreateToolDescriptor(typeof(NavisworksClashTools), nameof(NavisworksClashTools.ClashReportStatus), 1, false, false, Array.Empty<string>()),
            ["selection_export_properties"] = CreateToolDescriptor(
                typeof(NavisworksSelectionReportTools),
                nameof(NavisworksSelectionReportTools.SelectionExportProperties),
                contractVersion: 1,
                mutatesModel: false,
                writesFiles: true,
                requiredArguments: new[] { "outputPath" }),
            ["selection_sets_build_viewpoints"] = CreateToolDescriptor(
                nameof(NavisworksTools.SelectionSetsBuildViewpoints),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: new[] { "folderPrefix", "steps" }),
            ["clash_generate_report"] = CreateToolDescriptor(
                typeof(NavisworksClashTools),
                nameof(NavisworksClashTools.ClashGenerateReport),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: true,
                requiredArguments: Array.Empty<string>()),
            ["clash_save_viewpoints"] = CreateToolDescriptor(
                typeof(NavisworksClashTools),
                nameof(NavisworksClashTools.ClashSaveViewpoints),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: Array.Empty<string>()),
            ["clash_isolate_result"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.ClashIsolateResult),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: true,
                requiredArguments: new[] { "resultHandle" }),
            ["clash_reset_isolation"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.ClashResetIsolation),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: Array.Empty<string>()),
            ["capture_current_view"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.CaptureCurrentView),
                contractVersion: 1,
                mutatesModel: false,
                writesFiles: true,
                requiredArguments: new[] { "outputPath" }),
        };
        descriptors["select_by_search"].ChangesSelection = true;
        descriptors["find_items"].SupportedContractVersions.Add(1);
        descriptors["find_items"].OutputProjections.UnionWith(new[] { "matchedItemCount", "depthHistogram", "sampleValuesFromModel", "results" });
        descriptors["clash_bbox_pair_plan"].SupportedContractVersions.Add(2);
        descriptors["clash_create_matrix_from_selection"].SupportedContractVersions.Add(1);
        descriptors["select_by_search"].OutputProjections.UnionWith(new[] { "matchedItemCount", "selectedItemCount", "preview" });
        descriptors["clash_bbox_pair_plan"].OutputProjections.UnionWith(new[] { "pairs", "candidatePairCount", "outputPath" });
        descriptors["clash_pair_tests_create"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount" });
        descriptors["clash_create_matrix_from_selection"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount", "plannedPairCount" });
        descriptors["clash_tests_from_sets"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount", "runOperationId" });
        descriptors["clash_tests_export"].OutputProjections.UnionWith(new[] { "plan", "outputWritten", "artifactStatus", "bytesWritten", "sha256" });
        descriptors["clash_batchtest_import"].OutputProjections.UnionWith(new[] { "plan", "tests", "createdTestCount", "rolledBackTestCount" });
        descriptors["clash_run_batch"].OutputProjections.UnionWith(new[] { "operationId", "state", "processedTestCount" });
        descriptors["clash_list_tests"].OutputProjections.UnionWith(new[] { "tests", "returnedTestCount" });
        foreach (var descriptor in descriptors)
        {
            var responseType = descriptor.Value.Method.ReturnType;
            if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Task<>))
                responseType = responseType.GetGenericArguments()[0];
            foreach (var projection in descriptor.Value.OutputProjections)
            {
                var root = projection.Split(new[] { '.', '[' }, 2)[0];
                if (responseType.GetProperty(root, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) == null)
                {
                    throw new InvalidOperationException(
                        "Scenario output projection does not exist on the tool response: " +
                        descriptor.Key + "." + projection);
                }
            }
        }
        return descriptors;
    }

    public ScenarioCapabilitiesResponse GetCapabilities()
    {
        var response = new ScenarioCapabilitiesResponse
        {
            ParameterTypes = ParameterTypes.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            PairNameTokens = new List<string> { "{index}", "{aName}", "{bName}", "{aCode}", "{bCode}" },
            PairNameTransforms = new List<string> { "zeroPad:N", "strip:#regex#", "replace:#regex#replacement#", "upper", "lower" },
            ForeachRules = "schemaVersion 2 only; over must reference an allowlisted prior $stepResult projection; maxIterations is required (1..1000); maximum nesting depth is 1; body has 1..16 steps.",
        };
        response.AllowedTools = ToolDescriptors
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ScenarioToolCapability
            {
                Tool = item.Key,
                ScenarioContractVersion = item.Value.ContractVersion,
                HasApply = item.Value.HasApply,
                MutatesModel = item.Value.MutatesModel,
                WritesFiles = item.Value.WritesFiles,
                ChangesSelection = item.Value.ChangesSelection,
                ReviewedWrites = item.Value.ReviewedWrites.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                OutputProjections = item.Value.OutputProjections.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            })
            .ToList();
        response.Example = new ScenarioDraft
        {
            SchemaVersion = 2,
            Name = "Матрица коллизий по прямым потомкам",
            Description = "Select discipline groups below one building, rebuild and run a cleanly named clash matrix, then remove zero-result tests.",
            Parameters = new List<ScenarioParameterDefinition>
            {
                new()
                {
                    Name = "namePrefix",
                    Type = "string",
                    Required = false,
                    Default = JsonSerializer.SerializeToElement("100000-XXX1-YY"),
                    Pattern = "^[0-9A-Za-zА-Яа-я()\\- ]+$",
                },
            },
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "select_marks",
                    Tool = "select_by_search",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["conditions"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item", ["property"] = "Name", ["operator"] = "wildcard",
                                ["value"] = "/100000-XXX1-YY-01*", ["ignoreCase"] = true,
                            },
                        }),
                        ["scope"] = JsonSerializer.SerializeToElement("direct_children_of"),
                        ["parentConditions"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item", ["property"] = "Name", ["operator"] = "equals",
                                ["value"] = "/100000-XXX1-YY-01",
                            },
                        }),
                        ["replaceSelection"] = JsonSerializer.SerializeToElement(true),
                        ["maxMatchedItems"] = JsonSerializer.SerializeToElement(100),
                    },
                },
                new()
                {
                    StepId = "build_and_run",
                    Tool = "clash_create_matrix_from_selection",
                    ScenarioContractVersion = 2,
                    ReviewedWrites = new List<string> { "removePreviousGenerated", "runAfterCreate" },
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["namePrefix"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$parameter"] = "namePrefix" }),
                        ["testType"] = JsonSerializer.SerializeToElement("hard"),
                        ["runAfterCreate"] = JsonSerializer.SerializeToElement(true),
                        ["removePreviousGenerated"] = JsonSerializer.SerializeToElement(true),
                        ["pairNameTemplate"] = JsonSerializer.SerializeToElement("{index|zeroPad:3} 100000-XXX1-YY {aName|strip:#^/100000-XXX1-YY-01[-_]#}-{bName|strip:#^/100000-XXX1-YY-01[-_]#}"),
                        ["pairNameStartIndex"] = JsonSerializer.SerializeToElement(1),
                        ["includePairNames"] = JsonSerializer.SerializeToElement(false),
                    },
                },
                new()
                {
                    StepId = "delete_zero",
                    Tool = "clash_manage_tests",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["operation"] = JsonSerializer.SerializeToElement("delete"),
                        ["namePrefix"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$parameter"] = "namePrefix" }),
                        ["onlyWithTotal"] = JsonSerializer.SerializeToElement(0),
                    },
                },
            },
        };
        return response;
    }

    private static ToolDescriptor CreateToolDescriptor(
        string methodName,
        int contractVersion,
        bool mutatesModel,
        bool writesFiles,
        IEnumerable<string> requiredArguments)
    {
        return CreateToolDescriptor(
            typeof(NavisworksTools),
            methodName,
            contractVersion,
            mutatesModel,
            writesFiles,
            requiredArguments);
    }

    private static ToolDescriptor CreateToolDescriptor(
        Type toolType,
        string methodName,
        int contractVersion,
        bool mutatesModel,
        bool writesFiles,
        IEnumerable<string> requiredArguments)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Scenario allowlist method was not found: " +
                toolType.Name +
                "." +
                methodName);
        var apply = method.GetParameters().FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "apply", StringComparison.OrdinalIgnoreCase));
        var required = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType != typeof(CancellationToken) &&
                !parameter.IsOptional &&
                !parameter.HasDefaultValue &&
                !IsAuthorizationArgument(parameter.Name) &&
                !RuntimeIdentityArguments.Contains(parameter.Name))
            .Select(parameter => parameter.Name)
            .Concat(requiredArguments ?? Array.Empty<string>());
        var authorizationArguments = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType == typeof(bool) &&
                parameter.Name.StartsWith("confirm", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Name.StartsWith("confirmLarge", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scaleAuthorizationArguments = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType == typeof(bool) &&
                parameter.Name.StartsWith("confirmLarge", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedWrites = method.GetParameters()
            .Where(parameter => ReviewedWriteBehaviorArguments.Contains(parameter.Name))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ToolDescriptor
        {
            Method = method,
            ContractVersion = contractVersion,
            SupportedContractVersions = new HashSet<int> { contractVersion },
            HasApply = apply != null,
            ApplyParameterName = apply?.Name ?? "apply",
            MutatesModel = mutatesModel,
            WritesFiles = writesFiles,
            RequiredArguments = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase),
            AuthorizationArguments = authorizationArguments,
            ScaleAuthorizationArguments = scaleAuthorizationArguments,
            ReviewedWrites = reviewedWrites,
        };
    }

    private static bool IsAuthorizationArgument(string name)
    {
        return string.Equals(name, "apply", StringComparison.OrdinalIgnoreCase) ||
               (name?.StartsWith("confirm", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsCredentialName(string name)
    {
        var normalized = new string((name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return CredentialNameTerms.Any(normalized.Contains);
    }

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

    private static ContextMatch MatchContext(
        ScenarioContext context,
        string navisworksVersion,
        IReadOnlyCollection<string> rootFileNames,
        string projectLabel)
    {
        if (context == null ||
            ((context.NavisworksVersions?.Count ?? 0) == 0 &&
             (context.RootFilePatterns?.Count ?? 0) == 0 &&
             string.IsNullOrWhiteSpace(context.ProjectLabel)))
        {
            return new ContextMatch("weak", new List<string> { "Сценарий не содержит контекстных подсказок." });
        }

        var reasons = new List<string>();
        var conflicts = false;
        var matches = 0;
        var expected = 0;

        if ((context.NavisworksVersions?.Count ?? 0) > 0)
        {
            expected++;
            if (string.IsNullOrWhiteSpace(navisworksVersion))
                reasons.Add("Версия Navisworks не указана.");
            else if (context.NavisworksVersions.Contains(navisworksVersion, StringComparer.OrdinalIgnoreCase))
            {
                matches++;
                reasons.Add("Версия Navisworks совпадает.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Версия Navisworks не совпадает.");
            }
        }

        if ((context.RootFilePatterns?.Count ?? 0) > 0)
        {
            expected++;
            if (rootFileNames == null || rootFileNames.Count == 0)
                reasons.Add("Корневые файлы модели не указаны.");
            else if (context.RootFilePatterns.All(pattern => rootFileNames.Any(file => WildcardMatch(file, pattern))))
            {
                matches++;
                reasons.Add("Корневые файлы модели совпадают.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Корневые файлы модели не совпадают.");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.ProjectLabel))
        {
            expected++;
            if (string.IsNullOrWhiteSpace(projectLabel))
                reasons.Add("Метка проекта не указана.");
            else if (string.Equals(context.ProjectLabel.Trim(), projectLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches++;
                reasons.Add("Метка проекта совпадает.");
            }
            else
            {
                conflicts = true;
                reasons.Add("Метка проекта не совпадает.");
            }
        }

        if (conflicts)
            return new ContextMatch("mismatch", reasons);
        if (expected > 0 && matches == expected)
            return new ContextMatch("strong", reasons);
        if (matches > 0)
            return new ContextMatch("partial", reasons);
        return new ContextMatch("weak", reasons);
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        try
        {
            var regex = "^" + Regex.Escape(pattern ?? string.Empty).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private void EnsureSafeRoot(bool create)
    {
        if (create)
            Directory.CreateDirectory(_rootPath);
        if (!Directory.Exists(_rootPath))
            return;

        var info = new DirectoryInfo(_rootPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Scenario directory cannot be a reparse point.");
    }

    private static IDisposable AcquireStoreMutex()
    {
        var acquired = false;
        try
        {
            try
            {
                acquired = CrossProcessStoreMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new IOException("Scenario store is busy. Try again.");
            return new StoreMutexLease();
        }
        catch
        {
            if (acquired)
                CrossProcessStoreMutex.ReleaseMutex();
            throw;
        }
    }

    private int CountScenarioFiles()
    {
        return Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly)
            .Count(path => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _));
    }

    private string GetScenarioPath(string scenarioId)
    {
        var fileName = Guid.Parse(scenarioId).ToString("D").ToLowerInvariant() + ".json";
        var path = Path.GetFullPath(Path.Combine(_rootPath, fileName));
        var rootWithSeparator = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Scenario path escapes the scenario directory.");
        return path;
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string ComputePlanSha(string tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var root = new JsonObject { ["tool"] = tool };
        root["arguments"] = BuildCanonicalArguments(arguments);
        return ComputeSha256(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static string ComputeExactReplayFingerprint(ScenarioDraft scenario)
    {
        var steps = new JsonArray();
        foreach (var step in scenario.Steps)
        {
            if (string.Equals(step.Control, "foreach", StringComparison.Ordinal))
            {
                steps.Add(JsonSerializer.SerializeToNode(step, StoreJsonOptions));
                continue;
            }
            var descriptor = GetToolDescriptor(step.Tool)
                ?? throw new InvalidOperationException("Scenario tool is unavailable: " + step.Tool);
            var resolved = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in step.Arguments)
            {
                if (TryGetParameterReference(argument.Value, out var parameterName))
                    resolved[argument.Key] = scenario.ExactReplay.FixedParameters[parameterName].Clone();
                else
                    resolved[argument.Key] = argument.Value.Clone();
            }
            if (descriptor.HasApply)
                resolved[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            steps.Add(new JsonObject
            {
                ["stepId"] = step.StepId,
                ["tool"] = step.Tool,
                ["scenarioContractVersion"] = step.ScenarioContractVersion,
                ["arguments"] = BuildCanonicalArguments(resolved),
            });
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = scenario.SchemaVersion,
            ["executionMode"] = scenario.ExecutionMode,
            ["context"] = JsonSerializer.SerializeToNode(scenario.Context, StoreJsonOptions),
            ["parameters"] = BuildCanonicalScenarioParameters(scenario),
            ["fixedParameters"] = BuildCanonicalArguments(scenario.ExactReplay.FixedParameters),
            ["contextPolicy"] = scenario.ExactReplay.ContextPolicy,
            ["writePolicy"] = scenario.ExactReplay.WritePolicy,
            ["stepLimits"] = BuildCanonicalSafetyLimits(scenario.ExactReplay.SafetyEnvelope.StepLimits),
            ["steps"] = steps,
        };
        return ComputeSha256(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static JsonNode BuildCanonicalScenarioParameters(ScenarioDraft scenario)
    {
        if (scenario.SchemaVersion != 1)
            return JsonSerializer.SerializeToNode(scenario.Parameters, StoreJsonOptions);

        var parameters = new JsonArray();
        foreach (var parameter in scenario.Parameters ?? new List<ScenarioParameterDefinition>())
        {
            var item = new JsonObject
            {
                ["name"] = parameter.Name,
                ["type"] = parameter.Type,
                ["title"] = parameter.Title,
                ["description"] = parameter.Description,
                ["required"] = parameter.Required,
            };
            if (parameter.Default.HasValue)
                item["default"] = JsonNode.Parse(parameter.Default.Value.GetRawText());
            parameters.Add(item);
        }
        return parameters;
    }

    private static List<ScenarioResolvedStep> ResolveControlBody(
        IEnumerable<ScenarioStepDefinition> body,
        IReadOnlyDictionary<string, JsonElement> values,
        bool exactRequested)
    {
        var result = new List<ScenarioResolvedStep>();
        foreach (var source in body ?? Enumerable.Empty<ScenarioStepDefinition>())
        {
            if (source == null)
                continue;
            var descriptor = GetToolDescriptor(source.Tool)
                ?? throw new InvalidOperationException("Scenario foreach body tool is unavailable: " + source.Tool);
            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in source.Arguments ?? new Dictionary<string, JsonElement>())
            {
                if (TryGetParameterReference(argument.Value, out var parameterName) &&
                    values.TryGetValue(parameterName, out var value))
                {
                    arguments[argument.Key] = value.Clone();
                }
                else
                    arguments[argument.Key] = argument.Value.Clone();
            }
            if (descriptor.HasApply)
                arguments[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(false);

            var applyOverrides = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (descriptor.HasApply && exactRequested)
            {
                applyOverrides[descriptor.ApplyParameterName] = JsonSerializer.SerializeToElement(true);
                foreach (var authorizationArgument in descriptor.AuthorizationArguments)
                    applyOverrides[authorizationArgument] = JsonSerializer.SerializeToElement(true);
            }

            result.Add(new ScenarioResolvedStep
            {
                StepId = source.StepId,
                Tool = source.Tool,
                ScenarioContractVersion = source.ScenarioContractVersion,
                PreviewArguments = arguments,
                ApplyArgumentOverrides = applyOverrides,
                PlanSha256 = ComputePlanSha(source.Tool, arguments),
                MutatesModel = descriptor.MutatesModel,
                WritesFiles = descriptor.WritesFiles,
                ChangesSelection = descriptor.ChangesSelection,
                ReviewedWrites = source.ReviewedWrites.ToList(),
            });
        }
        return result;
    }

    private static JsonObject BuildCanonicalArguments(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var result = new JsonObject();
        foreach (var argument in arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
            result[argument.Key] = JsonNode.Parse(argument.Value.GetRawText());
        return result;
    }

    private static JsonObject BuildCanonicalSafetyLimits(
        IReadOnlyDictionary<string, ScenarioStepSafetyLimit> limits)
    {
        var result = new JsonObject();
        foreach (var item in limits.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            result[item.Key] = new JsonObject
            {
                ["maxMatchedItems"] = item.Value.MaxMatchedItems,
                ["maxModelWrites"] = item.Value.MaxModelWrites,
                ["maxFileWrites"] = item.Value.MaxFileWrites,
                ["approvedScaleGates"] = new JsonArray(
                    item.Value.ApprovedScaleGates
                        .OrderBy(gate => gate, StringComparer.Ordinal)
                        .Select(gate => (JsonNode)JsonValue.Create(gate))
                        .ToArray()),
            };
        }
        return result;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string NormalizeSha(string value)
    {
        return (value ?? string.Empty).Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeMessage(Exception exception)
    {
        return exception is JsonException ? "invalid JSON schema" : exception.Message;
    }

    private static int MatchRank(string grade)
    {
        return grade switch
        {
            "strong" => 0,
            "partial" => 1,
            "weak" => 2,
            _ => 3,
        };
    }

    private static bool IsExactReplay(ScenarioDraft scenario)
    {
        return string.Equals(scenario?.ExecutionMode, "exactReplay", StringComparison.Ordinal);
    }

    private static string GetDefaultRootPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("NAVISHELPER_SCENARIO_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisHelper", "Scenarios");
    }

    private sealed class ToolDescriptor
    {
        public MethodInfo Method { get; set; }
        public int ContractVersion { get; set; }
        public HashSet<int> SupportedContractVersions { get; set; } = new HashSet<int>();
        public bool HasApply { get; set; }
        public string ApplyParameterName { get; set; }
        public bool MutatesModel { get; set; }
        public bool WritesFiles { get; set; }
        public bool ChangesSelection { get; set; }
        public IReadOnlySet<string> RequiredArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> AuthorizationArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ScaleAuthorizationArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ReviewedWrites { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OutputProjections { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class StoreMutexLease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CrossProcessStoreMutex.ReleaseMutex();
        }
    }

    private sealed record ContextMatch(string Grade, List<string> Reasons);
}
