using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed class ItemAppearanceService
    {
        public ItemAppearanceResponse Read(Document document, ItemAppearanceRequest request, MatchSessionStore sessionStore)
        {
            request = request ?? new ItemAppearanceRequest();
            var items = ResolveGeometry(document, request.MatchHandles, request.UseSelection, sessionStore);
            var maxItems = ItemAppearanceRules.ClampMaxItems(request.MaxItems);
            var response = new ItemAppearanceResponse { ItemCount = items.Count, Truncated = items.Count > maxItems };
            foreach (var item in items)
            {
                var info = ReadAppearance(item);
                if (info.ColorOverridden || info.TransparencyOverridden)
                    response.OverriddenItemCount++;
                if (response.Items.Count < maxItems)
                {
                    info.DisplayName = item.DisplayName;
                    info.Path = SearchService.PrintItemPath(item);
                    response.Items.Add(info);
                }
            }
            return response;
        }

        public ResetAppearanceResponse Reset(Document document, ResetAppearanceRequest request, MatchSessionStore sessionStore)
        {
            request = request ?? new ResetAppearanceRequest();
            var items = ResolveGeometry(document, request.MatchHandles, request.UseSelection, sessionStore);
            var overridden = new ModelItemCollection();
            foreach (var item in items)
            {
                var info = ReadAppearance(item);
                if (info.ColorOverridden || info.TransparencyOverridden)
                    overridden.Add(item);
            }
            var response = new ResetAppearanceResponse
            {
                Apply = request.Apply == true,
                ItemCount = items.Count,
                OverriddenItemCount = overridden.Count,
                Message = "Reset appearance dry-run; nothing changed.",
            };
            if (!response.Apply)
                return response;
            if (items.Count == 0)
            {
                response.Message = "No geometry items to reset; nothing changed.";
                return response;
            }

            // Every item is reset, not only those counted as overridden: the count compares
            // values rounded for display, so a transparency of 0.4 % or a colour within one
            // byte would be missed, and resetting an item without an override changes nothing.
            var targets = new ModelItemCollection();
            targets.AddRange(items);
            using (var tx = document.BeginTransaction("NavisHelper reset appearance"))
            {
                document.Models.ResetPermanentMaterials(targets);
                tx.Commit();
            }
            response.ResetItemCount = items.Count;
            response.Message = "Appearance overrides reset to original materials as one undo step.";
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

        private static ItemAppearanceInfo ReadAppearance(ModelItem item)
        {
            var geometry = item.Geometry;
            var original = geometry.OriginalColor;
            var current = geometry.PermanentColor;
            var info = new ItemAppearanceInfo
            {
                OriginalColorHex = original == null ? null : ItemAppearanceRules.ToHex(original.R, original.G, original.B),
                OriginalTransparency = ItemAppearanceRules.ToPercent(geometry.OriginalTransparency),
                CurrentColorHex = current == null ? null : ItemAppearanceRules.ToHex(current.R, current.G, current.B),
                CurrentTransparency = ItemAppearanceRules.ToPercent(geometry.PermanentTransparency),
            };
            info.ColorOverridden = ItemAppearanceRules.ColorDiffers(info.OriginalColorHex, info.CurrentColorHex);
            info.TransparencyOverridden = info.OriginalTransparency != info.CurrentTransparency;
            return info;
        }

        private static List<ModelItem> ResolveGeometry(Document document, IList<string> handles, bool? useSelection,
            MatchSessionStore sessionStore)
        {
            var error = ItemAppearanceRules.DescribeTargetError(handles, useSelection);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);

            var targets = new List<ModelItem>();
            if (useSelection == true)
                targets.AddRange(document.CurrentSelection.SelectedItems);
            else
            {
                foreach (var handle in handles.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    IList<ModelItem> resolved;
                    if (!sessionStore.TryGet(handle, out resolved) || resolved == null)
                        throw new AgentCommandException(ErrorCodes.StaleMatchReference,
                            "matchHandle is stale or was not found: " + handle + ". Re-run find_items and retry.");
                    targets.AddRange(resolved);
                }
            }

            var result = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();
            foreach (var target in targets.Where(item => item != null).Distinct())
            {
                if (target.HasGeometry)
                {
                    if (seen.Add(target))
                        result.Add(target);
                }
                else
                {
                    foreach (var item in target.Descendants)
                        if (item.HasGeometry && seen.Add(item))
                            result.Add(item);
                }
            }
            return result;
        }
    }
}
