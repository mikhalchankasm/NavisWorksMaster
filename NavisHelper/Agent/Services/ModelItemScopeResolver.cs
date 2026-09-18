using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal static class ModelItemScopeResolver
    {
        public static List<ModelItem> Resolve(Document document, string scope, IList<string> handles, MatchSessionStore store)
        {
            scope = string.IsNullOrWhiteSpace(scope) ? "current_selection" : scope.Trim().ToLowerInvariant();
            if (scope == "current_selection")
            {
                if (handles != null && handles.Count > 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "matchHandles requires scope=match_handle.");
                return document.CurrentSelection.SelectedItems.ToList();
            }
            if (scope != "match_handle" || handles == null || handles.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "scope must be current_selection or match_handle with nonempty matchHandles.");
            var result = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();
            foreach (var handle in handles)
            {
                IList<ModelItem> items;
                if (store == null || !store.TryGet(handle, out items))
                    throw new AgentCommandException(ErrorCodes.StaleMatchReference, MatchSessionStore.DescribeStale(handle));
                foreach (var item in items)
                    if (item != null && seen.Add(item)) result.Add(item);
            }
            return result;
        }

        public static string PathOf(ModelItem item)
        {
            var names = new Stack<string>();
            for (var current = item; current != null; current = current.Parent)
                names.Push(string.IsNullOrWhiteSpace(current.DisplayName) ? current.ClassDisplayName : current.DisplayName);
            return string.Join(" / ", names);
        }
    }
}
