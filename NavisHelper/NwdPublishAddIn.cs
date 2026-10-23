using System;
using System.IO;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    [Plugin("PublishNwd", "CBC", DisplayName = "Publish NWD")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class NwdPublishAddIn : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                if (parameters == null || parameters.Length == 0 ||
                    !string.Equals(Path.GetExtension(parameters[0]), ".nwd", StringComparison.OrdinalIgnoreCase))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "A target .nwd path is required.");

                var embedTextures = false;
                string fileVersion = null;
                for (var i = 1; i < parameters.Length; i++)
                {
                    var option = parameters[i];
                    if (string.Equals(option, "embedTextures", StringComparison.OrdinalIgnoreCase))
                        embedTextures = true;
                    else if (option != null && option.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                        fileVersion = option.Substring("version=".Length);
                    else
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unknown PublishNwd option: " + option);
                }

                DocumentCommandService.SaveDocumentAsToPath(Application.ActiveDocument, parameters[0],
                    true, embedTextures, fileVersion);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message, "PublishNwd");
                return 1;
            }
        }
    }
}
