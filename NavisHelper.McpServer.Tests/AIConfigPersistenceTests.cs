using NavisHelper.AI;
using Xunit;

namespace NavisHelper.McpServer.Tests;

[Collection("Blocking infrastructure")]
public sealed class AIConfigPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));

    public AIConfigPersistenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CoalescedAwaiters_ReceiveTheActualBatchFailure()
    {
        using var persistence = new FailingQueuedPersistence();
        var runtime = new AIConfigRuntime(new AIConfigSnapshot("provider/first", 0.7, 9), persistence);
        var first = runtime.PersistLatestAsync();
        await persistence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = runtime.PersistLatestAsync();
        var third = runtime.PersistLatestAsync();
        persistence.Release.Set();
        await first;

        var failures = await Task.WhenAll(
            Record.ExceptionAsync(() => second).AsTask(), Record.ExceptionAsync(() => third).AsTask());

        Assert.All(failures, failure => Assert.IsType<IOException>(failure));
    }

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

    [Fact]
    public void Load_LockedSettingsReturnDefaultsWithoutModifyingTheOriginal()
    {
        var path = Path.Combine(_directory, "ai_config.json");
        const string original = "{\"ModelName\":\"provider/original\",\"Temperature\":0.7,\"ColorScheme\":9}";
        File.WriteAllText(path, original);
        var persistence = new AIConfigFilePersistence(path);
        var defaults = new AIConfigSnapshot("", 0.3, 8);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Same(defaults, persistence.Load(defaults));

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal("provider/original", persistence.Load(defaults).ModelName);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_MissingSettingsIsReadOnly()
    {
        var path = Path.Combine(_directory, "missing", "ai_config.json");
        var defaults = new AIConfigSnapshot("", 0.3, 8);

        Assert.Same(defaults, new AIConfigFilePersistence(path).Load(defaults));
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class FailingQueuedPersistence : IAIConfigSnapshotPersistence, IDisposable
    {
        private int _calls;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();

        public void Save(AIConfigSnapshot snapshot)
        {
            if (Interlocked.Increment(ref _calls) != 1)
                throw new IOException("Synthetic queued persistence failure.");
            Entered.TrySetResult(true);
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the first write.");
        }

        public void Dispose()
        {
            Release.Set();
            // A failed assertion may leave Save still inside Wait; do not dispose its gate.
        }
    }
}
