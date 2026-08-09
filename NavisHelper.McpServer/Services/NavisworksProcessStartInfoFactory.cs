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
        var processSystemRootValid = TryValidateWindowsDirectory(processSystemRoot, requireFontsUri: true, out _);
        var machineWindir = _environmentSource.GetMachineVariable("windir");
        var machineSystemRoot = _environmentSource.GetMachineVariable("SystemRoot");
        var osWindowsDirectory = _environmentSource.GetOsWindowsDirectory();

        var windowsRoot = ResolveWindowsRoot(
            processWindir,
            processSystemRoot,
            machineWindir,
            machineSystemRoot,
            osWindowsDirectory);

        SetEnvironmentValue(startInfo.Environment, "windir", windowsRoot.Value);
        SetEnvironmentValue(startInfo.Environment, "SystemRoot", windowsRoot.Value);

        var fontsUriValid = TryValidateWindowsDirectory(windowsRoot.Value, requireFontsUri: true, out _);
        if (!fontsUriValid)
            throw new InvalidOperationException("The normalized Windows directory does not produce a valid absolute Fonts URI.");

        return new NavisworksProcessStartInfoBuildResult(
            startInfo,
            new WindowsLaunchEnvironmentFacts
            {
                ProcessWindirPresent = !string.IsNullOrWhiteSpace(processWindir),
                ProcessWindirValid = processWindirValid,
                WindirSource = windowsRoot.Source,
                ProcessSystemRootPresent = !string.IsNullOrWhiteSpace(processSystemRoot),
                ProcessSystemRootValid = processSystemRootValid,
                SystemRootSource = windowsRoot.Source,
                FontsUriValid = fontsUriValid,
                WorkingDirectorySet = !string.IsNullOrWhiteSpace(startInfo.WorkingDirectory),
            });
    }

    internal static bool TryValidateWindowsDirectory(string value, bool requireFontsUri, out Uri fontsUri)
    {
        fontsUri = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (!Path.IsPathFullyQualified(value))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value);
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(fullPath))
            return false;

        if (!Directory.Exists(Path.Combine(fullPath, "System32")))
            return false;

        if (!requireFontsUri)
            return true;

        var fontsDirectory = Path.Combine(fullPath, "Fonts");
        if (!Directory.Exists(fontsDirectory))
            return false;

        var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\\Fonts\\";
        return Uri.TryCreate(candidate, UriKind.Absolute, out fontsUri) && fontsUri.IsAbsoluteUri;
    }

    private static ResolvedEnvironmentValue ResolveWindowsRoot(
        string processWindir,
        string processSystemRoot,
        string machineWindir,
        string machineSystemRoot,
        string osWindowsDirectory)
    {
        if (TrySelectConsistentWindowsRoot(processWindir, processSystemRoot, out var processRoot))
            return new ResolvedEnvironmentValue(processRoot, "process");

        if (TrySelectConsistentWindowsRoot(machineWindir, machineSystemRoot, out var machineRoot))
            return new ResolvedEnvironmentValue(machineRoot, "machine");

        if (TrySelectConsistentWindowsRoot(osWindowsDirectory, osWindowsDirectory, out var osRoot))
            return new ResolvedEnvironmentValue(osRoot, "os");

        throw new InvalidOperationException("A valid and consistent Windows root could not be resolved for windir and SystemRoot.");
    }

    private static bool TrySelectConsistentWindowsRoot(string windir, string systemRoot, out string selectedRoot)
    {
        selectedRoot = null;
        var windirValid = TryGetFullWindowsRoot(windir, out var fullWindir);
        var systemRootValid = TryGetFullWindowsRoot(systemRoot, out var fullSystemRoot);

        if (windirValid && systemRootValid)
        {
            if (!string.Equals(fullWindir, fullSystemRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            selectedRoot = fullWindir;
            return true;
        }

        if (windirValid)
        {
            selectedRoot = fullWindir;
            return true;
        }

        if (systemRootValid)
        {
            selectedRoot = fullSystemRoot;
            return true;
        }

        return false;
    }

    private static bool TryGetFullWindowsRoot(string value, out string fullPath)
    {
        fullPath = null;
        if (!TryValidateWindowsDirectory(value, requireFontsUri: true, out _))
            return false;

        fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
        return true;
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
