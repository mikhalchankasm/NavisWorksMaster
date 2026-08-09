using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class NavisworksProcessStartInfoFactoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _processWindowsDirectory;
    private readonly string _machineWindowsDirectory;
    private readonly string _osWindowsDirectory;
    private readonly string _roamerPath;

    public NavisworksProcessStartInfoFactoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "NavisHelper-LaunchEnvironmentTests-" + Guid.NewGuid().ToString("N"));
        _processWindowsDirectory = CreateWindowsDirectory("process-windows");
        _machineWindowsDirectory = CreateWindowsDirectory("machine-windows");
        _osWindowsDirectory = CreateWindowsDirectory("os-windows");
        var roamerDirectory = Path.Combine(_tempDirectory, "Navisworks Manage 2027");
        Directory.CreateDirectory(roamerDirectory);
        _roamerPath = Path.Combine(roamerDirectory, "Roamer.exe");
    }

    [Fact]
    public void Create_MissingProcessWindir_UsesMachineWindirAndSetsWorkingDirectory()
    {
        var source = new FakeEnvironmentSource(
            processWindir: null,
            processSystemRoot: _processWindowsDirectory,
            machineWindir: _machineWindowsDirectory,
            machineSystemRoot: _machineWindowsDirectory,
            osWindowsDirectory: _osWindowsDirectory);

        var result = new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, string.Empty);

        Assert.False(result.EnvironmentFacts.ProcessWindirPresent);
        Assert.False(result.EnvironmentFacts.ProcessWindirValid);
        Assert.Equal("machine", result.EnvironmentFacts.WindirSource);
        Assert.Equal(_machineWindowsDirectory, result.StartInfo.Environment["windir"]);
        Assert.Equal(Path.GetDirectoryName(_roamerPath), result.StartInfo.WorkingDirectory);
        Assert.True(result.EnvironmentFacts.WorkingDirectorySet);
        Assert.True(result.EnvironmentFacts.FontsUriValid);
        Assert.False(result.StartInfo.UseShellExecute);
        Assert.Empty(result.StartInfo.ArgumentList);
    }

    [Fact]
    public void Create_InvalidProcessVariables_FallBackToMachineAndOs()
    {
        var source = new FakeEnvironmentSource(
            processWindir: "not-an-absolute-path",
            processSystemRoot: "also-invalid",
            machineWindir: _machineWindowsDirectory,
            machineSystemRoot: null,
            osWindowsDirectory: _osWindowsDirectory);

        var result = new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, string.Empty);

        Assert.True(result.EnvironmentFacts.ProcessWindirPresent);
        Assert.False(result.EnvironmentFacts.ProcessWindirValid);
        Assert.Equal("machine", result.EnvironmentFacts.WindirSource);
        Assert.Equal("os", result.EnvironmentFacts.SystemRootSource);
        Assert.Equal(_machineWindowsDirectory, result.StartInfo.Environment["windir"]);
        Assert.Equal(_osWindowsDirectory, result.StartInfo.Environment["SystemRoot"]);
    }

    [Fact]
    public void Create_InvalidMachineVariables_UsesOsFallback()
    {
        var source = new FakeEnvironmentSource(
            processWindir: null,
            processSystemRoot: null,
            machineWindir: "Z:\\missing-windows",
            machineSystemRoot: "Z:\\missing-system-root",
            osWindowsDirectory: _osWindowsDirectory);

        var result = new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, string.Empty);

        Assert.Equal("os", result.EnvironmentFacts.WindirSource);
        Assert.Equal("os", result.EnvironmentFacts.SystemRootSource);
        Assert.Equal(_osWindowsDirectory, result.StartInfo.Environment["windir"]);
        Assert.Equal(_osWindowsDirectory, result.StartInfo.Environment["SystemRoot"]);
    }

    [Fact]
    public void Create_ValidProcessVariables_PreservesThem()
    {
        var source = new FakeEnvironmentSource(
            processWindir: _processWindowsDirectory,
            processSystemRoot: _processWindowsDirectory,
            machineWindir: _machineWindowsDirectory,
            machineSystemRoot: _machineWindowsDirectory,
            osWindowsDirectory: _osWindowsDirectory);

        var result = new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, string.Empty);

        Assert.Equal("process", result.EnvironmentFacts.WindirSource);
        Assert.Equal("process", result.EnvironmentFacts.SystemRootSource);
        Assert.True(result.EnvironmentFacts.ProcessWindirValid);
        Assert.True(result.EnvironmentFacts.ProcessSystemRootValid);
    }

    [Fact]
    public void Create_CyrillicPathWithSpaces_IsOneLiteralArgument()
    {
        var filePath = Path.Combine(_tempDirectory, "Порт Бухта-Север 07-08-2026_15-56.nwd");
        var source = new FakeEnvironmentSource(
            _processWindowsDirectory,
            _processWindowsDirectory,
            _machineWindowsDirectory,
            _machineWindowsDirectory,
            _osWindowsDirectory);

        var result = new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, filePath);

        Assert.Single(result.StartInfo.ArgumentList);
        Assert.Equal(filePath, result.StartInfo.ArgumentList[0]);
    }

    [Fact]
    public void TryValidateWindowsDirectory_ProducesAbsoluteFontsUri()
    {
        var valid = NavisworksProcessStartInfoFactory.TryValidateWindowsDirectory(
            _processWindowsDirectory,
            requireFontsUri: true,
            out var fontsUri);

        Assert.True(valid);
        Assert.NotNull(fontsUri);
        Assert.True(fontsUri.IsAbsoluteUri);
        Assert.EndsWith("/Fonts/", fontsUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NoValidEnvironmentFallback_ThrowsBeforeProcessCreation()
    {
        var source = new FakeEnvironmentSource(
            processWindir: null,
            processSystemRoot: null,
            machineWindir: "not-valid",
            machineSystemRoot: "also-not-valid",
            osWindowsDirectory: "still-not-valid");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new NavisworksProcessStartInfoFactory(source).Create(_roamerPath, string.Empty));

        Assert.Contains("windir", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string CreateWindowsDirectory(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(Path.Combine(path, "Fonts"));
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, true);
        }
        catch
        {
        }
    }

    private sealed class FakeEnvironmentSource : IWindowsLaunchEnvironmentSource
    {
        private readonly Dictionary<string, string> _process;
        private readonly Dictionary<string, string> _machine;
        private readonly string _osWindowsDirectory;

        public FakeEnvironmentSource(
            string processWindir,
            string processSystemRoot,
            string machineWindir,
            string machineSystemRoot,
            string osWindowsDirectory)
        {
            _process = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["windir"] = processWindir,
                ["SystemRoot"] = processSystemRoot,
            };
            _machine = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["windir"] = machineWindir,
                ["SystemRoot"] = machineSystemRoot,
            };
            _osWindowsDirectory = osWindowsDirectory;
        }

        public string GetProcessVariable(string name) => _process.GetValueOrDefault(name);
        public string GetMachineVariable(string name) => _machine.GetValueOrDefault(name);
        public string GetOsWindowsDirectory() => _osWindowsDirectory;
    }
}
