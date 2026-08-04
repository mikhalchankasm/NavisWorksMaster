namespace NavisHelper.AI
{
    internal enum AISettingsStatusVisual
    {
        Neutral = 0,
        Busy,
        Success,
        Error
    }

    /// <summary>
    /// Pure presentation state for the progressive OpenRouter settings card.
    /// It intentionally contains no key, network, or persistence behavior.
    /// </summary>
    internal sealed class AISettingsConnectionPresentation
    {
        private AISettingsConnectionPresentation(
            bool showConnectForm,
            bool showConnectedSummary,
            bool showModelBlock,
            bool connectEnabled,
            bool disconnectEnabled,
            bool refreshEnabled,
            string headlineResource,
            string detailResource,
            AISettingsStatusVisual statusVisual)
        {
            ShowConnectForm = showConnectForm;
            ShowConnectedSummary = showConnectedSummary;
            ShowModelBlock = showModelBlock;
            ConnectEnabled = connectEnabled;
            DisconnectEnabled = disconnectEnabled;
            RefreshEnabled = refreshEnabled;
            HeadlineResource = headlineResource;
            DetailResource = detailResource;
            StatusVisual = statusVisual;
        }

        internal bool ShowConnectForm { get; }
        internal bool ShowConnectedSummary { get; }
        internal bool ShowModelBlock { get; }
        internal bool ConnectEnabled { get; }
        internal bool DisconnectEnabled { get; }
        internal bool RefreshEnabled { get; }
        internal string HeadlineResource { get; }
        internal string DetailResource { get; }
        internal AISettingsStatusVisual StatusVisual { get; }

        internal static AISettingsConnectionPresentation Evaluate(
            AiConnectionDisplayState state,
            bool hasConnectedKey,
            bool catalogBusy)
        {
            var checking = state == AiConnectionDisplayState.Checking;
            var error = IsError(state);
            var missingKey = state == AiConnectionDisplayState.MissingKey;
            var headline = error
                ? "Settings_Ai_Status_Error_Compact"
                : checking
                    ? "Settings_Ai_Status_Connecting_Compact"
                    : hasConnectedKey
                        ? "Settings_Ai_Status_Connected_Compact"
                        : "Settings_Ai_Status_Disconnected";
            var detail = error || missingKey
                ? AiConnectionStatusMapper.ResourceKey(state)
                : string.Empty;
            var visual = checking
                ? AISettingsStatusVisual.Busy
                : error
                    ? AISettingsStatusVisual.Error
                    : hasConnectedKey
                        ? AISettingsStatusVisual.Success
                        : AISettingsStatusVisual.Neutral;

            return new AISettingsConnectionPresentation(
                !hasConnectedKey,
                hasConnectedKey,
                hasConnectedKey,
                !hasConnectedKey && !checking,
                hasConnectedKey && !checking,
                hasConnectedKey && !checking && !catalogBusy,
                headline,
                detail,
                visual);
        }

        private static bool IsError(AiConnectionDisplayState state)
        {
            switch (state)
            {
                case AiConnectionDisplayState.Disconnected:
                case AiConnectionDisplayState.Checking:
                case AiConnectionDisplayState.Connected:
                case AiConnectionDisplayState.Ready:
                case AiConnectionDisplayState.MissingKey:
                    return false;
                default:
                    return true;
            }
        }
    }

    internal static class AISettingsConnectInputPolicy
    {
        internal static bool MayStartConnection(string enteredKey)
        {
            return !string.IsNullOrWhiteSpace(enteredKey);
        }
    }
}
