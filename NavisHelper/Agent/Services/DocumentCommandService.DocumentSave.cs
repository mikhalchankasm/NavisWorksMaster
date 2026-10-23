using System;
using System.IO;
using System.Reflection;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public SaveDocumentResponse SaveDocument(Document document, SaveDocumentRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var currentPath = NormalizeExistingDocumentPath(document.FileName);
            return SaveDocumentToPath(document, currentPath, false);
        }

        public SaveDocumentResponse SaveDocumentAs(Document document, SaveDocumentAsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            return SaveDocumentAsToPath(document, request.Path, request.Overwrite == true,
                request.EmbedTextures == true, request.FileVersion);
        }

        internal static SaveDocumentResponse SaveDocumentAsToPath(Document document, string path,
            bool overwrite, bool embedTextures, string fileVersion)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var targetPath = NormalizeSaveAsPath(path);
            var error = SaveAsOptionsRules.DescribeError(targetPath, embedTextures, fileVersion);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var exists = File.Exists(targetPath);
            if (exists && !overwrite)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The target file already exists. Pass overwrite=true to replace it: " + targetPath);

            return SaveDocumentToPath(document, targetPath, exists, embedTextures, fileVersion);
        }

        internal static SaveDocumentResponse SaveDocumentToPath(Document document, string path, bool overwritten,
            bool embedTextures = false, string fileVersion = null)
        {
            try
            {
                SaveAsOptionsRules.TryParseFileVersion(fileVersion, out var year);
                var version = default(DocumentFileVersion);
                if (year.HasValue && !Enum.TryParse("Navisworks" + year.Value, out version))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation,
                        "fileVersion " + year.Value + " is not supported by this Navisworks version.");

                bool saved;
                if (embedTextures)
                {
                    var options = new NwdExportOptions();
                    if (year.HasValue)
                        options.FileVersion = (int)version;
                    var setEmbedTextures = typeof(NwdExportOptions).GetMethod("SetEmbedTextures",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (setEmbedTextures == null)
                        throw new AgentCommandException(ErrorCodes.CommandFailed,
                            "embedTextures is not available in this Navisworks version.");
                    setEmbedTextures.Invoke(options, new object[] { true });
                    var properties = new PublishProperties { AllowResave = true };
                    saved = document.TryPublishFile(path, options, properties);
                }
                else
                    saved = year.HasValue ? document.TrySaveFile(path, version) : document.TrySaveFile(path);

                if (!saved)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Navisworks did not save the document: " + path);

                if (!File.Exists(path))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Navisworks reported a successful save but the output file was not found: " + path);

                var info = new FileInfo(path);
                return new SaveDocumentResponse
                {
                    Path = info.FullName,
                    Format = Path.GetExtension(info.Name).TrimStart('.').ToLowerInvariant(),
                    FileSizeBytes = info.Length,
                    Overwritten = overwritten,
                    Published = embedTextures,
                    TexturesEmbedded = embedTextures,
                    FileVersion = year.HasValue ? fileVersion : null,
                };
            }
            catch (AgentCommandException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Navisworks could not save the document to " + path + ": " + ex.Message);
            }
        }

        internal static string NormalizeExistingDocumentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The current document has no file path. Use save_document_as with an absolute .nwd or .nwf path.");

            return NormalizeSavePath(path);
        }

        private static string NormalizeSaveAsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "path is required.");
            if (!Path.IsPathRooted(path.Trim()))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "path must be absolute.");

            return NormalizeSavePath(path);
        }

        private static string NormalizeSavePath(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "path is invalid: " + ex.Message);
            }

            var extension = Path.GetExtension(fullPath);
            if (!string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "path must have a .nwd or .nwf extension.");
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The target directory does not exist: " + (directory ?? string.Empty));

            return fullPath;
        }
    }
}
