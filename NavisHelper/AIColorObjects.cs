using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.AI;

namespace NavisHelper
{
    [Plugin("AIColorObjects", "CBC", DisplayName = "AI Color Objects")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class AIColorObjects : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            return AIColorOperationCoordinator.Current.TryStartOpenRouter()
                ? 1
                : 0;
        }
    }
}
