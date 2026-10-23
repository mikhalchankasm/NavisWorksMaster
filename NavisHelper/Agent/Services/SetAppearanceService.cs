using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class SetAppearanceService
    {
        public SelectionSetsApplyAppearanceResponse Execute(Document document, SelectionSetsApplyAppearanceRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new SelectionSetsApplyAppearanceRequest();
            var error = SetAppearanceStepRules.DescribeSteps(request.Steps);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);

            var response = new SelectionSetsApplyAppearanceResponse
            {
                Apply = request.Apply == true,
                Message = "Set appearance dry-run; nothing changed.",
            };
            var targets = new List<ModelItemCollection>();
            foreach (var step in request.Steps)
            {
                var result = new SetAppearanceStepResult
                {
                    SetPath = step == null ? null : step.SetPath,
                    ColorHex = step == null ? null : step.ColorHex,
                    Transparency = step == null ? null : step.Transparency,
                    Hide = step != null && step.Hide == true,
                    Error = SetAppearanceStepRules.Describe(step),
                };
                response.Steps.Add(result);
                ModelItemCollection items = null;
                if (result.Error == null)
                {
                    try
                    {
                        if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                            throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets.");
                        var node = ResolveSet(document.SelectionSets.RootItem, step.SetPath);
                        result.Resolved = true;
                        items = DocumentCommandService.BuildSelectionSetItems(document, node.Item);
                        result.ItemCount = items.Count;
                    }
                    catch (Exception exception)
                    {
                        result.Error = exception.Message;
                        items = null;
                    }
                }
                targets.Add(items);
            }

            if (!response.Apply)
                return response;

            var hasTargets = targets.Exists(items => items != null && items.Count > 0);
            if (!hasTargets)
            {
                response.Message = "No non-empty valid selection or search sets to apply; nothing changed.";
                return response;
            }

            using (var tx = document.BeginTransaction("NavisHelper set appearance"))
            {
                for (var i = 0; i < response.Steps.Count; i++)
                {
                    var items = targets[i];
                    if (items == null || items.Count == 0)
                        continue;
                    var step = response.Steps[i];
                    double r, g, b;
                    // Override the saved set's own items, including containers, without expanding leaves.
                    if (SetAppearanceStepRules.TryParseColorHex(step.ColorHex, out r, out g, out b))
                        document.Models.OverridePermanentColor(items, new Color(r, g, b));
                    if (step.Transparency.HasValue)
                        document.Models.OverridePermanentTransparency(items, step.Transparency.Value / 100.0);
                    if (step.Hide)
                        document.Models.SetHidden(items, true);
                    response.AppliedStepCount++;
                }
                tx.Commit();
            }
            response.Applied = response.AppliedStepCount > 0;
            response.Message = "Permanent set appearance overrides applied as one undo step. Undo with Ctrl+Z or reset_appearance for color/transparency; hidden items need unhiding.";
            try
            {
                if (document.ActiveView != null)
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch (Exception exception)
            {
                response.Warnings.Add("Could not request redraw: " + exception.Message);
            }
            return response;
        }

        private static DocumentCommandService.SelectionSetNode ResolveSet(GroupItem root, string setPath)
        {
            var request = new SelectionSetsManageRequest { PathOrName = setPath };
            try
            {
                return DocumentCommandService.ResolveSelectionSetNode(root, request,
                    item => item is SelectionSet, "Selection or search set was not found.");
            }
            catch (AgentCommandException exception) when (exception.ErrorCode == ErrorCodes.SelectionSetNotFound)
            {
                // Resolve folders only to explain the refusal; never expand their child sets.
                DocumentCommandService.ResolveSelectionSetNode(root, request,
                    item => item is GroupItem, "Selection or search set was not found.");
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    setPath + " is a folder; name a selection or search set");
            }
        }
    }
}
