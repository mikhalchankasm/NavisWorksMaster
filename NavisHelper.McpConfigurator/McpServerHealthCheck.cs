using System.Diagnostics;
using System.Text.Json;

namespace NavisHelper.McpConfigurator;

internal static class McpServerHealthCheck
{
    public static async Task<bool> RunAsync(string serverPath, TimeSpan timeout)
    {
        using var process = new Process();
        try
        {
            using var cancellation = new CancellationTokenSource(timeout);
            var token = cancellation.Token;
            var dll = serverPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            process.StartInfo = new ProcessStartInfo(dll ? "dotnet" : serverPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            if (dll) process.StartInfo.ArgumentList.Add(serverPath);
            if (!process.Start()) return false;
            var drain = DrainErrorsAsync(process, token);
            try
            {
                await SendAsync(process, new
                {
                    jsonrpc = "2.0", id = 1, method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2024-11-05", capabilities = new { },
                        clientInfo = new { name = "NavisHelper.McpConfigurator", version = "1.0" },
                    },
                }, token);
                if (!await ReadResponseAsync(process, 1, false, token)) return false;
                await SendAsync(process, new { jsonrpc = "2.0", method = "notifications/initialized" }, token);
                await SendAsync(process, new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }, token);
                return await ReadResponseAsync(process, 2, true, token);
            }
            finally
            {
                cancellation.Cancel();
                await drain;
            }
        }
        catch { return false; }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* Process creation or termination may already have failed. */ }
        }
    }

    private static async Task SendAsync(Process process, object message, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private static async Task<bool> ReadResponseAsync(Process process, int id, bool requireTools, CancellationToken token)
    {
        while (await process.StandardOutput.ReadLineAsync(token) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var response = document.RootElement;
            if (!response.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var value) || value != id)
                continue;
            if (response.TryGetProperty("error", out _) || !response.TryGetProperty("result", out var result))
                return false;
            return !requireTools || (result.TryGetProperty("tools", out var tools) &&
                tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0);
        }
        return false;
    }

    private static async Task DrainErrorsAsync(Process process, CancellationToken token)
    {
        try { while (await process.StandardError.ReadLineAsync(token) != null) { } }
        catch { /* stderr is diagnostic only; cancellation ends the drain. */ }
    }
}
