using System.Collections;
using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ToolCapabilitiesTests
{
    private static IReadOnlyList<MethodInfo> ToolMethods => typeof(NavisworksHostTools).Assembly
        .GetTypes()
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(method => method.IsDefined(typeof(McpServerToolAttribute), inherit: false))
        .ToList();

    [Fact]
    public void EveryMcpToolHasExactlyOneCapabilityDeclaration()
    {
        var methods = ToolMethods;
        Assert.Equal(105, methods.Count);
        foreach (var method in methods)
        {
            var declarations = method.GetCustomAttributes<ToolCapabilitiesAttribute>(inherit: false).ToList();
            Assert.True(declarations.Count == 1, $"{method.DeclaringType?.Name}.{method.Name} has {declarations.Count} capability declarations");
            Assert.False(declarations[0].RequiresDocument && !declarations[0].RequiresHost);
        }
    }

    [Fact]
    public void ToolsWithApplyParameterDeclareAnEffect()
    {
        foreach (var method in ToolMethods)
        {
            if (!method.GetParameters().Any(parameter => parameter.Name == "apply"))
                continue;

            var capabilities = method.GetCustomAttribute<ToolCapabilitiesAttribute>();
            Assert.NotNull(capabilities);
            Assert.NotEqual(ToolEffects.None, capabilities.Effects);
        }
    }

    [Fact]
    public void ScenarioCatalogWritesAgreeWithToolCapabilities()
    {
        var field = typeof(ScenarioLibraryService).GetField("ToolDescriptors", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var catalog = Assert.IsAssignableFrom<IDictionary>(field.GetValue(null));
        Assert.NotEmpty(catalog.Keys.Cast<object>());

        foreach (DictionaryEntry entry in catalog)
        {
            var descriptor = entry.Value;
            var descriptorType = descriptor.GetType();
            var method = Assert.IsAssignableFrom<MethodInfo>(descriptorType.GetProperty("Method")?.GetValue(descriptor));
            var capabilities = method.GetCustomAttribute<ToolCapabilitiesAttribute>();
            Assert.NotNull(capabilities);
            var mutatesModel = (bool)descriptorType.GetProperty("MutatesModel").GetValue(descriptor);
            var writesFiles = (bool)descriptorType.GetProperty("WritesFiles").GetValue(descriptor);
            Assert.True(!mutatesModel || (capabilities.Effects & (ToolEffects.View | ToolEffects.Document)) != 0,
                $"{entry.Key}: scenario catalog declares a model change without View or Document");
            Assert.True(!writesFiles || (capabilities.Effects & ToolEffects.Files) != 0,
                $"{entry.Key}: scenario catalog declares file output without Files");
        }
    }
}
