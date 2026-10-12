using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NavisHelper.McpConfigurator;

internal sealed record ServerActivation(bool Activated, string VersionDirectory, string? PreviousDirectory);

internal sealed class ServerVersionStore
{
    private const string Server = "NavisHelper.McpServer";
    private readonly string _root;
    private readonly string _versions;
    private readonly Func<string, bool> _healthCheck;
    private string Link => Path.Combine(_root, "McpServer");
    private string StatePath => Path.Combine(_versions, "state.json");
    private sealed record State(string? Current, string? Previous);

    public ServerVersionStore(string installRoot, Func<string, bool> healthCheck)
    {
        _root = Path.GetFullPath(installRoot);
        _versions = Path.Combine(_root, "versions");
        _healthCheck = healthCheck;
    }

    public ServerActivation Install(string sourceDirectory)
    {
        RecoverActivation();
        var source = Path.GetFullPath(sourceDirectory);
        if (!File.Exists(Path.Combine(source, Server + ".exe")))
            throw new FileNotFoundException("MCP server executable was not found.", Path.Combine(source, Server + ".exe"));
        var destination = Path.Combine(_versions, VersionName(source));
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The source tree cannot contain the version store.");
        EnsurePlainDirectory(_root);
        EnsurePlainDirectory(_versions);
        var hadVersions = Directory.Exists(_versions);
        var created = !Directory.Exists(destination);
        if (!created) VerifyVersion(destination);
        var migrated = false;
        string? legacy = null;
        try
        {
            Directory.CreateDirectory(_versions);
            if (created)
            {
                CopyTree(source, destination);
                if (!string.Equals(VersionName(destination), Path.GetFileName(destination), StringComparison.Ordinal))
                    throw new IOException("The MCP server source changed during copying; retry installation.");
            }
            else
                EnsurePlainDirectory(destination);
            var state = ReadState();
            var previous = CurrentTarget() ?? state.Current;
            if (!_healthCheck(Path.Combine(destination, Server + ".exe")))
            {
                if (created) DeleteVersion(destination);
                RemoveEmptyVersions(hadVersions);
                return new(false, destination, previous);
            }

            if (Directory.Exists(Link) && new DirectoryInfo(Link).LinkTarget == null)
            {
                legacy = Path.Combine(_versions, VersionName(Link));
                // If legacy code matches this new copy, move the original into the same slot.
                if (created && Same(legacy, destination)) DeleteVersion(destination);
                try { Directory.Move(Link, legacy); }
                catch (IOException ex)
                {
                    throw new IOException("Close MCP clients before migrating the legacy MCP server directory.", ex);
                }
                migrated = true;
                previous = legacy;
            }

            // Reinstalling the active version must preserve the rollback slot.
            var next = new State(destination, Same(previous, destination) ? state.Previous : previous);
            Activate(next);
            return new(true, destination, next.Previous);
        }
        catch
        {
            if (migrated && legacy != null)
            {
                DeleteJunction(Link);
                Directory.Move(legacy, Link);
            }
            if (created && Directory.Exists(destination) && !Same(CurrentTarget(), destination))
                DeleteVersion(destination);
            RemoveEmptyVersions(hadVersions);
            throw;
        }
    }

    public bool Rollback()
    {
        RecoverActivation();
        var state = ReadState();
        if (state.Previous == null || !Directory.Exists(state.Previous))
            return false;
        EnsurePlainDirectory(state.Previous);
        if (!_healthCheck(Path.Combine(state.Previous, Server + ".exe")))
            return false;
        Activate(new(state.Previous, CurrentTarget() ?? state.Current));
        return true;
    }

    public IReadOnlyList<string> Prune(Func<string, bool> inUse)
    {
        RecoverActivation();
        if (!Directory.Exists(_versions)) return Array.Empty<string>();
        var state = ReadState();
        var current = CurrentTarget();
        var deleted = new List<string>();
        foreach (var directory in Directory.GetDirectories(_versions).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Same(directory, current) || Same(directory, state.Current) ||
                Same(directory, state.Previous) || inUse(directory)) continue;
            DeleteVersion(directory);
            deleted.Add(directory);
        }
        return deleted;
    }

    private static string VersionName(string directory)
    {
        var hash = TreeHash(directory);
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, Server + ".deps.json")));
        var versions = deps.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(entry => entry.Name.StartsWith(Server + "/", StringComparison.Ordinal))
            .Select(entry => entry.Name[(Server.Length + 1)..]).Distinct().ToArray();
        if (versions.Length != 1 || !Version.TryParse(versions[0], out _))
            throw new InvalidDataException("The MCP server dependency manifest must identify one version.");
        return versions[0] + "-" + hash[..8];
    }

    private static string TreeHash(string directory)
    {
        var files = new List<string>();
        CollectFiles(directory);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in files.OrderBy(path => path, StringComparer.Ordinal))
        {
            using var file = File.OpenRead(Path.Combine(directory, relative));
            var fileHash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            digest.AppendData(Encoding.UTF8.GetBytes(relative + "\0" + fileHash + "\n"));
        }
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();

        void CollectFiles(string folder)
        {
            EnsurePlainDirectory(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse points are not supported in an MCP server tree.");
                if ((attributes & FileAttributes.Directory) != 0) CollectFiles(entry);
                else files.Add(Path.GetRelativePath(directory, entry).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }
    }

    private State ReadState()
    {
        EnsurePlainDirectory(_versions);
        if (!File.Exists(StatePath)) return new(null, null);
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath))
            ?? throw new InvalidDataException("Invalid MCP version state.");
        foreach (var path in new[] { state.Current, state.Previous })
            if (path != null) ValidateVersionPath(path);
        return state;
    }

    private string? CurrentTarget()
    {
        var target = new DirectoryInfo(Link).LinkTarget;
        if (target == null) return null;
        var resolved = Path.GetFullPath(target, _root);
        ValidateVersionPath(resolved);
        return resolved;
    }

    private void RecoverActivation()
    {
        var pending = Link + ".next";
        var pendingState = StatePath + ".next";
        var pendingTarget = new DirectoryInfo(pending).LinkTarget;
        var hasPending = Directory.Exists(pending) || File.Exists(pending) || pendingTarget != null;
        var hasPendingState = File.Exists(pendingState) || Directory.Exists(pendingState) ||
            new FileInfo(pendingState).LinkTarget != null;
        if (!hasPending && !hasPendingState) return;

        EnsurePlainDirectory(_root);
        EnsurePlainDirectory(_versions);
        var current = CurrentTarget();
        if ((hasPending && pendingTarget == null) ||
            ((Directory.Exists(Link) || File.Exists(Link)) && current == null) ||
            new FileInfo(pendingState).LinkTarget != null)
            throw new IOException("MCP activation recovery requires junctions and a plain state.json.next.");
        if (pendingTarget != null)
        {
            pendingTarget = Path.GetFullPath(pendingTarget, _root);
            ValidateVersionPath(pendingTarget);
        }
        State? state = null;
        if (hasPendingState)
        {
            try
            {
                state = JsonSerializer.Deserialize<State>(File.ReadAllText(pendingState))
                    ?? throw new InvalidDataException("Invalid MCP version state.");
                foreach (var path in new[] { state.Current, state.Previous })
                    if (path != null) ValidateVersionPath(path);
            }
            catch (JsonException) when (!hasPending)
            {
                // Torn while writing state.json.next (step 1): no junction was touched; discard it.
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or
                NotSupportedException or UnauthorizedAccessException)
            {
                throw new IOException("Cannot read pending MCP version state.", ex);
            }
        }

        if (current == null && pendingTarget == null && state == null)
        {
            // A first activation torn at step 1 switched nothing.
            File.Delete(pendingState);
            return;
        }

        if (current != null)
        {
            // Steps 1, 2 or 4: keep McpServer; commit the pending state only if McpServer already points at it.
            var commit = state != null && Same(state.Current, current);
            var healthy = Healthy(current);
            if (!healthy && commit && CommittedFallback(current) is { } fallback)
            {
                DeleteJunction(Link);
                CreateJunction(Link, fallback);
                commit = false;
                healthy = true;
            }
            DeleteJunction(pending);
            if (hasPendingState)
            {
                if (commit) File.Move(pendingState, StatePath, overwrite: true);
                else File.Delete(pendingState);
            }
            if (!healthy) throw new IOException("Recovered MCP server failed its health check.");
            return;
        }

        // Step 3: the old junction is gone; finish the switch unless its target fails its health check.
        current = pendingTarget ?? state?.Current
            ?? throw new IOException("Pending MCP activation has no version target.");
        ValidateVersionPath(current);
        if ((state != null && !Same(state.Current, current)) || !Directory.Exists(current))
            throw new IOException("Pending MCP activation does not name one existing version.");
        if (!Healthy(current))
        {
            // Roll back to the committed state; a first activation has none and continues without one.
            var committed = ReadState().Current;
            if (committed != null && Directory.Exists(committed)) CreateJunction(Link, committed);
            DeleteJunction(pending);
            if (hasPendingState) File.Delete(pendingState);
            if (committed != null && !Healthy(committed))
                throw new IOException("Recovered MCP server failed its health check.");
            return;
        }
        if (pendingTarget != null) Directory.Move(pending, Link);
        else CreateJunction(Link, current);
        if (hasPendingState) File.Move(pendingState, StatePath, overwrite: true);
    }

    // A stored tree counts as healthy only if it still matches its version name and passes its check.
    private bool Healthy(string version)
    {
        try { VerifyVersion(version); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; }
        return _healthCheck(Path.Combine(version, Server + ".exe"));
    }

    private static void VerifyVersion(string directory)
    {
        try
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            if (!string.Equals(VersionName(directory), name, StringComparison.Ordinal))
                throw new IOException("The stored MCP server tree no longer matches its version name.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                   or KeyNotFoundException or InvalidOperationException)
        {
            throw new IOException($"Cannot reuse MCP server version directory '{directory}'.", ex);
        }
    }

    // The committed state.json Current, when it differs from the failed target and passes its check.
    private string? CommittedFallback(string failed)
    {
        var committed = ReadState().Current;
        return committed != null && !Same(committed, failed) && Directory.Exists(committed) && Healthy(committed)
            ? committed : null;
    }

    private void Activate(State next)
    {
        ValidateVersionPath(next.Current!);
        var pending = Link + ".next";
        var pendingState = StatePath + ".next";
        var oldTarget = CurrentTarget();
        var removed = false;
        var switched = false;
        if (Directory.Exists(pending) || new DirectoryInfo(pending).LinkTarget != null || File.Exists(pendingState))
            throw new IOException("An unfinished MCP activation exists; inspect McpServer.next and state.json.next.");
        try
        {
            File.WriteAllText(pendingState, JsonSerializer.Serialize(next));
            CreateJunction(pending, next.Current!);
            if (oldTarget != null)
            {
                DeleteJunction(Link);
                removed = true;
            }
            Directory.Move(pending, Link);
            switched = true;
            File.Move(pendingState, StatePath, overwrite: true);
        }
        catch
        {
            if (switched) DeleteJunction(Link);
            if (removed && oldTarget != null) CreateJunction(Link, oldTarget);
            throw;
        }
        finally
        {
            DeleteJunction(pending);
            if (File.Exists(pendingState)) File.Delete(pendingState);
        }
    }

    private static void CreateJunction(string link, string target)
    {
        // cmd expands these characters even inside quotes; reject them before invoking it.
        if ((link + target).IndexOfAny(['"', '%', '!', '\r', '\n']) >= 0)
            throw new IOException("Unsupported characters in MCP junction paths.");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/d /v:off /c mklink /J \"{link}\" \"{target}\"",
        };
        using var process = Process.Start(start) ?? throw new IOException("Cannot create MCP junction.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException("Cannot create MCP junction: " + error.Result.Trim());
    }

    private static void DeleteJunction(string path)
    {
        if (new DirectoryInfo(path).LinkTarget != null) Directory.Delete(path, recursive: false);
    }

    private void ValidateVersionPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Same(Path.GetDirectoryName(full), _versions))
            throw new InvalidDataException("MCP version path is outside the version store.");
        EnsurePlainDirectory(full);
    }

    private void DeleteVersion(string path)
    {
        ValidateVersionPath(path);
        DeleteTree(path);
    }

    private static void DeleteTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) == 0) File.Delete(entry);
            else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry, recursive: false);
            else DeleteTree(entry);
        }
        Directory.Delete(path, recursive: false);
    }

    private static void CopyTree(string source, string destination)
    {
        EnsurePlainDirectory(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are not supported in an MCP server source tree.");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyTree(entry, target);
            else File.Copy(entry, target);
        }
    }

    private static void EnsurePlainDirectory(string path)
    {
        if ((Directory.Exists(path) || new DirectoryInfo(path).LinkTarget != null) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("MCP version directories must not be reparse points.");
    }

    private void RemoveEmptyVersions(bool existed)
    {
        if (!existed && Directory.Exists(_versions) && !Directory.EnumerateFileSystemEntries(_versions).Any())
            Directory.Delete(_versions, recursive: false);
    }

    private static bool Same(string? a, string? b) => a != null && b != null &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}
