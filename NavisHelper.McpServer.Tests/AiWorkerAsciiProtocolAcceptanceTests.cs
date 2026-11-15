using NavisHelper.AI;
using Newtonsoft.Json;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #463. The plugin writes the worker's stdin in the ANSI code page
// (Roamer has no console) and the worker reads and writes in its own console code page, so any
// character outside ASCII may be replaced on the way ("Труба Ø57" arrived as "Труба ?57").
// Both directions therefore carry ASCII only, with every other character escaped as \uXXXX.
public sealed class AiWorkerAsciiProtocolAcceptanceTests
{
    private static readonly string[] Names = { "Стена", "Труба Ø57", "Балка 2×3 м²" };
    private const string Scheme = "Архитектурная схема";

    [Fact]
    public void TheRequestCarriesOnlyAsciiAndRoundTrips()
    {
        var request = AiWorkerRequestEnvelope.Create(
            "req-463",
            AiWorkerOperation.GetColors,
            new AiWorkerRequestPayload { ObjectNames = Names.ToList(), SchemeName = Scheme, ModelId = "vendor/model" });

        var json = AiWorkerTransport.SerializeRequest(request);

        AssertAscii(json);
        var back = JsonConvert.DeserializeObject<AiWorkerRequestEnvelope>(json);
        Assert.Equal(Names, back!.Payload.ObjectNames);
        Assert.Equal(Scheme, back.Payload.SchemeName);
    }

    [Fact]
    public void TheResponseCarriesOnlyAsciiAndRoundTrips()
    {
        var response = AiWorkerResponseEnvelope.Success(AiWorkerRequestEnvelope.Create(
            "req-463", AiWorkerOperation.GetColors, new AiWorkerRequestPayload()));
        response.Colors = Names.ToDictionary(name => name, _ => "#FF8800");
        response.FinishReason = "stop";

        var json = AiWorkerProtocol.SerializeResponse(response);

        AssertAscii(json);
        Assert.DoesNotContain("\"HttpStatus\"", json, StringComparison.Ordinal);
        var back = JsonConvert.DeserializeObject<AiWorkerResponseEnvelope>(json);
        Assert.Equal(Names, back!.Colors.Keys.ToArray());
        Assert.Equal("req-463", back.RequestId);
    }

    // The worker must write its response through the shared serializer, not its own settings.
    [Fact]
    public void TheWorkerWritesItsResponseThroughTheSharedSerializer()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper.AiWorker", "Program.cs"));

        Assert.Contains("Console.Out.WriteLine(AiWorkerProtocol.SerializeResponse(response))", program, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonConvert.SerializeObject(", program, StringComparison.Ordinal);
    }

    private static void AssertAscii(string json)
    {
        var wide = json.Where(c => c >= 0x80).Select(c => ((int)c).ToString("X4")).ToArray();
        Assert.True(wide.Length == 0, "non-ASCII characters: " + string.Join(",", wide));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
