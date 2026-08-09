using System.Diagnostics;

namespace NavisHelper.McpServer.Services;

internal sealed class NavisworksProcessStartInfoFactory
{
    private readonly IWindowsLaunchEnvironmentSource _environmentSource;

    public NavisworksProcessStartInfoFactory()
        : this(new WindowsLaunchEnvironmentSource())
    {
    }

    internal NavisworksProcessStartInfoFactory(IWindowsLaunchEnvironmentSource environmentSource)
    {
        _environmentSource = environmentSource ?? throw new ArgumentNullException(nameof(environmentSource));
    }

    public NavisworksProcessStartInfoBuildResult Create(string roamerPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(roamerPath))
            throw new ArgumentException("Roamer path is required.", nameof(roamerPath));

        var workingDirectory = Path.GetDirectoryName(roamerPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new InvalidOperationException("The Navisworks executable directory could not be resolved.");

        var startInfo = new ProcessStartInfo
        {
            FileName = roamerPath,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        if (!string.IsNullOrWhiteSpace(filePath))
            startInfo.ArgumentList.Add(filePath);

        var processWindir = _environmentSource.GetProcessVariable("windir");
        var processSystemRoot = _environmentSource.GetProcessVariable("SystemRoot");
        var processWindirValid = TryValidateWindowsDirectory(processWindir, requireFontsUri: true, out _);
        var processSystemRootValid = TryValidateWindowsDirectory(processSystemRoot, requireFontsUri: false, out _);

        var windir = ResolveValue(
            processWindir,
            processWindirValid,
            _environmentSource.GetMachineVariable("windir"),
            requireFontsUri: true,
            _environmentSource.GetOsWindowsDirectory(),
            "windir");
        var systemRoot = ResolveValue(
            processSystemRoot,
            processSystemRootValid,
            _environmentSource.GetMachineVariable("SystemRoot"),
            requireFontsUri: false,
            _environmentSource.GetOsWindowsDirectory(),
            "SystemRoot");

        SetEnvironmentValue(startInfo.Environment, "windir", windir.Value);
        SetEnvironmentValue(startInfo.Environment, "SystemRoot", systemRoot.Value);

        var fontsUriValid = TryValidateWindowsDirectory(windir.Value, requireFontsUri: true, out _);
        if (!fontsUriValid)
            throw new InvalidOperationException("The normalized Windows directory does not produce a valid absolute Fonts URI.");

        return new NavisworksProcessStartInfoBuildResult(
            startInfo,
            new WindowsLaunchEnvironmentFacts
            {
                ProcessWindirPresent = !string.IsNullOrWhiteSpace(processWindir),
                ProcessWindirValid = processWindirValid,
                WindirSource = windir.Source,
                ProcessSystemRootPresent = !string.IsNullOrWhiteSpace(processSystemRoot),
                ProcessSystemRootValid = processSystemRootValid,
                SystemRootSource = systemRoot.Source,
                FontsUriValid = fontsUriValid,
                WorkingDirectorySet = !string.IsNullOrWhiteSpace(startInfo.WorkingDirectory),
            });
    }

    internal static bool TryValidateWindowsDirectory(string value, bool requireFontsUri, out Uri fontsUri)
    {
        fontsUri = null;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value.Trim());
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(fullPath))
            return false;

        if (!requireFontsUri)
            return true;

        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\\Fonts\\";
        return Uri.TryCreate(candidate, UriKind.Absolute, out fontsUri) && fontsUri.IsAbsoluteUri;
    }

    private static ResolvedEnvironmentValue ResolveValue(
        string processValue,
        bool processValueValid,
        string machineValue,
        bool requireFontsUri,
        string osValue,
        string variableName)
    {
        if (processValueValid)
            return new ResolvedEnvironmentValue(Path.GetFullPath(processValue.Trim()), "process");

        if (TryValidateWindowsDirectory(machineValue, requireFontsUri, out _))
            return new ResolvedEnvironmentValue(Path.GetFullPath(machineValue.Trim()), "machine");

        if (TryValidateWindowsDirectory(osValue, requireFontsUri, out _))
            return new ResolvedEnvironmentValue(Path.GetFullPath(osValue.Trim()), "os");

        throw new InvalidOperationException("A valid Windows directory could not be resolved for " + variableName + ".");
    }

    private static void SetEnvironmentValue(IDictionary<string, string> environment, string name, string value)
    {
        foreach (var key in environment.Keys.Where(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)).ToList())
            environment.Remove(key);

        environment[name] = value;
    }

    private sealed record ResolvedEnvironmentValue(string Value, string Source);
}

internal sealed record NavisworksProcessStartInfoBuildResult(
    ProcessStartInfo StartInfo,
    WindowsLaunchEnvironmentFacts EnvironmentFacts);

internal sealed class WindowsLaunchEnvironmentFacts
{
    public bool ProcessWindirPresent { get; init; }
    public bool ProcessWindirValid { get; init; }
    public string WindirSource { get; init; }
    public bool ProcessSystemRootPresent { get; init; }
    public bool ProcessSystemRootValid { get; init; }
    public string SystemRootSource { get; init; }
    public bool FontsUriValid { get; init; }
    public bool WorkingDirectorySet { get; init; }
}

internal interface IWindowsLaunchEnvironmentSource
{
    string GetProcessVariable(string name);
    string GetMachineVariable(string name);
    string GetOsWindowsDirectory();
}

internal sealed class WindowsLaunchEnvironmentSource : IWindowsLaunchEnvironmentSource
{
    public string GetProcessVariable(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
        catch
        {
            return null;
        }
    }

    public string GetMachineVariable(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        }
        catch
        {
            return null;
        }
    }

    public string GetOsWindowsDirectory()
    {
        try
        {
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDirectory))
                return windowsDirectory;
        }
        catch
        {
        }

        try
        {
            var systemDirectory = Environment.SystemDirectory;
            return string.IsNullOrWhiteSpace(systemDirectory) ? null : Directory.GetParent(systemDirectory)?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
