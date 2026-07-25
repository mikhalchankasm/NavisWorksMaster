using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    private const int DefaultTimeoutMs = 60000;

    private const int HostTimeoutMarginMs = 5000;

    private const int HostBusyRetryDelayBaseMs = 150;

    private const int HostBusyRetryDelayMaxMs = 750;

    private const int MaxFrameLengthBytes = ProtocolConstants.MaxFrameLengthBytes;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly McpCallLogger _callLogger;

    public HostBridgeClient(McpCallLogger callLogger)
    {
        _callLogger = callLogger;
    }

    public static string McpServerVersion
    {
        get
        {
            var version = typeof(HostBridgeClient).Assembly.GetName().Version;
            return version == null ? string.Empty : version.ToString();
        }
    }
}

internal sealed class HostCallException : Exception
{
    public HostCallException(string errorCode, string message)
        : base((errorCode ?? ErrorCodes.CommandFailed) + ": " + (message ?? string.Empty))
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? ErrorCodes.CommandFailed : errorCode;
    }

    public HostCallException(string errorCode, string message, Exception innerException)
        : base((errorCode ?? ErrorCodes.CommandFailed) + ": " + (message ?? string.Empty), innerException)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? ErrorCodes.CommandFailed : errorCode;
    }

    public string ErrorCode { get; }
}
