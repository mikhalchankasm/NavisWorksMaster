using System.Text.Json;
using System.Text.Json.Nodes;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ScenarioLibraryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ScenarioLibraryService _service;

    public ScenarioLibraryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "NavisHelper-ScenarioTests-" + Guid.NewGuid().ToString("N"));
        _service = new ScenarioLibraryService(_root);
    }

    [Fact]
    public void Save_PreviewDoesNotCreateScenarioDirectory()
    {
        var response = _service.Save(CreateTemplateDraft(), "", "", false, false, false);

        Assert.True(response.Ok);
        Assert.False(response.Applied);
        Assert.False(Directory.Exists(_root));
        Assert.Single(response.PlannedWrites);
    }

    [Fact]
    public void Save_ApplyRequiresConfirmation()
    {
        var response = _service.Save(CreateTemplateDraft(), "", "", true, false, false);

        Assert.False(response.Ok);
        Assert.Equal("scenario_save_confirmation_required", response.ErrorCode);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void SaveGetList_RoundTripsScenarioAndHash()
    {
        var saved = Save(CreateTemplateDraft());

        var fetched = _service.Get(saved.ScenarioId);
        var listed = _service.List("выгрузка", "2027", Array.Empty<string>(), "", 3);

        Assert.True(saved.Ok);
        Assert.True(saved.Applied);
        Assert.True(File.Exists(saved.FilePath));
        Assert.True(fetched.Ok);
        Assert.Equal(saved.Sha256, fetched.Sha256);
        Assert.Equal("Выгрузка свойств", fetched.Scenario.Name);
        Assert.Single(listed.Scenarios);
        Assert.Equal("strong", listed.Scenarios[0].MatchGrade);
    }

    [Fact]
    public void Save_UpdateUsesShaOptimisticConcurrency()
    {
        var saved = Save(CreateTemplateDraft());
        var update = CreateTemplateDraft();
        update.Description = "Обновлённое описание";

        var conflict = _service.Save(update, saved.ScenarioId, "deadbeef", true, true, false);
        var applied = _service.Save(update, saved.ScenarioId, saved.Sha256, true, true, false);

        Assert.False(conflict.Ok);
        Assert.Equal("scenario_conflict", conflict.ErrorCode);
        Assert.True(applied.Ok);
        Assert.NotEqual(saved.Sha256, applied.Sha256);
    }

    [Fact]
    public void Delete_PreviewsThenDeletesExactFile()
    {
        var saved = Save(CreateTemplateDraft());

        var preview = _service.Delete(saved.ScenarioId, saved.Sha256, false, false);
        var deleted = _service.Delete(saved.ScenarioId, saved.Sha256, true, true);

        Assert.True(preview.Ok);
        Assert.False(preview.Applied);
        Assert.True(deleted.Ok);
        Assert.True(deleted.Applied);
        Assert.False(File.Exists(saved.FilePath));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void Validate_RejectsStoredAuthorizationAndRuntimeIdentity()
    {
        var draft = CreateTemplateDraft();
        draft.Steps[0].Arguments["apply"] = Element(true);
        draft.Steps[0].Arguments["instanceId"] = Element("host-id");

        var validation = _service.ValidateDraft(draft);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("authorization argument"));
        Assert.Contains(validation.Errors, error => error.Contains("runtime identity"));
    }

    [Fact]
    public void Validate_RejectsUnknownToolAndUnknownArgument()
    {
        var unknownTool = CreateTemplateDraft();
        unknownTool.Steps[0].Tool = "save_document";
        var unknownArgument = CreateTemplateDraft();
        unknownArgument.Steps[0].Arguments["madeUp"] = Element(1);

        Assert.Contains(_service.ValidateDraft(unknownTool).Errors, error => error.Contains("not scenario-allowlisted"));
        Assert.Contains(_service.ValidateDraft(unknownArgument).Errors, error => error.Contains("unknown tool argument"));
    }

    [Fact]
    public void Resolve_TemplateSubstitutesTypedParameterAndForcesDryRun()
    {
        var saved = Save(CreateTemplateDraft());
        var parameters = new Dictionary<string, JsonElement>
        {
            ["outputPath"] = Element(@"D:\Reports\selection.csv"),
        };

        var resolved = _service.Resolve(saved.ScenarioId, parameters, "preview", "2027", null, "");

        Assert.True(resolved.Ok);
        Assert.Single(resolved.Steps);
        Assert.Equal(@"D:\Reports\selection.csv", resolved.Steps[0].PreviewArguments["outputPath"].GetString());
        Assert.False(resolved.Steps[0].PreviewArguments["apply"].GetBoolean());
        Assert.Empty(resolved.Steps[0].ApplyArgumentOverrides);
        Assert.Contains(resolved.PlannedWrites, write => write.Contains("file output"));
    }

    [Fact]
    public void Resolve_TemplateReportsMissingRequiredParameter()
    {
        var saved = Save(CreateTemplateDraft());

        var resolved = _service.Resolve(saved.ScenarioId, null, "preview", "2027", null, "");

        Assert.False(resolved.Ok);
        Assert.Equal("scenario_parameters_required", resolved.ErrorCode);
    }

    [Fact]
    public void Save_ExactReplayRequiresDedicatedConfirmationAndUniqueName()
    {
        var draft = CreateExactDraft();
        draft.Name = "  Точная выгрузка свойств  ";
        var needsConfirmation = _service.Save(draft, "", "", true, true, false);
        var first = _service.Save(draft, "", "", true, true, true);
        var duplicate = _service.Save(CreateExactDraft(), "", "", true, true, true);

        Assert.Equal("scenario_exact_replay_confirmation_required", needsConfirmation.ErrorCode);
        Assert.True(first.Ok);
        Assert.Equal("Точная выгрузка свойств", _service.Get(first.ScenarioId).Scenario.Name);
        Assert.False(duplicate.Ok);
        Assert.Equal("scenario_name_conflict", duplicate.ErrorCode);
    }

    [Fact]
    public void Resolve_ExactReplayRequiresStrongContextAndRejectsOverrides()
    {
        var saved = Save(CreateExactDraft(), exact: true);

        var weak = _service.Resolve(saved.ScenarioId, null, "exact_replay", "", null, "");
        var overridden = _service.Resolve(
            saved.ScenarioId,
            new Dictionary<string, JsonElement> { ["outputPath"] = Element(@"D:\Other.csv") },
            "exact_replay",
            "2027",
            null,
            "");
        var resolved = _service.Resolve(saved.ScenarioId, null, "exact_replay", "2027", null, "");

        Assert.Equal("scenario_context_mismatch", weak.ErrorCode);
        Assert.Equal("scenario_exact_override_forbidden", overridden.ErrorCode);
        Assert.True(resolved.Ok);
        Assert.NotNull(resolved.SafetyEnvelope);
        Assert.Contains("Do not ask follow-up questions", resolved.AgentInstruction);
    }

    [Fact]
    public void Read_V1ExactReplayWithoutV2FieldsKeepsOriginalFingerprintValid()
    {
        var saved = Save(CreateExactDraft(), exact: true);
        var root = JsonNode.Parse(File.ReadAllText(saved.FilePath))!.AsObject();
        foreach (var parameter in root["parameters"]!.AsArray().OfType<JsonObject>())
        {
            parameter.Remove("enum");
            parameter.Remove("pattern");
        }
        foreach (var step in root["steps"]!.AsArray().OfType<JsonObject>())
        {
            step.Remove("reviewedWrites");
            step.Remove("control");
            step.Remove("over");
            step.Remove("as");
            step.Remove("maxIterations");
            step.Remove("body");
        }
        File.WriteAllText(saved.FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var fetched = _service.Get(saved.ScenarioId);

        Assert.True(fetched.Ok, fetched.ErrorMessage);
        Assert.Equal(1, fetched.Scenario.SchemaVersion);
    }

    [Fact]
    public void Resolve_ExactPreviewDoesNotAuthorizeApply()
    {
        var saved = Save(CreateExactDraft(), exact: true);

        var preview = _service.Resolve(saved.ScenarioId, null, "preview", "", null, "");

        Assert.True(preview.Ok);
        Assert.Empty(preview.Steps[0].ApplyArgumentOverrides);
        Assert.Contains("Do not execute", preview.AgentInstruction);
    }

    [Fact]
    public void Validate_ExactReplayRequiresReviewedAbsolutePathAndSafetyEnvelope()
    {
        var draft = CreateExactDraft();
        draft.ExactReplay.FixedParameters["outputPath"] = Element("relative.csv");
        draft.ExactReplay.SafetyEnvelope = null;

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("path must be absolute"));
        Assert.Contains(validation.Errors, error => error.Contains("safetyEnvelope is required"));
    }

    [Fact]
    public void Get_RejectsUnknownPersistedFields()
    {
        var saved = Save(CreateTemplateDraft());
        var node = JsonNode.Parse(File.ReadAllText(saved.FilePath)).AsObject();
        node["unexpected"] = true;
        File.WriteAllText(saved.FilePath, node.ToJsonString());

        var fetched = _service.Get(saved.ScenarioId);

        Assert.False(fetched.Ok);
        Assert.Equal("scenario_invalid", fetched.ErrorCode);
    }

    [Fact]
    public void Get_RejectsHandEditedExactReplayPlanFingerprintMismatch()
    {
        var saved = Save(CreateExactDraft(), exact: true);
        var node = JsonNode.Parse(File.ReadAllText(saved.FilePath)).AsObject();
        node["steps"][0]["arguments"]["format"] = "xlsx";
        File.WriteAllText(saved.FilePath, node.ToJsonString());

        var fetched = _service.Get(saved.ScenarioId);

        Assert.False(fetched.Ok);
        Assert.Equal("scenario_invalid", fetched.ErrorCode);
        Assert.Contains("previewFingerprint", fetched.ErrorMessage);
    }

    [Fact]
    public void Get_RejectsHandEditedExactReplaySafetyEnvelope()
    {
        var saved = Save(CreateExactDraft(), exact: true);
        var node = JsonNode.Parse(File.ReadAllText(saved.FilePath)).AsObject();
        node["exactReplay"]["safetyEnvelope"]["stepLimits"]["exportSelection"]["maxFileWrites"] = 999;
        File.WriteAllText(saved.FilePath, node.ToJsonString());

        var fetched = _service.Get(saved.ScenarioId);

        Assert.False(fetched.Ok);
        Assert.Equal("scenario_invalid", fetched.ErrorCode);
        Assert.Contains("previewFingerprint", fetched.ErrorMessage);
    }

    [Fact]
    public void Save_NormalizesExactReplayFixedParameterComparerBeforeFingerprinting()
    {
        var draft = CreateExactDraft();
        var value = draft.ExactReplay.FixedParameters["outputPath"];
        draft.ExactReplay.FixedParameters = new Dictionary<string, JsonElement>
        {
            ["OUTPUTPATH"] = value,
        };

        var saved = Save(draft, exact: true);
        var resolved = _service.Resolve(saved.ScenarioId, null, "exact_replay", "2027", null, "");

        Assert.True(resolved.Ok);
        Assert.Equal(@"D:\Reports\selection.csv", resolved.Steps[0].PreviewArguments["outputPath"].GetString());
    }

    [Fact]
    public void Resolve_TemplateRejectsUnsafeRuntimePath()
    {
        var saved = Save(CreateTemplateDraft());
        var parameters = new Dictionary<string, JsonElement>
        {
            ["outputPath"] = Element("relative.csv"),
        };

        var resolved = _service.Resolve(saved.ScenarioId, parameters, "preview", "2027", null, "");

        Assert.False(resolved.Ok);
        Assert.Equal("scenario_parameters_required", resolved.ErrorCode);
        Assert.Contains(resolved.Warnings, warning => warning.Contains("Небезопасный путь"));
    }

    [Fact]
    public void Validate_RejectsStaleScenarioToolContractVersion()
    {
        var draft = CreateTemplateDraft();
        draft.Steps[0].ScenarioContractVersion = 2;

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("scenarioContractVersion is stale"));
    }

    [Fact]
    public void Validate_RejectsNestedParameterReferences()
    {
        var draft = CreateTemplateDraft();
        draft.Steps[0].Arguments["categoryFilters"] = Element(new object[]
        {
            new Dictionary<string, string> { ["$parameter"] = "outputPath" },
        });

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("nested parameter references"));
    }

    [Fact]
    public void Validate_AllInitialAllowlistedToolsBindToExplicitMethods()
    {
        var draft = new ScenarioDraft
        {
            SchemaVersion = 1,
            ExecutionMode = "template",
            Name = "Все разрешённые операции",
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "export",
                    Tool = "selection_export_properties",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["outputPath"] = ParameterReference("outputPath"),
                    },
                },
                new()
                {
                    StepId = "sets",
                    Tool = "selection_sets_build_viewpoints",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["folderPrefix"] = Element("MTR"),
                        ["steps"] = Element(new[] { new { step = "overview", label = "Обзор" } }),
                    },
                },
                new() { StepId = "report", Tool = "clash_generate_report" },
                new() { StepId = "viewpoints", Tool = "clash_save_viewpoints" },
            },
            Parameters = new List<ScenarioParameterDefinition>
            {
                new() { Name = "outputPath", Type = "filePath", Required = true },
            },
        };

        var validation = _service.ValidateDraft(draft);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void Capabilities_ExposeAllowlistSyntaxAndValidExample()
    {
        var capabilities = _service.GetCapabilities();

        Assert.Equal("{\"$parameter\":\"parameterName\"}", capabilities.ParameterReferenceSyntax);
        Assert.Equal("^[A-Za-z][A-Za-z0-9_]{0,63}$", capabilities.StepIdPattern);
        Assert.Contains(capabilities.AllowedTools, item => item.Tool == "clash_bbox_pair_plan");
        Assert.Contains(capabilities.AllowedTools, item => item.Tool == "clash_manage_tests" && item.HasApply);
        Assert.Contains(capabilities.AllowedTools, item => item.Tool == "select_selection_set");
        Assert.Contains(capabilities.AllowedTools, item =>
            item.Tool == "find_items" &&
            item.ScenarioContractVersion == 2 &&
            item.OutputProjections.Contains("depthHistogram") &&
            item.OutputProjections.Contains("sampleValuesFromModel"));
        Assert.True(_service.ValidateDraft(capabilities.Example).IsValid);
    }

    [Fact]
    public void Validate_AllowsRequestedClashWorkflowWithoutRuntimeHandles()
    {
        var draft = new ScenarioDraft
        {
            Name = "BBox clash workflow",
            Parameters = new List<ScenarioParameterDefinition>
            {
                new() { Name = "planOutputPath", Type = "filePath", Required = true },
            },
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "plan_pairs",
                    Tool = "clash_bbox_pair_plan",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["targetRootName"] = Element("Target model"),
                        ["outputPath"] = ParameterReference("planOutputPath"),
                    },
                },
                new()
                {
                    StepId = "create_tests",
                    Tool = "clash_pair_tests_create",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["planOutputPath"] = ParameterReference("planOutputPath"),
                        ["testNamePrefix"] = Element("NH-BBOX"),
                        ["toleranceMm"] = Element(10.0),
                        ["testType"] = Element("hard_conservative"),
                    },
                },
                new()
                {
                    StepId = "delete_empty",
                    Tool = "clash_manage_tests",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["operation"] = Element("delete"),
                        ["namePrefix"] = Element("NH-BBOX"),
                        ["onlyWithTotal"] = Element(0),
                    },
                },
                new()
                {
                    StepId = "rename_tests",
                    Tool = "clash_manage_tests",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["operation"] = Element("rename_batch"),
                        ["namePrefix"] = Element("NH-BBOX"),
                        ["namePattern"] = Element("{index}. {aName}-vs-{bName}"),
                        ["renameFromEnd"] = Element(true),
                    },
                },
            },
        };

        var validation = _service.ValidateDraft(draft);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void Validate_RejectsTopLevelAndNestedRuntimeHandles()
    {
        var topLevel = new ScenarioDraft
        {
            Name = "Unsafe handles",
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "group",
                    Tool = "clash_group_results",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["testHandles"] = Element(new[] { "clash-test:1" }),
                    },
                },
            },
        };
        var nested = new ScenarioDraft
        {
            Name = "Unsafe nested handles",
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "rename",
                    Tool = "clash_manage_tests",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["operation"] = Element("rename_batch"),
                        ["renames"] = Element(new[] { new { testHandle = "clash-test:1", newName = "Renamed" } }),
                    },
                },
            },
        };

        Assert.Contains(_service.ValidateDraft(topLevel).Errors, error => error.Contains("runtime identity"));
        Assert.Contains(_service.ValidateDraft(nested).Errors, error => error.Contains("runtime identity"));
    }

    [Fact]
    public void Validate_DerivesRequiredToolArgumentsAndAllowsNavisworksTreePaths()
    {
        var missingOutput = new ScenarioDraft
        {
            Name = "Missing output",
            Steps = new List<ScenarioStepDefinition>
            {
                new() { StepId = "export", Tool = "clash_export_points" },
            },
        };
        var navigation = new ScenarioDraft
        {
            Name = "Navigation",
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "select",
                    Tool = "select_selection_set",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["pathOrName"] = Element("Папка/Набор"),
                    },
                },
            },
        };

        Assert.Contains(_service.ValidateDraft(missingOutput).Errors, error => error.Contains("required tool argument is missing: outputPath"));
        Assert.True(_service.ValidateDraft(navigation).IsValid);
    }

    [Fact]
    public void Validate_NullStepsReturnErrorsInsteadOfThrowing()
    {
        var draft = CreateTemplateDraft();
        draft.Steps = new List<ScenarioStepDefinition> { null };

        var exception = Record.Exception(() => _service.ValidateDraft(draft));
        var validation = _service.ValidateDraft(draft);

        Assert.Null(exception);
        Assert.Contains("step cannot be null", validation.Errors);
    }

    [Fact]
    public void Resolve_ExactReplayIncludesApprovedConfirmationOverrides()
    {
        var draft = new ScenarioDraft
        {
            ExecutionMode = "exactReplay",
            Name = "Renumber clashes",
            Context = new ScenarioContext { NavisworksVersions = new List<string> { "2027" } },
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "renumber",
                    Tool = "clash_renumber_results",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["testName"] = Element("Project test"),
                    },
                },
                new()
                {
                    StepId = "group",
                    Tool = "clash_group_results",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["testName"] = Element("Project test"),
                    },
                },
            },
            ExactReplay = new ScenarioExactReplayDefinition
            {
                SafetyEnvelope = new ScenarioSafetyEnvelope
                {
                    PreviewFingerprint = "sha256:" + new string('a', 64),
                    StepLimits = new Dictionary<string, ScenarioStepSafetyLimit>
                    {
                        ["renumber"] = new(),
                        ["group"] = new()
                        {
                            ApprovedScaleGates = new List<string> { "confirmLargeGrouping" },
                        },
                    },
                },
            },
        };
        var saved = Save(draft, exact: true);

        var resolved = _service.Resolve(saved.ScenarioId, null, "exact_replay", "2027", null, "");

        Assert.True(resolved.Ok);
        Assert.True(resolved.Steps[0].ApplyArgumentOverrides["apply"].GetBoolean());
        Assert.True(resolved.Steps[0].ApplyArgumentOverrides["confirmRename"].GetBoolean());
        Assert.True(resolved.Steps[1].ApplyArgumentOverrides["confirmLargeGrouping"].GetBoolean());
    }

    [Fact]
    public void Validate_SeparatesInvalidAndDuplicateStepIdErrors()
    {
        var draft = CreateTemplateDraft();
        draft.Steps[0].StepId = "find-target";
        draft.Steps.Add(new ScenarioStepDefinition
        {
            StepId = "step1",
            Tool = "zoom_to_selection",
        });
        draft.Steps.Add(new ScenarioStepDefinition
        {
            StepId = "STEP1",
            Tool = "isolate_selected",
        });

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("invalid format") && error.Contains("find-target"));
        Assert.Contains(validation.Errors, error => error.Contains("duplicated") && error.Contains("STEP1"));
    }

    [Fact]
    public void Save_AllowlistFailureReturnsAllowedTools()
    {
        var draft = CreateTemplateDraft();
        draft.Steps[0].Tool = "not_a_tool";

        var response = _service.Save(draft, "", "", false, false, false);

        Assert.False(response.Ok);
        Assert.Contains("clash_bbox_pair_plan", response.AllowedTools);
        Assert.Contains(response.Errors, error => error.Contains("allowed: ["));
    }

    [Fact]
    public void Capabilities_DescribeSchemaV2WorkflowSurface()
    {
        var capabilities = _service.GetCapabilities();

        Assert.Equal(2, capabilities.CurrentSchemaVersion);
        Assert.Contains(1, capabilities.SupportedSchemaVersions);
        Assert.Contains(2, capabilities.SupportedSchemaVersions);
        Assert.Contains(capabilities.AllowedTools, tool =>
            tool.Tool == "select_by_search" && tool.ChangesSelection);
        Assert.Contains(capabilities.AllowedTools, tool =>
            tool.Tool == "clash_create_matrix_from_selection" &&
            tool.ScenarioContractVersion == 2 &&
            tool.ReviewedWrites.Contains("removePreviousGenerated"));
        Assert.Contains("{aCode}", capabilities.PairNameTokens);
        Assert.Equal(2, capabilities.Example.SchemaVersion);
        Assert.Contains(capabilities.Example.Steps, step => step.Tool == "select_by_search");
        Assert.True(_service.ValidateDraft(capabilities.Example).IsValid);
    }

    [Fact]
    public void Validate_V2AllowsReviewedWritesAndTypedParameterConstraints()
    {
        var draft = CreateMatrixScenarioV2();

        var validation = _service.ValidateDraft(draft);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void Resolve_V2EnforcesEnumAndPattern()
    {
        var draft = CreateMatrixScenarioV2();
        var saved = Save(draft);

        var invalid = _service.Resolve(
            saved.ScenarioId,
            new Dictionary<string, JsonElement> { ["namePrefix"] = Element("bad/slash") },
            "preview",
            "2027",
            null,
            "");
        var valid = _service.Resolve(
            saved.ScenarioId,
            new Dictionary<string, JsonElement> { ["namePrefix"] = Element("100000-XXX1-YY") },
            "preview",
            "2027",
            null,
            "");

        Assert.False(invalid.Ok);
        Assert.True(valid.Ok, valid.ErrorMessage);
        Assert.Contains(valid.PlannedWrites, write => write.Contains("removePreviousGenerated"));
    }

    [Fact]
    public void Validate_StepResultMustReferencePriorAllowlistedOutput()
    {
        var draft = CreateMatrixScenarioV2();
        draft.Steps.Insert(1, new ScenarioStepDefinition
        {
            StepId = "create_pairs",
            Tool = "clash_pair_tests_create",
            ScenarioContractVersion = 2,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["pairs"] = Element(new Dictionary<string, string> { ["$stepResult"] = "missing.pairs" }),
            },
        });

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("unknown or later stepId"));
    }

    [Fact]
    public void Validate_ForeachRequiresBoundAndRejectsNesting()
    {
        var draft = CreateMatrixScenarioV2();
        draft.Steps.Insert(1, new ScenarioStepDefinition
        {
            StepId = "loop",
            Control = "foreach",
            Over = Element(new Dictionary<string, string> { ["$stepResult"] = "select_marks.preview" }),
            As = "item",
            MaxIterations = 0,
            Body = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "nested",
                    Control = "foreach",
                    Over = Element(new Dictionary<string, string> { ["$stepResult"] = "select_marks.preview" }),
                    As = "nestedItem",
                    MaxIterations = 10,
                    Body = new List<ScenarioStepDefinition>(),
                },
            },
        });

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error => error.Contains("maxIterations"));
        Assert.Contains(validation.Errors, error => error.Contains("nested foreach"));
    }

    [Fact]
    public void Resolve_ForeachBodyForcesMutatingToolsToPreview()
    {
        var draft = CreateMatrixScenarioV2();
        draft.Steps.Insert(1, new ScenarioStepDefinition
        {
            StepId = "loop",
            Control = "foreach",
            Over = Element(new Dictionary<string, string> { ["$stepResult"] = "select_marks.preview" }),
            As = "item",
            MaxIterations = 100,
            Body = new List<ScenarioStepDefinition>
            {
                new() { StepId = "isolate_one", Tool = "isolate_selected" },
            },
        });
        var saved = Save(draft);

        var resolved = _service.Resolve(saved.ScenarioId, null, "preview", "2027", null, "");

        Assert.True(resolved.Ok, resolved.ErrorMessage);
        var loop = Assert.Single(resolved.Steps, step => step.Control == "foreach");
        var body = Assert.Single(loop.Body);
        Assert.False(body.PreviewArguments["apply"].GetBoolean());
        Assert.Empty(body.ApplyArgumentOverrides);
    }

    [Fact]
    public void SectionBoxReplay_IsAllowlistedAsMutatingApplyTool_ButCaptureIsNot()
    {
        var capabilities = _service.GetCapabilities();

        Assert.Contains(capabilities.AllowedTools, tool =>
            tool.Tool == "isolate_by_box" && tool.MutatesModel && tool.HasApply);
        Assert.DoesNotContain(capabilities.AllowedTools, tool => tool.Tool == "get_current_section_box");

        var captureDraft = CreateSectionBoxExactReplayDraft();
        captureDraft.Steps[0].Tool = "get_current_section_box";
        captureDraft.Steps[0].Arguments.Clear();
        Assert.Contains(
            _service.ValidateDraft(captureDraft).Errors,
            error => error.Contains("not scenario-allowlisted", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionBoxExactReplay_StoresLiteralGeometryAndResolvesWithoutCaptureOrRuntimeHandles()
    {
        var draft = CreateSectionBoxExactReplayDraft();
        var serialized = JsonSerializer.Serialize(draft);
        var saved = Save(draft, exact: true);
        var resolved = _service.Resolve(saved.ScenarioId, null, "exact_replay", "2027", null, "");

        Assert.DoesNotContain("get_current_section_box", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("$stepResult", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("matchHandle", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.True(resolved.Ok, resolved.ErrorMessage);
        var step = Assert.Single(resolved.Steps);
        Assert.Equal("isolate_by_box", step.Tool);
        Assert.Equal("document_global", step.PreviewArguments["box"].GetProperty("coordinateSpace").GetString());
        Assert.Equal(SectionBoxIsolationLimits.DefaultMaxScannedItems, step.PreviewArguments["maxScannedItems"].GetInt32());
        Assert.Equal(SectionBoxIsolationLimits.DefaultMaxDurationSeconds, step.PreviewArguments["maxDurationSeconds"].GetInt32());
        Assert.False(step.PreviewArguments["apply"].GetBoolean());
        Assert.True(step.ApplyArgumentOverrides["apply"].GetBoolean());
    }

    [Fact]
    public void SectionBoxExactReplay_RejectsStepResultInsteadOfLiteralGeometry()
    {
        var draft = CreateSectionBoxExactReplayDraft();
        draft.Steps[0].Arguments["box"] = Element(new Dictionary<string, string>
        {
            ["$stepResult"] = "scan.results[0].boundingBox",
        });

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error =>
            error.Contains("box must be literal geometry", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionBoxExactReplay_RejectsNestedRuntimeReferenceAndInvalidLiteral()
    {
        var nestedReferenceDraft = CreateSectionBoxExactReplayDraft();
        nestedReferenceDraft.Steps[0].Arguments["box"] = JsonDocument.Parse(
            "{\"formatVersion\":1,\"coordinateSpace\":\"document_global\",\"documentUnits\":\"meters\"," +
            "\"center\":{\"$stepResult\":\"scan.center\"},\"halfExtents\":{\"x\":1,\"y\":1,\"z\":1}," +
            "\"axes\":[{\"x\":1,\"y\":0,\"z\":0},{\"x\":0,\"y\":1,\"z\":0},{\"x\":0,\"y\":0,\"z\":1}]}"
        ).RootElement.Clone();
        var invalidLiteralDraft = CreateSectionBoxExactReplayDraft();
        invalidLiteralDraft.Steps[0].Arguments["box"] = Element(new Dictionary<string, string>());

        var nestedValidation = _service.ValidateDraft(nestedReferenceDraft);
        var invalidValidation = _service.ValidateDraft(invalidLiteralDraft);

        Assert.Contains(nestedValidation.Errors, error =>
            error.Contains("box must be literal geometry", StringComparison.Ordinal));
        Assert.Contains(invalidValidation.Errors, error =>
            error.Contains("valid literal canonical geometry", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionBoxExactReplay_RequiresLiteralInRangeTraversalLimit()
    {
        var missing = CreateSectionBoxExactReplayDraft();
        missing.Steps[0].Arguments.Remove("maxScannedItems");
        var referenced = CreateSectionBoxExactReplayDraft();
        referenced.Steps[0].Arguments["maxScannedItems"] = Element(new Dictionary<string, string>
        {
            ["$parameter"] = "limit",
        });
        var aboveHardMaximum = CreateSectionBoxExactReplayDraft();
        aboveHardMaximum.Steps[0].Arguments["maxScannedItems"] =
            Element(SectionBoxIsolationLimits.MaximumMaxScannedItems + 1);

        var missingValidation = _service.ValidateDraft(missing);
        var referencedValidation = _service.ValidateDraft(referenced);
        var maximumValidation = _service.ValidateDraft(aboveHardMaximum);

        Assert.Contains(missingValidation.Errors, error =>
            error.Contains("required tool argument is missing: maxScannedItems", StringComparison.Ordinal));
        Assert.Contains(referencedValidation.Errors, error =>
            error.Contains("maxScannedItems must be a literal integer", StringComparison.Ordinal));
        Assert.Contains(maximumValidation.Errors, error =>
            error.Contains("between 1 and 500000", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionBoxExactReplay_RequiresLiteralDurationAndMatchingSafetyEnvelope()
    {
        var missing = CreateSectionBoxExactReplayDraft();
        missing.Steps[0].Arguments.Remove("maxDurationSeconds");
        var referenced = CreateSectionBoxExactReplayDraft();
        referenced.Steps[0].Arguments["maxDurationSeconds"] = Element(new Dictionary<string, string>
        {
            ["$stepResult"] = "previous.duration",
        });
        var aboveHardMaximum = CreateSectionBoxExactReplayDraft();
        aboveHardMaximum.Steps[0].Arguments["maxDurationSeconds"] =
            Element(SectionBoxIsolationLimits.MaximumMaxDurationSeconds + 1);
        var missingSafety = CreateSectionBoxExactReplayDraft();
        missingSafety.ExactReplay.SafetyEnvelope.StepLimits["isolateCapturedBox"].MaxDurationSeconds = null;
        var mismatchedSafety = CreateSectionBoxExactReplayDraft();
        mismatchedSafety.ExactReplay.SafetyEnvelope.StepLimits["isolateCapturedBox"].MaxDurationSeconds = 120;

        Assert.Contains(_service.ValidateDraft(missing).Errors, error =>
            error.Contains("required tool argument is missing: maxDurationSeconds", StringComparison.Ordinal));
        Assert.Contains(_service.ValidateDraft(referenced).Errors, error =>
            error.Contains("maxDurationSeconds must be a literal integer", StringComparison.Ordinal));
        Assert.Contains(_service.ValidateDraft(aboveHardMaximum).Errors, error =>
            error.Contains("between 1 and 480", StringComparison.Ordinal));
        Assert.Contains(_service.ValidateDraft(missingSafety).Errors, error =>
            error.Contains("safety maxDurationSeconds is required", StringComparison.Ordinal));
        Assert.Contains(_service.ValidateDraft(mismatchedSafety).Errors, error =>
            error.Contains("must equal the literal", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionBoxExactReplay_DurationParticipatesInFingerprint()
    {
        var draft = CreateSectionBoxExactReplayDraft();
        var preview = _service.Save(draft, "", "", false, true, true);
        Assert.True(preview.Ok, string.Join("; ", preview.Errors));
        draft.Steps[0].Arguments["maxDurationSeconds"] = Element(120);
        draft.ExactReplay.SafetyEnvelope.StepLimits["isolateCapturedBox"].MaxDurationSeconds = 120;

        var validation = _service.ValidateDraft(draft);

        Assert.Contains(validation.Errors, error =>
            error.Contains("previewFingerprint does not match", StringComparison.Ordinal));
    }

    private ScenarioMutationResponse Save(ScenarioDraft draft, bool exact = false)
    {
        var response = _service.Save(draft, "", "", true, true, exact);
        Assert.True(response.Ok, response.ErrorMessage + " " + string.Join("; ", response.Errors));
        return response;
    }

    private static ScenarioDraft CreateTemplateDraft()
    {
        return new ScenarioDraft
        {
            SchemaVersion = 1,
            ExecutionMode = "template",
            Name = "Выгрузка свойств",
            Description = "Выгрузка выбранных свойств в CSV.",
            Context = new ScenarioContext { NavisworksVersions = new List<string> { "2027" } },
            Parameters = new List<ScenarioParameterDefinition>
            {
                new()
                {
                    Name = "outputPath",
                    Type = "filePath",
                    Title = "Файл отчёта",
                    Required = true,
                },
            },
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "exportSelection",
                    Tool = "selection_export_properties",
                    ScenarioContractVersion = 1,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["outputPath"] = ParameterReference("outputPath"),
                        ["format"] = Element("csv"),
                    },
                },
            },
        };
    }

    private static ScenarioDraft CreateExactDraft()
    {
        var draft = CreateTemplateDraft();
        draft.ExecutionMode = "exactReplay";
        draft.Name = "Точная выгрузка свойств";
        draft.ExactReplay = new ScenarioExactReplayDefinition
        {
            FixedParameters = new Dictionary<string, JsonElement>
            {
                ["outputPath"] = Element(@"D:\Reports\selection.csv"),
            },
            ContextPolicy = "strict",
            WritePolicy = "repeatReviewedWrites",
            SafetyEnvelope = new ScenarioSafetyEnvelope
            {
                PreviewFingerprint = "sha256:" + new string('a', 64),
                StepLimits = new Dictionary<string, ScenarioStepSafetyLimit>
                {
                    ["exportSelection"] = new()
                    {
                        MaxMatchedItems = 100,
                        MaxFileWrites = 1,
                    },
                },
            },
        };
        return draft;
    }

    private static ScenarioDraft CreateSectionBoxExactReplayDraft()
    {
        var geometry = new SectionBoxGeometry
        {
            FormatVersion = 1,
            CoordinateSpace = "document_global",
            DocumentUnits = "meters",
            Center = new BoxVector3 { X = 10, Y = 20, Z = 30 },
            HalfExtents = new BoxVector3 { X = 4, Y = 3, Z = 2 },
            Axes = new List<BoxVector3>
            {
                new() { X = 0.7071067811865476, Y = 0.7071067811865475, Z = 0 },
                new() { X = -0.7071067811865475, Y = 0.7071067811865476, Z = 0 },
                new() { X = 0, Y = 0, Z = 1 },
            },
        };
        return new ScenarioDraft
        {
            SchemaVersion = 2,
            ExecutionMode = "exactReplay",
            Name = "Literal section box replay",
            Description = "Replays isolation from captured literal oriented-box geometry.",
            Context = new ScenarioContext { NavisworksVersions = new List<string> { "2027" } },
            Parameters = new List<ScenarioParameterDefinition>(),
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "isolateCapturedBox",
                    Tool = "isolate_by_box",
                    ScenarioContractVersion = 1,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["box"] = JsonSerializer.SerializeToElement(
                            geometry,
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        ["maxScannedItems"] = Element(SectionBoxIsolationLimits.DefaultMaxScannedItems),
                        ["maxDurationSeconds"] = Element(SectionBoxIsolationLimits.DefaultMaxDurationSeconds),
                        ["previewLimit"] = Element(10),
                    },
                },
            },
            ExactReplay = new ScenarioExactReplayDefinition
            {
                FixedParameters = new Dictionary<string, JsonElement>(),
                ContextPolicy = "strict",
                WritePolicy = "repeatReviewedWrites",
                SafetyEnvelope = new ScenarioSafetyEnvelope
                {
                    PreviewFingerprint = "sha256:" + new string('a', 64),
                    StepLimits = new Dictionary<string, ScenarioStepSafetyLimit>
                    {
                        ["isolateCapturedBox"] = new()
                        {
                            MaxMatchedItems = SectionBoxIsolationLimits.MaximumMaxScannedItems,
                            MaxModelWrites = SectionBoxIsolationLimits.MaximumMaxScannedItems,
                            MaxDurationSeconds = SectionBoxIsolationLimits.DefaultMaxDurationSeconds,
                        },
                    },
                },
            },
        };
    }

    private static ScenarioDraft CreateMatrixScenarioV2()
    {
        return new ScenarioDraft
        {
            SchemaVersion = 2,
            ExecutionMode = "template",
            Name = "Matrix workflow",
            Context = new ScenarioContext { NavisworksVersions = new List<string> { "2027" } },
            Parameters = new List<ScenarioParameterDefinition>
            {
                new()
                {
                    Name = "namePrefix",
                    Type = "string",
                    Default = Element("100000-XXX1-YY"),
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
                        ["conditions"] = Element(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item",
                                ["property"] = "Name",
                                ["operator"] = "wildcard",
                                ["value"] = "/100000-XXX1-YY-01*",
                            },
                        }),
                        ["scope"] = Element("direct_children_of"),
                        ["parentConditions"] = Element(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item",
                                ["property"] = "Name",
                                ["operator"] = "equals",
                                ["value"] = "/100000-XXX1-YY-01",
                            },
                        }),
                    },
                },
                new()
                {
                    StepId = "matrix",
                    Tool = "clash_create_matrix_from_selection",
                    ScenarioContractVersion = 2,
                    ReviewedWrites = new List<string> { "removePreviousGenerated" },
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["namePrefix"] = ParameterReference("namePrefix"),
                        ["removePreviousGenerated"] = Element(true),
                        ["pairNameTemplate"] = Element("{index|zeroPad:3} 100000-XXX1-YY {aCode}-{bCode}"),
                    },
                },
            },
        };
    }

    private static JsonElement ParameterReference(string name)
    {
        return Element(new Dictionary<string, string> { ["$parameter"] = name });
    }

    private static JsonElement Element<T>(T value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }
}
