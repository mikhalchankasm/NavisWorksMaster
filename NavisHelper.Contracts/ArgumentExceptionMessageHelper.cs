using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Removes the runtime-localized parameter-identification suffix that the
    /// running .NET runtime appends to an <see cref="ArgumentException"/>
    /// message, and the trailing actual-value line the runtime appends to an
    /// <see cref="ArgumentOutOfRangeException"/> built with an actual value.
    /// The appended text differs between runtimes and UI languages, so it is
    /// derived from probe exceptions built at call time rather than
    /// hard-coded: each probe's message is a marker that cannot occur in a
    /// real error, so whatever the runtime appends after the marker is the
    /// suffix shape, and a candidate message is stripped only when it ends
    /// with that shape and the segment between the lead and the tail is a
    /// single parameter identifier. Messages that match neither shape are
    /// returned unchanged.
    /// </summary>
    public static class ArgumentExceptionMessageHelper
    {
        private const string ProbeMessage = "\u0001NavisHelper.ArgumentExceptionMessageHelper.probe.message\u0001";
        private const string ProbeParameterName = "\u0002NavisHelper.ArgumentExceptionMessageHelper.probe.name\u0002";
        private const string ProbeActualValue = "\u0003NavisHelper.ArgumentExceptionMessageHelper.probe.value\u0003";

        /// <summary>
        /// Returns <paramref name="message"/> without the parameter-identification
        /// suffix the running runtime appends to an <see cref="ArgumentException"/>
        /// message, without the appended parameter and actual-value text of an
        /// <see cref="ArgumentOutOfRangeException"/> carrying an actual value, or
        /// unchanged when the message matches neither shape.
        /// </summary>
        public static string StripParameterNameSuffix(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            string lead;
            string tail;
            if (TryDeriveSuffixShape(out lead, out tail))
            {
                var cutAt = FindSuffixStart(message, lead, tail);
                if (cutAt >= 0)
                    return message.Substring(0, cutAt);
            }

            string valueLead;
            string valueTail;
            if (TryDeriveRangeSuffixShape(out lead, out valueLead, out valueTail))
            {
                var rangeCutAt = FindRangeSuffixStart(message, lead, valueLead, valueTail);
                if (rangeCutAt >= 0)
                    return message.Substring(0, rangeCutAt);
            }

            return message;
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

        private static bool TryDeriveRangeSuffixShape(out string lead, out string valueLead, out string valueTail)
        {
            lead = null;
            valueLead = null;
            valueTail = null;

            var probe = new ArgumentOutOfRangeException(ProbeParameterName, ProbeActualValue, ProbeMessage).Message;
            if (probe == null || !probe.StartsWith(ProbeMessage, StringComparison.Ordinal))
                return false;

            var appended = probe.Substring(ProbeMessage.Length);
            var parameterAt = appended.IndexOf(ProbeParameterName, StringComparison.Ordinal);
            if (parameterAt < 0)
                return false;

            var valueAt = appended.IndexOf(ProbeActualValue, parameterAt + ProbeParameterName.Length, StringComparison.Ordinal);
            if (valueAt < 0)
                return false;

            lead = appended.Substring(0, parameterAt);
            valueLead = appended.Substring(parameterAt + ProbeParameterName.Length, valueAt - parameterAt - ProbeParameterName.Length);
            valueTail = appended.Substring(valueAt + ProbeActualValue.Length);
            return lead.Length > 0 || valueLead.Length > 0 || valueTail.Length > 0;
        }

        private static int FindSuffixStart(string message, string lead, string tail)
        {
            if (tail.Length > 0 && !message.EndsWith(tail, StringComparison.Ordinal))
                return -1;

            var leadAt = message.LastIndexOf(lead, StringComparison.Ordinal);
            if (leadAt < 0)
                return -1;

            var parameterStart = leadAt + lead.Length;
            var parameterLength = message.Length - tail.Length - parameterStart;
            if (parameterLength <= 0 || !IsParameterIdentifier(message, parameterStart, parameterLength))
                return -1;

            return leadAt;
        }

        private static int FindRangeSuffixStart(string message, string lead, string valueLead, string valueTail)
        {
            if (valueTail.Length > 0 && !message.EndsWith(valueTail, StringComparison.Ordinal))
                return -1;

            var leadAt = message.LastIndexOf(lead, StringComparison.Ordinal);
            if (leadAt < 0)
                return -1;

            var parameterStart = leadAt + lead.Length;
            var valueLeadAt = message.IndexOf(valueLead, parameterStart, StringComparison.Ordinal);
            if (valueLeadAt < 0)
                return -1;

            var parameterLength = valueLeadAt - parameterStart;
            if (parameterLength <= 0 || !IsParameterIdentifier(message, parameterStart, parameterLength))
                return -1;

            var valueStart = valueLeadAt + valueLead.Length;
            var valueLength = message.Length - valueTail.Length - valueStart;
            if (valueLength < 0)
                return -1;

            return leadAt;
        }

        private static bool IsParameterIdentifier(string message, int start, int length)
        {
            for (var i = start; i < start + length; i++)
            {
                var c = message[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                    return false;
            }

            return true;
        }
    }
}
