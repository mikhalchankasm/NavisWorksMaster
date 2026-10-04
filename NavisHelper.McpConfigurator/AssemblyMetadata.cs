using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace NavisHelper.McpConfigurator;

internal sealed record AssemblyEvidence(string Status, string? Version = null, string? Sha256 = null);

internal static class AssemblyMetadata
{
    internal const long MaximumBytes = 32 * 1024 * 1024;

    internal static AssemblyEvidence Read(string path, string expectedName)
    {
        try
        {
            // A single read-only handle supplies metadata and hash; no Assembly.Load or execution.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = stream.Length;
            if (length > MaximumBytes)
                return new("file_too_large");
            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
                return new("read_failed");
            using var pe = new PEReader(new MemoryStream(bytes, writable: false));
            var reader = pe.GetMetadataReader();
            var definition = reader.GetAssemblyDefinition();
            if (!reader.GetString(definition.Name).Equals(expectedName, StringComparison.Ordinal))
                return new("invalid_assembly");
            var version = definition.Version.ToString();
            return new("readable", version, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (FileNotFoundException) { return new("missing"); }
        catch (DirectoryNotFoundException) { return new("missing"); }
        catch (UnauthorizedAccessException) { return new("read_failed"); }
        catch (IOException) { return new("read_failed"); }
        catch (BadImageFormatException) { return new("invalid_assembly"); }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return new("invalid_assembly");
        }
    }
}
