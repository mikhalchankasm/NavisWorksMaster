using System;
using NavisHelper.Core.Localization;

namespace NavisHelper.Agent.Services
{
    internal static class ProtectedModeLabels
    {
        internal static string SessionTag(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return string.Empty;
            return sessionId.Substring(0, Math.Min(6, sessionId.Length)).ToLowerInvariant();
        }

        internal static string ClientLabel(string clientName, string sessionId, string unknownClientText)
        {
            var client = string.IsNullOrWhiteSpace(clientName) ? unknownClientText : clientName;
            var tag = SessionTag(sessionId);
            return string.IsNullOrEmpty(tag) ? client : client + " [" + tag + "]";
        }

        internal static string RequestRow(UiLocalizationService localization, PendingApproval request,
            string documentName)
        {
            return localization.Format("Settings_ProtectedMode_RequestFormat",
                ClientLabel(request.ClientName, request.SessionId,
                    localization.GetString("Settings_ProtectedMode_UnknownClient")),
                documentName, request.Code);
        }

        internal static string GrantRow(UiLocalizationService localization, GrantedApproval grant,
            string documentName)
        {
            return localization.Format("Settings_ProtectedMode_GrantFormat",
                ClientLabel(grant.ClientName, grant.SessionId,
                    localization.GetString("Settings_ProtectedMode_UnknownClient")),
                documentName);
        }
    }
}
