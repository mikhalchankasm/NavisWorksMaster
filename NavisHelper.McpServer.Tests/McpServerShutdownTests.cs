using System.Diagnostics;
using NavisHelper.McpServer.Resources;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// A stdio MCP server must exit promptly once its client closes stdin; otherwise
// every client shutdown waits for its own kill timeout. ModelContextProtocol 1.2
// exits at once; the 2.x upgrade must keep that.
[Collection("MCP stdio")]
public sealed class McpServerShutdownTests
{
    [Fact]
    public async Task ServerExitsSoonAfterStdinCloses()
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { typeof(McpToolCatalogResources).Assembly.Location },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            _ = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"shutdown-probe\",\"version\":\"1\"}}}".AsMemory(), TestContext.Current.CancellationToken);
            await process.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.Contains("\"id\":1", await process.StandardOutput.ReadLineAsync(timeout.Token));

            var closed = Stopwatch.StartNew();
            process.StandardInput.Close();
            using var exitWindow = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(exitWindow.Token);
            Assert.True(closed.Elapsed < TimeSpan.FromSeconds(3), "exit took " + closed.Elapsed);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
