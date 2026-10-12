using System;

namespace NavisHelper.Agent.Services
{
    // SDK-independent, source-linked into the test project. #263: a save under the same
    // key keeps the protected-mode instance; every other trigger resets it.
    internal enum DocumentResetTrigger
    {
        ActiveDocumentChanged,
        FileNameChanged,
        Closed
    }

    internal static class DocumentInstanceTracker
    {
        internal static bool ShouldReset(string previousKey, string newKey, DocumentResetTrigger trigger)
        {
            return trigger != DocumentResetTrigger.FileNameChanged
                || !string.Equals(previousKey, newKey, StringComparison.Ordinal);
        }

        internal static string LogLine(string previousKey, string newKey, DocumentResetTrigger trigger)
        {
            var keyChanged = !string.Equals(previousKey, newKey, StringComparison.Ordinal);
            var reset = ShouldReset(previousKey, newKey, trigger);
            return "document_instance trigger=" + trigger
                + " key_changed=" + (keyChanged ? "true" : "false")
                + " reset=" + (reset ? "true" : "false");
        }
    }
}
