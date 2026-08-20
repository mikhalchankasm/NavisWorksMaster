using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public sealed class VerifiedFileArtifact
    {
        public string OutputPath { get; set; }
        public long BytesWritten { get; set; }
        public string Sha256 { get; set; }
    }

    public static class VerifiedFileArtifactWriter
    {
        public static VerifiedFileArtifact WriteUtf8(string absolutePath, string content, bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                throw new ArgumentException("An absolute output path is required.", nameof(absolutePath));
            if (!Path.IsPathRooted(absolutePath))
                throw new ArgumentException("outputPath must be absolute.", nameof(absolutePath));

            var path = Path.GetFullPath(absolutePath);
            var partialPath = path + ".partial";
            string backupPath = null;
            var replacedExisting = false;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            if (File.Exists(path) && !overwriteExisting)
                throw new IOException("Output file already exists: " + path);

            TryDelete(partialPath);
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(content ?? string.Empty);
                using (var stream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    if (!overwriteExisting)
                        throw new IOException("Output file appeared during write and overwriteExisting=false: " + path);
                    backupPath = path + ".backup." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.Replace(partialPath, path, backupPath);
                    replacedExisting = true;
                }
                else
                {
                    File.Move(partialPath, path);
                }

                var info = new FileInfo(path);
                if (!info.Exists || info.Length != bytes.LongLength)
                    throw new IOException("Output artifact verification failed for " + path + ".");
                using (var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(read);
                    var artifact = new VerifiedFileArtifact
                    {
                        OutputPath = path,
                        BytesWritten = info.Length,
                        Sha256 = ToHex(hash),
                    };
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                        File.Delete(backupPath);
                    return artifact;
                }
            }
            catch (Exception writeError)
            {
                TryDelete(partialPath);
                if (replacedExisting && !string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Replace(backupPath, path, null);
                        else
                            File.Move(backupPath, path);
                    }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Artifact write/verification failed and the original could not be restored. Recovery backup remains at '" + backupPath + "': " + restoreError.Message, writeError);
                    }
                }
                throw;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes == null ? 0 : bytes.Length) * 2);
            if (bytes != null)
            {
                foreach (var value in bytes)
                    builder.Append(value.ToString("x2"));
            }
            return builder.ToString();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
