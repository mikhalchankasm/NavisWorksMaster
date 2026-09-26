using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// Host commands over the overlay world-marker store. Set and Manage plan through
    /// <see cref="WorldMarkerOverlayPlanner"/> against the store's snapshot for the active
    /// document: a dry run returns the plan unchanged, and only Apply = true swaps the store
    /// by calling <see cref="WorldMarkerOverlayStore.Update(Document, WorldMarkerOverlaySnapshot)"/>.
    /// A refused plan (cap, blank selectors) is reported, not thrown, in both modes. List is
    /// read-only. The commands only touch the overlay store: no model traversal, no selection,
    /// no document mutation, no files.
    /// </summary>
    internal sealed class WorldMarkerOverlayService
    {
        public WorldMarkerOverlaySetResponse Set(Document document, WorldMarkerOverlaySetRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            WorldMarkerOverlaySnapshot next;
            WorldMarkerOverlaySetResult plan;
            try
            {
                var planned = WorldMarkerOverlayPlanner.Set(EffectiveSnapshot(document), request);
                next = planned.Snapshot;
                plan = planned.Result;
            }
            catch (ArgumentException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
            }

            var response = new WorldMarkerOverlaySetResponse { Apply = request.Apply == true, Result = plan };
            if (response.Apply && plan.Accepted)
            {
                WorldMarkerOverlayStore.Update(document, next);
                response.Applied = true;
            }

            return response;
        }

        public WorldMarkerOverlayManageResponse Manage(Document document, WorldMarkerOverlayManageRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            WorldMarkerOverlaySnapshot next;
            WorldMarkerOverlayManageResult plan;
            try
            {
                var planned = WorldMarkerOverlayPlanner.Manage(EffectiveSnapshot(document), request);
                next = planned.Snapshot;
                plan = planned.Result;
            }
            catch (ArgumentException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
            }

            var response = new WorldMarkerOverlayManageResponse { Apply = request.Apply == true, Result = plan };
            if (response.Apply && plan.Accepted)
            {
                WorldMarkerOverlayStore.Update(document, next);
                response.Applied = true;
            }

            return response;
        }

        public WorldMarkerOverlayListResponse List(Document document, WorldMarkerOverlayListRequest request)
        {
            var filter = request ?? new WorldMarkerOverlayListRequest();
            string groupFilter;
            try
            {
                groupFilter = WorldMarkerOverlayPlanner.NormalizeGroup(filter.Group);
            }
            catch (ArgumentException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
            }

            var nameKeys = CollectNameKeys(filter.Names);
            var frame = WorldMarkerOverlayStore.Current;
            var boundToActive = document != null && ReferenceEquals(frame.Document, document);
            var snapshot = boundToActive ? frame.Snapshot : WorldMarkerOverlaySnapshot.Empty;

            var markers = new List<WorldMarkerOverlayListItem>(snapshot.Count);
            foreach (var marker in snapshot.Markers)
            {
                if (nameKeys.Count > 0 && !nameKeys.Contains(marker.MarkerId))
                    continue;
                if (groupFilter != null && marker.Group != groupFilter)
                    continue;
                markers.Add(ToListItem(marker));
            }

            var diagnostics = WorldMarkerOverlayStore.GetDiagnostics();
            return new WorldMarkerOverlayListResponse
            {
                Markers = markers,
                MarkerCount = markers.Count,
                DocumentFileName = DescribeDocument(frame.Document),
                BoundToActiveDocument = boundToActive,
                Version = diagnostics.Version,
                StoredMarkerCount = diagnostics.StoredMarkerCount,
                VisibleMarkerCount = diagnostics.VisibleMarkerCount,
                OverlayRenderCount = diagnostics.OverlayRenderCount,
                RenderBoundsCount = diagnostics.RenderBoundsCount,
                LastDrawMarkerCount = diagnostics.LastDrawMarkerCount,
                LastDrawUtc = diagnostics.LastDrawUtc,
                LastError = diagnostics.LastError,
                Persistent = false,
            };
        }

        private static WorldMarkerOverlaySnapshot EffectiveSnapshot(Document document)
        {
            var frame = WorldMarkerOverlayStore.Current;
            return ReferenceEquals(frame.Document, document) ? frame.Snapshot : WorldMarkerOverlaySnapshot.Empty;
        }

        private static HashSet<string> CollectNameKeys(IEnumerable<string> names)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names == null)
                return keys;

            foreach (var value in names)
            {
                var normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
                if (normalized.Length == 0)
                    continue;
                keys.Add(WorldMarkerInputPolicy.CreateMarkerId(normalized));
            }

            return keys;
        }

        private static WorldMarkerOverlayListItem ToListItem(WorldMarkerOverlayMarker marker)
        {
            return new WorldMarkerOverlayListItem
            {
                Id = marker.MarkerId,
                Name = marker.Name,
                X = marker.X,
                Y = marker.Y,
                Z = marker.Z,
                Style = marker.Style,
                SizePx = marker.SizePx,
                WorldSize = marker.WorldSize,
                Color = marker.Color,
                Alpha = marker.Alpha,
                Label = marker.Label,
                PoleEnabled = marker.PoleEnabled,
                PoleBaseZ = marker.PoleBaseZ,
                PoleTopZ = marker.PoleTopZ,
                Group = marker.Group,
                Visible = marker.Visible,
            };
        }

        private static string DescribeDocument(Document document)
        {
            return document == null ? string.Empty : document.FileName ?? string.Empty;
        }
    }
}
