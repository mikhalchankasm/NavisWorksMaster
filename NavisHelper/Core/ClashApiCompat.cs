using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace NavisHelper.Core
{
    internal static class ClashApiCompat
    {
        public static IEnumerable<ClashTest> GetClashTests(DocumentClash clash)
        {
            var children = clash?.TestsData?.Value?.TestsRoot?.Children;
            if (children == null)
                return Enumerable.Empty<ClashTest>();

            return EnumerateClashTests(children.Cast<SavedItem>());
        }

        private static IEnumerable<ClashTest> EnumerateClashTests(IEnumerable<SavedItem> items)
        {
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var test = item as ClashTest;
                if (test != null)
                {
                    yield return test;
                    continue;
                }

                var group = item as GroupItem;
                if (group == null)
                    continue;

                foreach (var child in EnumerateClashTests(group.Children.Cast<SavedItem>()))
                    yield return child;
            }
        }

        public static void AddClashTestCopy(DocumentClashTests testsData, ClashTest test)
        {
            if (testsData == null || test == null)
                return;

            var oneArgument = testsData.GetType().GetMethod(
                "TestsAddCopy",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(SavedItem) },
                null);
            if (oneArgument != null)
            {
                oneArgument.Invoke(testsData, new object[] { test });
                return;
            }

            var testsRoot = testsData.Value == null ? null : testsData.Value.TestsRoot;
            var twoArguments = testsData.GetType().GetMethod(
                "TestsAddCopy",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(GroupItem), typeof(SavedItem) },
                null);
            if (twoArguments != null)
            {
                twoArguments.Invoke(testsData, new object[] { testsRoot, test });
                return;
            }

            throw new MissingMethodException("DocumentClashTests.TestsAddCopy");
        }
    }
}
