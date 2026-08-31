namespace NavisHelper.Agent.Contracts
{
    public static class ProtocolConstants
    {
        public const string CurrentProtocolVersion = "1";
        public const int MaxFrameLengthBytes = 4 * 1024 * 1024;
        public const int HostTransportResponseMarginMilliseconds = 5000;
        public const int MaximumHostRequestTimeoutMilliseconds = 600000;
    }
}
