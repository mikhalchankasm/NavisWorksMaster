using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Removes the runtime-localized parameter-identification suffix that the
    /// running .NET runtime appends to an <see cref="ArgumentException"/>
    /// message. The suffix text differs between runtimes and UI languages, so
    /// it is derived from a probe exception built at call time rather than
    /// hard-coded: the probe's message is a marker that cannot occur in a real
    /// error, so whatever the runtime appends after the marker is the suffix
    /// shape, and a candidate message is stripped only when it ends with that
    /// shape. Messages that do not end with the suffix are returned unchanged.
    /// </summary>
    public static class ArgumentExceptionMessageHelper
    {
        private const string ProbeMessage = "\u0001NavisHelper.ArgumentExceptionMessageHelper.probe.message\u0001";
        private const string ProbeParameterName = "\u0002NavisHelper.ArgumentExceptionMessageHelper.probe.name\u0002";

        /// <summary>
        /// Returns <paramref name="message"/> without the parameter-identification
        /// suffix the running runtime appends to an <see cref="ArgumentException"/>
        /// message, or unchanged when the message does not end with that suffix.
        /// </summary>
        public static string StripParameterNameSuffix(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            string lead;
            string tail;
            if (!TryDeriveSuffixShape(out lead, out tail))
                return message;

            var cutAt = FindSuffixStart(message, lead, tail);
            return cutAt < 0 ? message : message.Substring(0, cutAt);
        }

        private static bool TryDeriveSuffixShape(out string lead, out string tail)
        {
            lead = null;
            tail = null;

            var probe = new ArgumentException(ProbeMessage, ProbeParameterName).Message;
            if (probe == null || !probe.StartsWith(ProbeMessage, StringComparison.Ordinal))
                return false;

            var appended = probe.Substring(ProbeMessage.Length);
            var parameterAt = appended.IndexOf(ProbeParameterName, StringComparison.Ordinal);
            if (parameterAt < 0)
                return false;

            lead = appended.Substring(0, parameterAt);
            tail = appended.Substring(parameterAt + ProbeParameterName.Length);
            return lead.Length > 0 || tail.Length > 0;
        }

        private static int FindSuffixStart(string message, string lead, string tail)
        {
            if (tail.Length > 0 && !message.EndsWith(tail, StringComparison.Ordinal))
                return -1;

            var leadAt = message.LastIndexOf(lead, StringComparison.Ordinal);
            if (leadAt < 0)
                return -1;

            // The segment between lead and tail is the parameter identifier; it must be non-empty.
            var parameterLength = message.Length - tail.Length - leadAt - lead.Length;
            return parameterLength > 0 ? leadAt : -1;
        }
    }
}
