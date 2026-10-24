using Cothon.Core.Memory;
using Xunit;

public class MemoryStoreTests : IDisposable
{
    private readonly string _path;

    public MemoryStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"cothon-mem-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private MemoryStore Store(bool enabled = true) => new(enabled, _path);

    [Fact]
    public void Remember_then_Recall_round_trips_a_fact()
    {
        var store = Store();
        store.Remember("birthday", "may 14", "people");

        var fact = Assert.Single(store.Recall());
        Assert.Equal("birthday", fact.Key);
        Assert.Equal("may 14", fact.Value);
        Assert.Equal("people", fact.Category);
    }

    [Fact]
    public void Disabled_store_writes_nothing_and_recalls_nothing()
    {
        var store = Store(enabled: false);
        store.Remember("k", "v");
        store.EraseAll();

        Assert.Empty(store.Recall());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Secrets_are_redacted_before_disk()
    {
        var store = Store();
        store.Remember("creds", "password=hunter2 and token=abcdef123456");

        Assert.Contains("***REDACTED***", store.Recall().Single(f => f.Key == "creds").Value);
        var raw = File.ReadAllText(_path);
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("abcdef123456", raw);
    }

    [Fact]
    public void Forget_removes_only_the_named_key()
    {
        var store = Store();
        store.Remember("a", "1");
        store.Remember("b", "2");
        store.Forget("a");

        Assert.Null(store.RecallText("a"));
        Assert.Equal("b: 2", store.RecallText("b"));
    }

    [Fact]
    public async Task Corrupt_file_refuses_to_be_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "{ not valid json");
        var store = Store();

        Assert.Throws<InvalidOperationException>(() => store.Remember("k", "v"));
    }

    [Fact]
    public void EraseAll_clears_everything()
    {
        var store = Store();
        store.Remember("a", "1");
        store.Remember("b", "2");
        store.EraseAll();

        Assert.Empty(store.Recall());
    }
}