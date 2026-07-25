using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;

namespace NavisHelper
{
    [Plugin("TopViewBoundingHatch", "CBC", DisplayName = "Заштриховать габарит")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class TopViewBoundingHatch : TopViewBoundingRect
    {
        protected override string MarkupStyle => MarkupRedlineJsonBuilder.HatchStyle;
        protected override string CommandTitle => "Заштриховать габарит";
        protected override string ViewpointNamePrefix => "Штриховка";

        public override int Execute(params string[] parameters)
        {
            return base.Execute(parameters);
        }
    }
}
