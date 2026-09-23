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

    private static readonly JsonSerializerOptions SectionBoxLiteralJsonOptions = new(ToolInputJsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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

}
