using CommandPark.Strings;
using JetBrains.Annotations;

namespace CommandPark.Tests.Strings;

[TestSubject(typeof(MultiStringCollection))]
public class MultiStringCollectionTest
{
    private static StringCollection Provider(string json, string name) => StringCollection.Parse(json, name);

    private static MultiStringCollection TwoLanguages() =>
        new(
        [
            Provider("""{"ui":{"title":"English","send":"Send ({0})"}}""", "en_US"),
            Provider("""{"ui":{"title":"中文","only":"只有中文"}}""", "zh_CN"),
        ]);

    [Fact]
    public void GetString_FirstProviderWins()
    {
        Assert.Equal("English", TwoLanguages().GetString("ui.title"));
    }

    [Fact]
    public void GetString_FallsBackToLaterProvider()
    {
        Assert.Equal("只有中文", TwoLanguages().GetString("ui.only"));
    }

    [Fact]
    public void GetString_WhenNobodyHasTheKey_ReturnsPath()
    {
        Assert.Equal("ui.missing", TwoLanguages().GetString("ui.missing"));
        Assert.Equal("ui.missing", new MultiStringCollection().GetString("ui.missing"));
    }

    [Fact]
    public void GetString_FallsThroughNonStringValuesToLaterProviders()
    {
        var chain = new MultiStringCollection()
            .Add(Provider("""{"ui":{"title":123}}""", "broken"))
            .Add(Provider("""{"ui":{"title":"中文"}}""", "zh_CN"));

        Assert.Equal("中文", chain.GetString("ui.title"));
    }

    [Fact]
    public void TryGetString_ReportsHitAndMiss()
    {
        var chain = TwoLanguages();

        Assert.True(chain.TryGetString("ui.only", out var value));
        Assert.Equal("只有中文", value);

        Assert.False(chain.TryGetString("ui.missing", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Contains_MatchesTryGetString()
    {
        var chain = TwoLanguages();

        Assert.True(chain.Contains("ui.title"));
        Assert.False(chain.Contains("ui.missing"));
    }

    [Fact]
    public void GetFormat_UsesFirstHitAndFormatsIt()
    {
        Assert.Equal("Send (Alt)", TwoLanguages().GetFormat("ui.send", "Alt"));
    }

    [Fact]
    public void GetFormat_WhenMissing_ReturnsPath()
    {
        Assert.Equal("ui.missing", TwoLanguages().GetFormat("ui.missing", "Alt"));
    }

    [Fact]
    public void Chain_KeepsInsertionOrder()
    {
        var first = Provider("""{"a":"1"}""", "first");
        var second = Provider("""{"a":"2"}""", "second");
        var chain = new MultiStringCollection().Add(first).Add(second);

        Assert.Equal([first, second], chain.Chain);
        Assert.Equal(2, chain.Count);
        Assert.False(chain.IsEmpty);
    }

    [Fact]
    public void Constructor_AddsEveryProviderInOrder()
    {
        var first = Provider("""{"a":"1"}""", "first");
        var second = Provider("""{"a":"2"}""", "second");
        var third = Provider("""{"a":"3"}""", "third");
        var chain = new MultiStringCollection([first, second, third]);

        Assert.Equal([first, second, third], chain.Chain);
        Assert.Equal("1", chain.GetString("a"));
    }

    [Fact]
    public void EmptyChain_IsEmpty()
    {
        var chain = new MultiStringCollection();

        Assert.True(chain.IsEmpty);
        Assert.Equal(0, chain.Count);
        Assert.Empty(chain.Chain);
    }

    [Fact]
    public void Add_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiStringCollection().Add(null!));
        Assert.Throws<ArgumentNullException>(() => new MultiStringCollection().AddRange(null!));
    }

    [Fact]
    public void AddRange_AppendsToTheEnd()
    {
        var first = Provider("""{"a":"1"}""", "first");
        var second = Provider("""{"a":"2"}""", "second");
        var chain = new MultiStringCollection().Add(first).AddRange([second]);

        Assert.Equal([first, second], chain.Chain);
    }

    [Fact]
    public void NestedMultiCollection_IsSearchedInPlace()
    {
        var inner = new MultiStringCollection([Provider("""{"a":"inner"}""", "inner")]);
        var chain = new MultiStringCollection([Provider("""{"b":"outer"}""", "outer"), inner]);

        Assert.Equal("outer", chain.GetString("b"));
        Assert.Equal("inner", chain.GetString("a"));
    }
}