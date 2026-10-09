using System.Text.Json.Nodes;
using CommandPark.Utils.Clicks;
using JetBrains.Annotations;

namespace CommandPark.Tests.Utils.Clicks;

[TestSubject(typeof(HintClickStore))]
public class HintClickStoreTest
{
    private static IHint Hint(string id, string? name = null, string? description = null) =>
        new Hint(id, name ?? id, description ?? id);

    [Fact]
    public void HashOf_IsStableForSameContent()
    {
        Assert.Equal(
            HintClickStore.HashOf(Hint("id", "name", "desc")),
            HintClickStore.HashOf(Hint("id", "name", "desc")));
    }

    [Fact]
    public void HashOf_ChangesWhenAnyFieldChanges()
    {
        var baseline = HintClickStore.HashOf(Hint("id", "name", "desc"));

        Assert.NotEqual(baseline, HintClickStore.HashOf(Hint("other", "name", "desc")));
        Assert.NotEqual(baseline, HintClickStore.HashOf(Hint("id", "other", "desc")));
        Assert.NotEqual(baseline, HintClickStore.HashOf(Hint("id", "name", "other")));
    }

    [Fact]
    public void HashOf_FieldBoundariesDoNotCollide()
    {
        // 没有分隔符时 ("ab", "c") 与 ("a", "bc") 会拼成同一个字符串；这里必须区分。
        Assert.NotEqual(
            HintClickStore.HashOf("ab", "c", ""),
            HintClickStore.HashOf("a", "bc", ""));
    }

    [Fact]
    public void RecordClick_IncrementsPerHint()
    {
        var store = new HintClickStore();
        var hint  = Hint("app");

        store.RecordClick(hint);
        store.RecordClick(hint);
        store.RecordClick(Hint("other"));

        Assert.Equal(2, store.GetClickCount(hint));
        Assert.Equal(1, store.GetClickCount(Hint("other")));
        Assert.Equal(0, store.GetClickCount(Hint("never-clicked")));
    }

    [Fact]
    public void GetWeight_ScalesWithClicks_AndCapsAtMax()
    {
        var store = new HintClickStore();
        var hint  = Hint("app");

        Assert.Equal(0f, store.GetWeight(hint));

        for (var i = 1; i <= 5; i++)
        {
            store.RecordClick(hint);
            Assert.Equal(HintClickStore.BoostPerClick * i, store.GetWeight(hint), precision: 5);
        }

        for (var i = 0; i < 20; i++)
        {
            store.RecordClick(hint);
        }

        Assert.Equal(HintClickStore.MaxBoost, store.GetWeight(hint), precision: 5);
    }

    [Fact]
    public void KeyedByContent_NotByIdentity()
    {
        // 同一条命令在不同批次里会是不同的对象，但内容相同就应命中同一份点击记录。
        var store = new HintClickStore();

        store.RecordClick(Hint("app", "App", "Launch the app"));

        Assert.Equal(1, store.GetClickCount(Hint("app", "App", "Launch the app")));
    }

    [Fact]
    public async Task SaveLoad_RoundTrips()
    {
        var store = new HintClickStore();
        store.RecordClick(Hint("a"));
        store.RecordClick(Hint("a"));
        store.RecordClick(Hint("b"));

        var stream = new MemoryStream();
        await store.SaveAsync(stream);
        stream.Position = 0;

        var loaded = await HintClickStore.LoadAsync(stream);

        Assert.Equal(2, loaded.GetClickCount(Hint("a")));
        Assert.Equal(1, loaded.GetClickCount(Hint("b")));
    }

    [Fact]
    public void MaxEntries_EvictsOldest()
    {
        var store = new HintClickStore { MaxEntries = 2 };

        store.RecordClick(Hint("a"));
        store.RecordClick(Hint("b"));
        store.RecordClick(Hint("c"));

        Assert.Equal(2, store.Count);
        Assert.Equal(0, store.GetClickCount(Hint("a")));
        Assert.Equal(1, store.GetClickCount(Hint("c")));
    }

    // ------------------------------------------------------------------
    // 配置文件与文件读写
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null, HintClickConfig.DefaultMaxEntries)]
    [InlineData("""{"clicks":{}}""", HintClickConfig.DefaultMaxEntries)]
    [InlineData("""{"clicks":{"maxEntries":42}}""", 42)]
    [InlineData("""{"clicks":{"maxEntries":0}}""", HintClickConfig.MinMaxEntries)]
    [InlineData("""{"clicks":{"maxEntries":-5}}""", HintClickConfig.MinMaxEntries)]
    [InlineData("""{"clicks":{"maxEntries":99999999}}""", HintClickConfig.MaxMaxEntries)]
    [InlineData("""{"clicks":{"maxEntries":"oops"}}""", HintClickConfig.DefaultMaxEntries)]
    public void Config_MaxEntries_IsReadAndClamped(string? json, int expected)
    {
        var node = json is null ? null : JsonNode.Parse(json);

        Assert.Equal(expected, HintClickConfig.ReadMaxEntries(node));
    }

    [Fact]
    public void StoreFile_RoundTripsThroughDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "commandpark-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, HintClickStoreFile.FileName);

        try
        {
            var store = new HintClickStore();
            store.RecordClick(Hint("disk"));
            HintClickStoreFile.Save(store, path);

            Assert.True(File.Exists(path));

            var loaded = HintClickStoreFile.Load(path);

            Assert.Equal(1, loaded.GetClickCount(Hint("disk")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StoreFile_MissingFile_ReturnsEmptyStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "commandpark-tests", $"{Guid.NewGuid():N}.htt");

        var store = HintClickStoreFile.Load(path);

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void StoreFile_CorruptFile_DoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "commandpark-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, HintClickStoreFile.FileName);

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, [1, 2, 3]);

            var store = HintClickStoreFile.Load(path);

            Assert.Equal(0, store.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
