using CommandPark;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(HintProvider))]
public class HintProviderTest
{

    [Fact]
    public async Task Test()
    {
        var query = "shrabbit";
        var compare = await new TestHintSource().SearchAsync(query).ToListAsync();
        
        var provider = new HintProvider
        {
            Sources = { new TestHintSource() }
        };

        var list = new List<IHint>();
        await provider.SearchAsync(query, h => BatchAdd(list, h), TimeSpan.FromMilliseconds(15));
        
        // 并发查询可能无法准确返回顺序
        Assert.Equivalent(compare, list);
    }

    [Fact]
    public async Task Test2()
    {
        var query   = "shrabbit";
        var compare = await new MockHintSource1().SearchAsync(query).ToListAsync();
        compare.AddRange(await new MockHintSource2().SearchAsync(query).ToListAsync());
        compare.AddRange(await new MockHintSource3().SearchAsync(query).ToListAsync());

        var provider = new HintProvider
        {
            Sources =
            {
                new MockHintSource1(),
                new MockHintSource2(),
                new MockHintSource3()
            }
        };

        var list = new List<IHint>();
        await provider.SearchAsync(query, h => BatchAdd(list, h), TimeSpan.FromMilliseconds(15));

        // 并发查询可能无法准确返回顺序
        Assert.Equivalent(compare, list);
    }

    private void BatchAdd<T>(List<T> list, T[] item)
    {
        foreach (var i in item)
        {
            list.Add(i);
        }
    }

    private static IHint GemHint(string db) => new Hint(db, db, db);
    
    public class MockHintSource1 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            yield return GemHint("a");
            yield return GemHint("b");
            yield return GemHint("c");
        }
    }

    public class MockHintSource2 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            yield return GemHint("d");
            yield return GemHint("e");
            yield return GemHint("f");
        }
    }

    public class MockHintSource3 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            yield return GemHint("g");
            yield return GemHint("h");
            yield return GemHint("i");
        }
    }
}