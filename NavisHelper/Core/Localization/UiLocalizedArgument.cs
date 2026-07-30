using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiLocalizedArgument
    {
        private UiLocalizedArgument(string resourceKey, object[] arguments)
        {
            ResourceKey = resourceKey;
            Arguments = arguments ?? new object[0];
        }

        internal string ResourceKey { get; }
        internal object[] Arguments { get; }

        internal static UiLocalizedArgument FromResource(
            string resourceKey,
            params object[] arguments)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("A semantic resource key is required.", nameof(resourceKey));

            return new UiLocalizedArgument(resourceKey, arguments);
        }

        internal static object Join(
            string separator,
            IEnumerable<object> arguments)
        {
            return new UiJoinedArgument(
                separator ?? string.Empty,
                (arguments ?? Enumerable.Empty<object>()).ToArray());
        }

        internal static object[] Resolve(
            object[] arguments,
            Func<string, string> resourceResolver)
        {
            if (resourceResolver == null)
                throw new ArgumentNullException(nameof(resourceResolver));

            return (arguments ?? new object[0])
                .Select(argument =>
                {
                    var localized = argument as UiLocalizedArgument;
                    if (localized != null)
                        return (object)resourceResolver(localized.ResourceKey);

                    var joined = argument as UiJoinedArgument;
                    if (joined == null)
                        return argument;

                    return (object)string.Join(
                        joined.Separator,
                        Resolve(joined.Arguments, resourceResolver)
                            .Select(value => value?.ToString() ?? string.Empty));
                })
                .ToArray();
        }

        internal static object[] Resolve(
            object[] arguments,
            Func<string, object[], string> resourceFormatter)
        {
            if (resourceFormatter == null)
                throw new ArgumentNullException(nameof(resourceFormatter));

            return (arguments ?? new object[0])
                .Select(argument =>
                {
                    var localized = argument as UiLocalizedArgument;
                    if (localized != null)
                    {
                        object[] resolvedNested = Resolve(localized.Arguments, resourceFormatter);
                        return (object)resourceFormatter(localized.ResourceKey, resolvedNested);
                    }

                    var joined = argument as UiJoinedArgument;
                    if (joined == null)
                        return argument;

                    return (object)string.Join(
                        joined.Separator,
                        Resolve(joined.Arguments, resourceFormatter)
                            .Select(value => value?.ToString() ?? string.Empty));
                })
                .ToArray();
        }

        private sealed class UiJoinedArgument
        {
            internal UiJoinedArgument(string separator, object[] arguments)
            {
                Separator = separator;
                Arguments = arguments ?? new object[0];
            }

            internal string Separator { get; }
            internal object[] Arguments { get; }
        }
    }
}
