using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisHelper.Agent.Services
{
    internal static class ClashWorkflowService
    {
        public const string IgnoreCommentPrefix = "[NavisHelper Ignore] ";

        public static IList<ClashResult> EnumerateResults(GroupItem parent)
        {
            var results = new List<ClashResult>();
            AddResults(parent, results);
            return results;
        }

        public static bool IsIgnoredResult(ClashResult result)
        {
            if (result == null || result.Comments == null)
                return false;

            try
            {
                return result.Comments.Any(comment =>
                    comment != null &&
                    (comment.Body ?? string.Empty).StartsWith(IgnoreCommentPrefix, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public static void ApplyResultUpdates(
            DocumentClashTests testsData,
            IEnumerable<ClashResult> results,
            ClashResultStatus status,
            string assignedTo,
            string comment)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");

            foreach (var result in (results ?? Enumerable.Empty<ClashResult>()).Where(item => item != null).Distinct())
            {
                EditStatus(testsData, result, status);
                if (!string.IsNullOrWhiteSpace(assignedTo))
                    EditAssignedTo(testsData, result, assignedTo.Trim());
                if (!string.IsNullOrWhiteSpace(comment))
                    AppendComment(testsData, result, comment.Trim());
            }
        }

        public static void ApplyMetadata(DocumentClashTests testsData, IEnumerable<ClashResult> results, string assignedTo, string comment)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");
            foreach (var result in (results ?? Enumerable.Empty<ClashResult>()).Where(item => item != null).Distinct())
            {
                if (!string.IsNullOrWhiteSpace(assignedTo))
                    EditAssignedTo(testsData, result, assignedTo.Trim());
                if (!string.IsNullOrWhiteSpace(comment))
                    AppendComment(testsData, result, comment.Trim());
            }
        }

        public static void AppendComment(DocumentClashTests testsData, ClashResult result, string body)
        {
            if (testsData == null || result == null || string.IsNullOrWhiteSpace(body))
                return;

            var comments = result.Comments == null
                ? new CommentCollection()
                : new CommentCollection(result.Comments);
            comments.Add(new Comment(body, CommentStatus.New));
            testsData.TestsEditResultComments(result, comments);
        }

        public static void EditStatus(DocumentClashTests testsData, ClashResult result, ClashResultStatus status)
        {
            var method = FindMutationMethod(testsData, "TestsEditResultStatus", 2, 3);
            var parameters = method.GetParameters();
            var arguments = parameters.Length == 2
                ? new object[] { result, status }
                : new object[] { result, status, CreateTextArgument(parameters[2].ParameterType, Environment.UserName ?? string.Empty) };
            InvokeMutation(testsData, method, arguments);
        }

        public static void EditAssignedTo(DocumentClashTests testsData, ClashResult result, string assignedTo)
        {
            var method = FindMutationMethod(testsData, "TestsEditResultAssignedTo", 2);
            var parameterType = method.GetParameters()[1].ParameterType;
            InvokeMutation(testsData, method, new[] { (object)result, CreateTextArgument(parameterType, assignedTo ?? string.Empty) });
        }

        private static MethodInfo FindMutationMethod(DocumentClashTests testsData, string name, params int[] parameterCounts)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");
            var method = testsData.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == name && parameterCounts.Contains(candidate.GetParameters().Length));
            if (method == null)
                throw new MissingMethodException(testsData.GetType().FullName, name);
            return method;
        }

        private static object CreateTextArgument(Type parameterType, string value)
        {
            if (parameterType == typeof(string))
                return value ?? string.Empty;
            return Activator.CreateInstance(parameterType, value ?? string.Empty);
        }

        private static void InvokeMutation(DocumentClashTests testsData, MethodInfo method, object[] arguments)
        {
            try
            {
                method.Invoke(testsData, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static void AddResults(GroupItem parent, ICollection<ClashResult> results)
        {
            if (parent == null || parent.Children == null)
                return;

            foreach (SavedItem child in parent.Children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    results.Add(result);
                    continue;
                }

                var group = child as GroupItem;
                if (group != null)
                    AddResults(group, results);
            }
        }
    }
}
