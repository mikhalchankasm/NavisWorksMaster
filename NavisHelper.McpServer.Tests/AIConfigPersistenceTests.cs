using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class AIConfigPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));

    public AIConfigPersistenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Save_PreservesPreviousBytesWhenAtomicReplaceIsDenied()
    {
        var path = Path.Combine(_directory, "ai_config.json");
        const string original = "{\"ModelName\":\"provider/original\",\"Temperature\":0.7,\"ColorScheme\":9}";
        File.WriteAllText(path, original);
        Exception error;
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            error = Record.Exception(() => new AIConfigFilePersistence(path).Save(new AIConfigSnapshot("provider/new", 0.3, 8)));

        Assert.Equal(original, File.ReadAllText(path));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task PersistLatest_ReportsFailureAndAllowsLaterRecovery()
    {
        var path = Path.Combine(_directory, "ai_config.json");
        File.WriteAllText(path, "{}");
        var runtime = new AIConfigRuntime(new AIConfigSnapshot("provider/first", 0.7, 9), new AIConfigFilePersistence(path));
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => runtime.PersistLatestAsync());
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal("{}", File.ReadAllText(path));

        runtime.UpdateModelName("provider/latest");
        await runtime.PersistLatestAsync();

        var saved = AIConfigJsonSerializer.Parse(File.ReadAllText(path), new AIConfigData());
        Assert.Equal("provider/latest", saved.ModelName);
        Assert.Equal(0.7, saved.Temperature);
        Assert.Equal(9, saved.ColorScheme);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_NewSettingsRoundTripWithoutByteOrderMarkOrScratchFiles()
    {
        var path = Path.Combine(_directory, "nested", "ai_config.json");
        new AIConfigFilePersistence(path).Save(new AIConfigSnapshot("provider/model-\u03b1", 0.5, 11));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'{', bytes[0]);
        var saved = AIConfigJsonSerializer.Parse(System.Text.Encoding.UTF8.GetString(bytes), new AIConfigData());
        Assert.Equal("provider/model-\u03b1", saved.ModelName);
        Assert.Equal(0.5, saved.Temperature);
        Assert.Equal(11, saved.ColorScheme);
        Assert.Single(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
