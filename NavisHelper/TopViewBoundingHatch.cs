using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core.Localization;

namespace NavisHelper
{
    [Plugin("TopViewBoundingHatch", "CBC", DisplayName = "Заштриховать габарит")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class TopViewBoundingHatch : TopViewBoundingRect
    {
        protected override string MarkupStyle => MarkupRedlineJsonBuilder.HatchStyle;
        protected override string CommandTitle =>
            UiLocalizationService.Current.GetString("BoundingHatchTitle");
        protected override string ViewpointNamePrefix =>
            UiLocalizationService.Current.GetString("BoundingHatchViewpointPrefix");
        protected override string LogCommandTitle => "Заштриховать габарит";

        public override int Execute(params string[] parameters)
        {
            return base.Execute(parameters);
        }
    }
}
