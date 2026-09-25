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
        
        Assert.Equivalent(compare, list);
    }

    private void BatchAdd<T>(List<T> list, T[] item)
    {
        foreach (var i in item)
        {
            list.Add(i);
        }
    }
}