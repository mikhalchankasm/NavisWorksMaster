using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed class ItemTextureMappingService
    {
        public GetItemTextureMappingResponse Read(Document document, GetItemTextureMappingRequest request,
            MatchSessionStore sessionStore)
        {
            request = request ?? new GetItemTextureMappingRequest();
            var items = ResolveItems(request.MatchHandles, sessionStore);
            var maxItems = TextureMappingRules.ClampMaxItems(request.MaxItems);
            var response = new GetItemTextureMappingResponse
            {
                ItemCount = items.Count,
                Truncated = items.Count > maxItems,
            };
            using (var savedSelection = new ModelItemCollection())
            {
                savedSelection.AddRange(document.CurrentSelection.SelectedItems);
                try
                {
                    foreach (var item in items.Take(maxItems))
                    {
                        var info = new ItemTextureMapping();
                        response.Items.Add(info);
                        try
                        {
                            info.DisplayName = item.DisplayName;
                            info.Path = SearchService.PrintItemPath(item);
                            using (var selected = new ModelItemCollection())
                            {
                                selected.Add(item);
                                document.CurrentSelection.CopyFrom(selected);
                            }
                            ReadSelected(info);
                            response.ReadCount++;
                        }
                        catch (Exception exception)
                        {
                            info.Error = exception.Message;
                        }
                    }
                }
                finally
                {
                    try
                    {
                        document.CurrentSelection.CopyFrom(savedSelection);
                        response.SelectionRestored = true;
                    }
                    catch (Exception exception)
                    {
                        response.Warnings.Add("Could not restore selection: " + exception.Message);
                    }
                }
            }
            return response;
        }

        private static List<ModelItem> ResolveItems(IList<string> handles, MatchSessionStore sessionStore)
        {
            if (handles == null || !handles.Any(handle => !string.IsNullOrWhiteSpace(handle)))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Pass non-blank matchHandles.");

            var items = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();
            foreach (var handle in handles.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                IList<ModelItem> resolved;
                if (!sessionStore.TryGet(handle, out resolved) || resolved == null)
                    throw new AgentCommandException(ErrorCodes.StaleMatchReference,
                        "matchHandle is stale or was not found: " + handle + ". Re-run find_items and retry.");
                foreach (var item in resolved)
                    if (item != null && seen.Add(item))
                        items.Add(item);
            }
            return items;
        }

        private static void ReadSelected(ItemTextureMapping info)
        {
            var state = LcOpState.GetActiveInstance();
            info.Mapping = TextureMappingRules.MappingName(
                LcOpTextureSpaceElement.GetSelectedTextureSpaceMapping(state).ToString());
            info.HasTextureCoordinates = LcOpTextureSpaceElement.CheckSelectedHasTextureCoordinates(state);
            var space = LcOpTextureSpaceElement.GetSelectedTextureSpace(state);
            if (space == null)
                throw new InvalidOperationException("No texture space is available for this item.");

            info.Translation = Vector(space.GetTranslation());
            info.Scale = Vector(space.GetScale());
            var rotation = space.GetRotation();
            info.Rotation = new TextureRotation { A = rotation.A, B = rotation.B, C = rotation.C, D = rotation.D };

            var plane = space as LcOpTextureSpacePlane;
            var box = space as LcOpTextureSpaceBox;
            var cylinder = space as LcOpTextureSpaceCylinder;
            if (plane != null || box != null || cylinder != null)
            {
                var min = new Vector3D();
                var max = new Vector3D();
                if (plane != null)
                    plane.GetDomain(min, max);
                else if (box != null)
                    box.GetDomain(min, max);
                else
                    cylinder.GetDomain(min, max);
                info.DomainMin = Vector(min);
                info.DomainMax = Vector(max);
            }
            if (box != null)
            {
                var faces = new[]
                {
                    LcOpTextureSpaceAxisDirection.eDIRECTION_PLUSX, LcOpTextureSpaceAxisDirection.eDIRECTION_MINUSX,
                    LcOpTextureSpaceAxisDirection.eDIRECTION_PLUSY, LcOpTextureSpaceAxisDirection.eDIRECTION_MINUSY,
                    LcOpTextureSpaceAxisDirection.eDIRECTION_PLUSZ, LcOpTextureSpaceAxisDirection.eDIRECTION_MINUSZ,
                };
                foreach (var face in faces)
                {
                    LcOpTextureSpaceAxisDirection u = LcOpTextureSpaceAxisDirection.eDIRECTION_PLUSX, v = LcOpTextureSpaceAxisDirection.eDIRECTION_PLUSX;
                    box.GetDirection(face, ref u, ref v);
                    info.FaceDirections.Add(new TextureFaceDirection
                    {
                        Face = TextureMappingRules.AxisName(face.ToString()),
                        U = TextureMappingRules.AxisName(u.ToString()),
                        V = TextureMappingRules.AxisName(v.ToString()),
                    });
                }
            }
            if (cylinder != null)
            {
                info.CapEnabled = cylinder.GetEnableCap();
                info.CapThreshold = cylinder.GetCapThreshold();
            }
        }

        private static TextureVector Vector(Vector3D value)
        {
            return new TextureVector { X = value.X, Y = value.Y, Z = value.Z };
        }
    }
}
