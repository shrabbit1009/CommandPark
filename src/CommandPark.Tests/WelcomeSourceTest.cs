using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(WelcomeSource))]
public class WelcomeSourceTest
{
    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsWelcomeItems()
    {
        var source = new WelcomeSource();

        var hints = await source.SearchAsync(string.Empty).ToListAsync();

        Assert.Equal(4, hints.Count);
        Assert.Contains(hints, hint => hint.Id == "welcome:changeConfigFile");
        Assert.Contains(hints, hint => hint.Id == "welcome:repo");
        Assert.Contains(hints, hint => hint.Id == "welcome:official");
        Assert.Contains(hints, hint => hint.Id == "welcome:version");
    }

    [Fact]
    public async Task SearchAsync_WithQuery_ReturnsNothing()
    {
        var source = new WelcomeSource();

        Assert.Empty(await source.SearchAsync("query").ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WhenBackgroundUnavailable_AppendsHintLast()
    {
        BackgroundStatus.UnavailableReason = "测试：后台热键不可用";
        try
        {
            var source = new WelcomeSource();

            var hints = await source.SearchAsync(string.Empty).ToListAsync();

            Assert.Equal(5, hints.Count);
            Assert.Equal("welcome:backgroundUnavailable", hints[^1].Id);
            Assert.Contains("测试：后台热键不可用", hints[^1].Description);
        }
        finally
        {
            BackgroundStatus.UnavailableReason = null;
        }
    }

    [Fact]
    public async Task SearchAsync_WhenBackgroundAvailable_HasNoBackgroundHint()
    {
        BackgroundStatus.UnavailableReason = null;
        var source = new WelcomeSource();

        var hints = await source.SearchAsync(string.Empty).ToListAsync();

        Assert.DoesNotContain(hints, hint => hint.Id == "welcome:backgroundUnavailable");
        Assert.Equal("welcome:version", hints[^1].Id);
    }

    [Fact]
    public void ReadConfig_AppliesWelcomeSettings()
    {
        var source = new WelcomeSource();
        var json = JsonNode.Parse(
            """
            {
              "sources": {
                "welcome": {
                  "showEditConfig": false,
                  "showRepo": false,
                  "showOfficial": true
                }
              }
            }
            """)!;

        Assert.True(source.ReadConfig(json));
        Assert.False(source.ShowEditConfig);
        Assert.False(source.ShowRepo);
        Assert.True(source.ShowOfficial);
    }

    [Fact]
    public void ReadConfig_WithoutWelcomeSection_ReturnsFalse()
    {
        var source = new WelcomeSource();

        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }
}