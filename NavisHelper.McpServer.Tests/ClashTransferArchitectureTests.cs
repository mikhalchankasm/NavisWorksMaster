using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashTransferArchitectureTests
{
    [Fact]
    public void XmlParser_UsesHardenedReaderAndNeverLoadsSchemas()
    {
        var source = Read("NavisHelper.Contracts", "ClashBatchtestXmlParser.cs");
        Assert.Contains("DtdProcessing = DtdProcessing.Prohibit", source);
        Assert.Contains("XmlResolver = null", source);
        Assert.Contains("ValidationType = ValidationType.None", source);
        Assert.Contains("MaxCharactersInDocument", source);
        Assert.DoesNotContain("XmlSchemaSet", source);
        Assert.DoesNotContain("Schemas.Add", source);
    }

    [Fact]
    public void NewTools_HaveDedicatedContainerAndReuseExistingMutationService()
    {
        var tools = Read("NavisHelper.McpServer", "Tools", "NavisworksClashTransferTools.cs");
        var importer = Read("NavisHelper", "Agent", "Services", "ClashBatchtestImportService.cs");
        Assert.Contains("class NavisworksClashTransferTools", tools);
        Assert.Contains("ClashTestsExport", tools);
        Assert.Contains("ClashBatchtestImport", tools);
        Assert.Contains("_creationService.Execute", importer);
        Assert.DoesNotContain("new ClashTest {", importer);
        Assert.DoesNotContain("new ClashTest(", importer);
    }

    [Fact]
    public void JsonPlanLoader_KeepsLegacyPairsAndAddsVersionedTransferPlan()
    {
        var service = Read("NavisHelper", "Agent", "Services", "ClashTestsFromSetsService.cs");
        Assert.Contains("token as JArray ?? token[\"pairs\"] as JArray", service);
        Assert.Contains("ClashTransferPlanHelper.Validate", service);
        Assert.Contains("ClashTransferPlanHelper.ToPairs(plan, false)", service);
        Assert.Contains("SnakeCaseNamingStrategy", service);
        Assert.Contains("token.ToObject<ClashTestTransferPlan>(serializer)", service);
        Assert.Contains("RollbackMutations", service);
    }

    [Fact]
    public void Exporter_UsesSharedStrictToleranceUnitConverter()
    {
        var exporter = Read("NavisHelper", "Agent", "Services", "ClashTestTransferExporterService.cs");
        Assert.Contains("ClashToleranceUnitConverter.ToMillimeters(value, document.Units.ToString())", exporter);
        Assert.Contains("case Units.Micrometers:", exporter);
        Assert.Contains("case Units.Mils:", exporter);
        Assert.Contains("case Units.Microinches:", exporter);
        Assert.DoesNotContain("default: return value * 1000.0", exporter);
    }

    [Fact]
    public void ImportTool_DefaultsToDryRunNoOverwriteAndFailFastRollback()
    {
        var tools = Read("NavisHelper.McpServer", "Tools", "NavisworksClashTransferTools.cs");
        Assert.Contains("bool apply = false", tools);
        Assert.Contains("bool overwriteExisting = false", tools);
        Assert.Contains("bool continueOnError = false", tools);

        var importer = Read("NavisHelper", "Agent", "Services", "ClashBatchtestImportService.cs");
        Assert.Contains("Apply = request.Apply == true", importer);
        Assert.Contains("OverwriteExisting = request.OverwriteExisting == true", importer);
        Assert.Contains("ContinueOnError = request.ContinueOnError == true", importer);
        Assert.Contains("string.Equals(type, \"ModelRoot\"", importer);
        Assert.Contains("return ClashTransferSideKinds.ModelRoot", importer);
    }

    [Fact]
    public void SharedMutationPath_DryRunDoesNotMutateAndConflictsTriggerRollback()
    {
        var service = Read("NavisHelper", "Agent", "Services", "ClashTestsFromSetsService.cs");
        Assert.Contains("if (!apply)\n                        continue;", service.Replace("\r\n", "\n"));
        Assert.Contains("previous != null && request.OverwriteExisting != true", service);
        Assert.Contains("if (apply && !continueOnError)", service);
        Assert.DoesNotContain("if (!continueOnError)", service);
        Assert.Contains("RollbackMutations(clash, appliedMutations, response)", service);
        Assert.Contains("mutation.Item.RolledBack = true", service);
        Assert.Contains("mutation.Item.Status = \"rolled_back\"", service);
        Assert.Contains("ClashTestMutationService.RemoveTest", service);
        Assert.Contains("ClashApiCompat.AddClashTestCopy(clash.TestsData, mutation.PreviousCopy)", service);
    }

    [Fact]
    public void ProductionTransferCode_DoesNotReferenceUserReportsModelsLogsOrCredentials()
    {
        var files = new[]
        {
            Path.Combine(Root(), "NavisHelper.Contracts", "ClashBatchtestXmlParser.cs"),
            Path.Combine(Root(), "NavisHelper", "Agent", "Services", "ClashTestTransferExporterService.cs"),
            Path.Combine(Root(), "NavisHelper", "Agent", "Services", "ClashBatchtestImportService.cs"),
            Path.Combine(Root(), "NavisHelper.McpServer", "Tools", "NavisworksClashTransferTools.cs"),
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("12470", source);
            Assert.DoesNotContain("AppData", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mcp-calls", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("API_KEY", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BboxArtifact_DoesNotPersistUnverifiableSelfHashOrOptimisticVerifiedStatus()
    {
        var source = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.BboxSupport.cs");
        Assert.Contains("BuildClashBboxPlanJson", source);
        Assert.Contains("artifact.Remove(nameof(ClashBboxPairPlanResponse.OutputWritten))", source);
        Assert.Contains("artifact.Remove(nameof(ClashBboxPairPlanResponse.ArtifactStatus))", source);
        Assert.Contains("artifact.Remove(nameof(ClashBboxPairPlanResponse.Sha256))", source);
        Assert.DoesNotContain("response.ArtifactStatus = ClashTransferArtifactStatuses.WrittenVerified", source);
    }

    [Fact]
    public void CreateSearchSet_InternalOnlyFastPathAvoidsDisplayResolutionAndDisplayStillWinsWhenBothExist()
    {
        var source = Read("NavisHelper", "Agent", "Services", "SelectionSetSearchBuilder.cs");
        var displayBranch = source.IndexOf("condition.Category) && !string.IsNullOrWhiteSpace(condition.Property)", StringComparison.Ordinal);
        var internalBranch = source.IndexOf("condition.CategoryInternal) && !string.IsNullOrWhiteSpace(condition.PropertyInternal)", StringComparison.Ordinal);
        Assert.True(displayBranch >= 0 && internalBranch > displayBranch);
        var internalBlock = source.Substring(internalBranch, Math.Min(300, source.Length - internalBranch));
        Assert.Contains("SearchCondition.HasPropertyByName", internalBlock);
        Assert.DoesNotContain("TryResolvePersistedPropertyBinding", internalBlock);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate NavisHelper.sln.");
    }
}
