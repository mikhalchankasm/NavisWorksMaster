using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerArtifactPathPolicy
    {
        public const string ManagedDirectoryName = "NavisHelper.WorldMarkers";
        public const string UnsavedPortabilityWarning = "The active document is unsaved. World-marker DXF sidecars will be stored under LocalApplicationData and are not portable with an NWF until moved to a durable project directory.";
        public const string ExplicitDirectoryPortabilityWarning = "The explicit artifact directory is durable, but portability with the Navisworks document has not been verified.";

        public static WorldMarkerArtifactRoot ResolveManagedRoot(
            string documentFilePath,
            string explicitArtifactDirectory,
            string localApplicationDataDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitArtifactDirectory))
            {
                return new WorldMarkerArtifactRoot
                {
                    Path = NormalizeSafeDirectory(explicitArtifactDirectory, nameof(explicitArtifactDirectory)),
                    IsPortableWithDocument = false,
                    Warning = ExplicitDirectoryPortabilityWarning,
                };
            }

            if (!string.IsNullOrWhiteSpace(documentFilePath))
            {
                if (!Path.IsPathRooted(documentFilePath.Trim()))
                    throw new ArgumentException("documentFilePath must be absolute.", nameof(documentFilePath));
                var fullDocumentPath = Path.GetFullPath(documentFilePath.Trim());
                var documentDirectory = Path.GetDirectoryName(fullDocumentPath);
                if (string.IsNullOrWhiteSpace(documentDirectory))
                    throw new ArgumentException("documentFilePath must have a parent directory.", nameof(documentFilePath));
                var documentKey = BuildDocumentKey(fullDocumentPath);
                var root = Path.Combine(documentDirectory, ManagedDirectoryName, documentKey);
                return new WorldMarkerArtifactRoot
                {
                    Path = NormalizeSafeDirectory(root, nameof(documentFilePath)),
                    IsPortableWithDocument = true,
                    Warning = string.Empty,
                };
            }

            var localRoot = NormalizeSafeDirectory(localApplicationDataDirectory, nameof(localApplicationDataDirectory));
            return new WorldMarkerArtifactRoot
            {
                Path = NormalizeSafeDirectory(Path.Combine(localRoot, "NavisHelper", "WorldMarkers", "Unsaved"), nameof(localApplicationDataDirectory)),
                IsPortableWithDocument = false,
                Warning = UnsavedPortabilityWarning,
            };
        }

        public static string CreateRevisionId(DateTime utcTimestamp, Guid nonce)
        {
            if (nonce == Guid.Empty)
                throw new ArgumentException("A non-empty revision nonce is required.", nameof(nonce));
            return "r" + utcTimestamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + nonce.ToString("N").Substring(0, 12);
        }

        public static string BuildArtifactPath(string managedRoot, string markerId, string revisionId)
        {
            var root = NormalizeSafeDirectory(managedRoot, nameof(managedRoot));
            if (!IsMarkerId(markerId))
                throw new ArgumentException("markerId is not a generated world-marker ID.", nameof(markerId));
            if (!IsRevisionId(revisionId))
                throw new ArgumentException("revisionId is not a generated world-marker revision.", nameof(revisionId));

            var candidate = Path.GetFullPath(Path.Combine(root, markerId + "--" + revisionId + ".dxf"));
            if (!IsContainedPath(root, candidate))
                throw new ArgumentException("The generated artifact path escapes the managed root.", nameof(managedRoot));
            return candidate;
        }

        /// <summary>
        /// Performs lexical eligibility checks only. A future executor must also reject reparse points
        /// and delete only this exact, non-recursive tool-owned file path.
        /// </summary>
        public static bool IsCleanupCandidate(string managedRoot, string candidatePath)
        {
            try
            {
                var root = NormalizeSafeDirectory(managedRoot, nameof(managedRoot));
                if (string.IsNullOrWhiteSpace(candidatePath))
                    return false;
                if (!Path.IsPathRooted(candidatePath.Trim()))
                    return false;
                var candidate = Path.GetFullPath(candidatePath.Trim());
                if (!IsContainedPath(root, candidate) || !string.Equals(Path.GetExtension(candidate), ".dxf", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(TrimTrailingSeparators(Path.GetDirectoryName(candidate)), root, StringComparison.OrdinalIgnoreCase))
                    return false;

                var fileName = Path.GetFileNameWithoutExtension(candidate);
                var separator = fileName.IndexOf("--", StringComparison.Ordinal);
                if (separator <= 0 || fileName.IndexOf("--", separator + 2, StringComparison.Ordinal) >= 0)
                    return false;
                return IsMarkerId(fileName.Substring(0, separator)) && IsRevisionId(fileName.Substring(separator + 2));
            }
            catch (Exception ex) when (
                ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return false;
            }
        }

        public static bool IsMarkerId(string value)
        {
            return value != null && value.Length == 19 && value.StartsWith("wm-", StringComparison.Ordinal) && IsLowerHex(value, 3, 16);
        }

        public static bool IsRevisionId(string value)
        {
            if (value == null || value.Length != 33 || value[0] != 'r' || value[9] != 'T' || value[19] != 'Z' || value[20] != '-')
                return false;
            for (var i = 1; i <= 8; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            for (var i = 10; i <= 18; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            DateTime parsedTimestamp;
            if (!DateTime.TryParseExact(
                value.Substring(1, 19),
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedTimestamp))
            {
                return false;
            }
            return IsLowerHex(value, 21, 12);
        }

        private static string NormalizeSafeDirectory(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty absolute directory is required.", parameterName);
            if (!IsFullyQualifiedPath(value.Trim()))
                throw new ArgumentException("The managed artifact directory must be absolute.", parameterName);
            var fullPath = Path.GetFullPath(value.Trim());
            var pathRoot = Path.GetPathRoot(fullPath);
            if (string.Equals(TrimSeparators(fullPath), TrimSeparators(pathRoot), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A filesystem root cannot be used as the managed artifact directory.", parameterName);
            return TrimTrailingSeparators(fullPath);
        }

        private static bool IsContainedPath(string root, string candidate)
        {
            var prefix = TrimTrailingSeparators(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFullyQualifiedPath(string value)
        {
            if (!Path.IsPathRooted(value))
                return false;

            // Path.IsPathRooted can accept Windows drive-relative forms such as C:dir.
            // A drive path is fully qualified only when the colon is followed by a separator.
            if (value.Length >= 2 && value[1] == ':')
            {
                return value.Length >= 3 &&
                    (value[2] == Path.DirectorySeparatorChar || value[2] == Path.AltDirectorySeparatorChar);
            }

            if (Path.DirectorySeparatorChar == '\\')
            {
                // On Windows a separator-rooted path (\dir or /dir) still depends on the
                // current drive. Only UNC/device paths beginning with two separators qualify.
                return value.Length >= 2 &&
                    (value[0] == '\\' || value[0] == '/') &&
                    (value[1] == '\\' || value[1] == '/');
            }

            return true;
        }

        private static string BuildDocumentKey(string fullDocumentPath)
        {
            var baseName = Path.GetFileNameWithoutExtension(fullDocumentPath);
            var safeBaseName = new StringBuilder();
            foreach (var character in baseName)
            {
                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') || character == '-' || character == '_')
                {
                    safeBaseName.Append(character);
                }
            }
            if (safeBaseName.Length == 0)
                safeBaseName.Append("document");
            if (safeBaseName.Length > 32)
                safeBaseName.Length = 32;

            byte[] digest;
            using (var sha256 = SHA256.Create())
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(fullDocumentPath.ToUpperInvariant()));
            var suffix = new StringBuilder(12);
            for (var i = 0; i < 6; i++)
                suffix.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return safeBaseName + "-" + suffix;
        }

        private static bool IsLowerHex(string value, int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var character = value[i];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            }
            return true;
        }

        private static string TrimTrailingSeparators(string value)
        {
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string TrimSeparators(string value)
        {
            return (value ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
