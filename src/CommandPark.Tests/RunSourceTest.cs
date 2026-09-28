using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(RunSource))]
public class RunSourceTest
{
    [Theory]
    [InlineData("@notepad")]
    [InlineData("run:notepad")]
    [InlineData("r:notepad")]
    public async Task SearchAsync_WithPrefix_StripsPrefixAndBoostsResult(string query)
    {
        var source = new RunSource();

        var hints = await source.SearchAsync(query).ToListAsync();

        var hint = Assert.Single(hints);
        Assert.Equal("运行 notepad", hint.Name);
        Assert.Equal("以管理员身份运行输入的命令", hint.Description);
        Assert.Equal(1.0f, hint.Similarity);
    }

    [Fact]
    public async Task SearchAsync_WithoutPrefix_ReturnsFallbackCandidate()
    {
        var source = new RunSource { PrefixMode = false };

        var hint = Assert.Single(await source.SearchAsync("notepad").ToListAsync());

        Assert.Equal("运行 notepad", hint.Name);
        Assert.Equal(0.0f, hint.Similarity);
    }

    [Fact]
    public async Task SearchAsync_WithPrefixModeAndNoPrefix_ReturnsNothing()
    {
        var source = new RunSource { PrefixMode = true };

        Assert.Empty(await source.SearchAsync("notepad").ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("run:")]
    [InlineData("@")]
    [InlineData("r:")]
    public async Task SearchAsync_WithEmptyCommand_ReturnsNothing(string query)
    {
        var source = new RunSource();

        Assert.Empty(await source.SearchAsync(query).ToListAsync());
    }

    [Fact]
    public void ReadConfig_AppliesPrefixMode()
    {
        var source = new RunSource();
        var json = JsonNode.Parse("""{"sources":{"run":{"prefixMode":true}}}""")!;

        Assert.True(source.ReadConfig(json));
        Assert.True(source.PrefixMode);
    }

    [Fact]
    public void ReadConfig_WithoutRunSection_ReturnsFalse()
    {
        var source = new RunSource();

        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }
}