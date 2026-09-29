using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(BingSearchSource))]
public class BingSearchSourceTest
{
    [Theory]
    [InlineData("b:hello world")]
    [InlineData("bs:hello world")]
    public async Task SearchAsync_WithPrefix_ReturnsDirectSearchAndSuggestions(string query)
    {
        var helper = new FakeSearchEngineHelper();
        var source = new BingSearchSource(helper);

        var hints = await source.SearchAsync(query).ToListAsync();

        Assert.Equal(3, hints.Count);
        Assert.Equal("bing:search", hints[0].Id);
        Assert.Equal("Bing 搜索：hello world", hints[0].Name);
        Assert.Equal("hello world", helper.LastKeyword);
        Assert.Contains(hints, hint => hint.Name == "hello world news");
        Assert.Contains(hints, hint => hint.Name == "hello bing");
    }

    [Fact]
    public async Task SearchAsync_WithoutPrefix_WhenPrefixModeTrue_ReturnsNothing()
    {
        var source = new BingSearchSource(new FakeSearchEngineHelper()) { PrefixMode = true };

        Assert.Empty(await source.SearchAsync("hello").ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WhenSuggestionRequestFails_StillReturnsDirectSearch()
    {
        var source = new BingSearchSource(new FailingSearchEngineHelper());

        var hint = Assert.Single(await source.SearchAsync("b:hello").ToListAsync());

        Assert.Equal("bing:search", hint.Id);
    }

    [Fact]
    public void ReadConfig_AppliesPrefixMode()
    {
        var source = new BingSearchSource(new FakeSearchEngineHelper());
        var json = JsonNode.Parse("""{"sources":{"bing":{"prefixMode":false}}}""")!;

        Assert.True(source.ReadConfig(json));
        Assert.False(source.PrefixMode);
    }

    [Fact]
    public void ReadConfig_WithoutBingSection_ReturnsFalse()
    {
        var source = new BingSearchSource(new FakeSearchEngineHelper());

        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }

    private sealed class FakeSearchEngineHelper : ISearchEngineHelper
    {
        public string? LastKeyword { get; private set; }

        public Task<string[]> GetSuggestedItems(string keyword, CancellationToken cancellationToken = default)
        {
            LastKeyword = keyword;
            return Task.FromResult(new[]
            {
                "hello world",
                "hello world news",
                "hello bing",
                "hello world",
            });
        }

        public string BuildSearchUrl(string keyword) =>
            $"https://www.bing.com/search?q={Uri.EscapeDataString(keyword)}";
    }

    private sealed class FailingSearchEngineHelper : ISearchEngineHelper
    {
        public Task<string[]> GetSuggestedItems(string keyword, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("suggest unavailable");

        public string BuildSearchUrl(string keyword) =>
            $"https://www.bing.com/search?q={Uri.EscapeDataString(keyword)}";
    }
}