using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core.Localization;

namespace NavisHelper
{
    [Plugin("SelectionHatchBoundsMarker", "CBC", DisplayName = "Маркер габаритов")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class SelectionHatchBoundsMarker : SelectionHatchMarker
    {
        protected override bool UseObjectBoundingCorners => true;
        protected override string CommandTitle =>
            UiLocalizationService.Current.GetString("SelectionBoundsMarkerTitle");

        public override int Execute(params string[] parameters)
        {
            return base.Execute(parameters);
        }
    }
}
