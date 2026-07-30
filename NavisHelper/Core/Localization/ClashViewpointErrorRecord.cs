using System;

namespace NavisHelper.Core.Localization
{
    internal sealed class ClashViewpointErrorRecord
    {
        private const int MaximumDisplayedMessageLength = 180;

        private ClashViewpointErrorRecord(
            int index,
            int total,
            string displayName,
            string rawExceptionMessage,
            UiStatusResourceDescriptor expectedFailure)
        {
            Index = index;
            Total = total;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "Conflict"
                : displayName;
            RawExceptionMessage = rawExceptionMessage;
            ExpectedFailure = expectedFailure;
        }

        internal int Index { get; }
        internal int Total { get; }
        internal string DisplayName { get; }
        internal string RawExceptionMessage { get; }
        internal UiStatusResourceDescriptor ExpectedFailure { get; }

        internal static ClashViewpointErrorRecord FromException(
            int index,
            int total,
            string displayName,
            Exception exception)
        {
            return new ClashViewpointErrorRecord(
                index,
                total,
                displayName,
                exception?.Message,
                null);
        }

        internal static ClashViewpointErrorRecord FromExpectedFailure(
            int index,
            int total,
            string displayName,
            UiStatusResourceDescriptor descriptor)
        {
            return new ClashViewpointErrorRecord(
                index,
                total,
                displayName,
                null,
                descriptor ?? throw new ArgumentNullException(nameof(descriptor)));
        }

        internal UiStatusResourceDescriptor ToStatusDescriptor()
        {
            object detail = ExpectedFailure == null
                ? RawExceptionMessage == null
                    ? (object)UiLocalizedArgument.FromResource("Panel_Common_UnknownError")
                    : TrimForStatus(RawExceptionMessage, MaximumDisplayedMessageLength)
                : ExpectedFailure.AsLocalizedArgument();

            return new UiStatusResourceDescriptor(
                "Panel_Clash_Viewpoints_ErrorRecord_Format",
                Index,
                Total,
                DisplayName,
                detail);
        }

        private static string TrimForStatus(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string clean = value.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (maxLength <= 3 || clean.Length <= maxLength)
                return clean;

            return clean.Substring(0, maxLength - 3).TrimEnd() + "...";
        }
    }
}
