using System.Text.Json.Nodes;
using CommandPark.Ai;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(AiConfig))]
public class AiConfigTest
{
    private static AiConfig Read(string json) =>
        Assert.IsType<AiConfig>(AiConfig.Read(JsonNode.Parse(json)));

    [Fact]
    public void Read_MissingSection_ReturnsNull()
    {
        Assert.Null(AiConfig.Read(null));
        Assert.Null(AiConfig.Read(JsonNode.Parse("""{"sources":{}}""")));
    }

    [Fact]
    public void Read_CustomSection_IsConfigured()
    {
        var config = Read(
            """{"ai":{"endpoint":"https://api.openai.com/v1","apiKey":"sk-123","model":"gpt-4o-mini"}}""");

        Assert.Equal(AiProvider.Custom, config.Provider);
        Assert.True(config.IsConfigured);
        Assert.True(config.RequiresApiKey);
        Assert.True(config.SendApiKey);
        Assert.Empty(config.MissingFields);
        Assert.Equal("https://api.openai.com/v1", config.Endpoint);
        Assert.Equal("sk-123", config.ApiKey);
        Assert.Equal("gpt-4o-mini", config.Model);
        Assert.Equal(AiConfig.DefaultMaxResults, config.MaxResults);
        Assert.Equal(AiConfig.DefaultTemperature, config.Temperature);
    }

    [Fact]
    public void Read_CustomSection_PartialReportsMissingFields()
    {
        var config = Read("""{"ai":{"endpoint":"https://example.com/v1","apiKey":"","model":"   "}}""");

        Assert.False(config.IsConfigured);
        Assert.Equal([AiConfig.ApiKeyPropertyName, AiConfig.ModelPropertyName], config.MissingFields);
    }

    [Fact]
    public void Read_NonObjectSection_ReturnsNull()
    {
        Assert.Null(AiConfig.Read(JsonNode.Parse("""{"ai":"nope"}""")));
    }

    [Fact]
    public void Read_ClampsOptionalValues()
    {
        var tooLarge = Read(
            """{"ai":{"endpoint":"e","apiKey":"k","model":"m","maxResults":999,"temperature":9}}""");
        Assert.Equal(AiConfig.MaxResultsLimit, tooLarge.MaxResults);
        Assert.Equal(2f, tooLarge.Temperature);

        var tooSmall = Read(
            """{"ai":{"endpoint":"e","apiKey":"k","model":"m","maxResults":0,"temperature":-3}}""");
        Assert.Equal(1, tooSmall.MaxResults);
        Assert.Equal(0f, tooSmall.Temperature);
    }

    [Fact]
    public void Read_TrimsStringValues()
    {
        var config = Read(
            """{"ai":{"endpoint":"  https://example.com/v1  ","apiKey":"  sk-x  ","model":"  m  "}}""");

        Assert.Equal("https://example.com/v1", config.Endpoint);
        Assert.Equal("sk-x", config.ApiKey);
        Assert.Equal("m", config.Model);
    }

    [Fact]
    public void Read_KiloFreePreset_NeedsNoKey()
    {
        var config = Read("""{"ai":{"provider":"kilo-free"}}""");

        Assert.Equal(AiProvider.KiloFree, config.Provider);
        Assert.Equal(AiConfig.KiloFreeEndpoint, config.Endpoint);
        Assert.Equal(AiConfig.KiloFreeModel, config.Model);
        Assert.True(config.IsConfigured);
        Assert.False(config.RequiresApiKey);
        Assert.False(config.SendApiKey);
        Assert.Empty(config.MissingFields);
    }

    [Fact]
    public void Read_DeepSeekPreset_UsesFlashByDefault()
    {
        var config = Read("""{"ai":{"provider":"deepseek","apiKey":"sk-deepseek"}}""");

        Assert.Equal(AiProvider.DeepSeek, config.Provider);
        Assert.Equal(AiConfig.DeepSeekEndpoint, config.Endpoint);
        Assert.Equal(AiConfig.DeepSeekDefaultModel, config.Model);
        Assert.True(config.IsConfigured);
        Assert.True(config.RequiresApiKey);
    }

    [Fact]
    public void Read_DeepSeekPreset_AllowsProModelOverride()
    {
        var config = Read(
            """{"ai":{"provider":"deepseek","apiKey":"sk-deepseek","model":"deepseek-pro"}}""");

        Assert.Equal(AiConfig.DeepSeekProModel, config.Model);
    }

    [Fact]
    public void Read_DeepSeekPreset_WithoutKey_IsNotConfigured()
    {
        var config = Read("""{"ai":{"provider":"deepseek"}}""");

        Assert.False(config.IsConfigured);
        Assert.Equal([AiConfig.ApiKeyPropertyName], config.MissingFields);
    }

    [Fact]
    public void Read_OpenCodeGoPreset_NeedsKeyAndModel()
    {
        var missing = Read("""{"ai":{"provider":"opencode-go"}}""");
        Assert.False(missing.IsConfigured);
        Assert.Equal([AiConfig.ApiKeyPropertyName, AiConfig.ModelPropertyName], missing.MissingFields);

        var configured = Read(
            """{"ai":{"provider":"opencode-go","apiKey":"oc_sk","model":"deepseek-v4.1-flash"}}""");
        Assert.True(configured.IsConfigured);
        Assert.Equal(AiConfig.OpenCodeGoEndpoint, configured.Endpoint);
        Assert.Equal("deepseek-v4.1-flash", configured.Model);
    }

    [Fact]
    public void Read_ProviderObject_IsAccepted()
    {
        var config = Read("""{"ai":{"provider":{"name":"deepseek"},"apiKey":"k"}}""");

        Assert.Equal(AiProvider.DeepSeek, config.Provider);
        Assert.Equal(AiConfig.DeepSeekEndpoint, config.Endpoint);
    }

    [Fact]
    public void Read_ExplicitValues_OverridePresetDefaults()
    {
        var config = Read(
            """{"ai":{"provider":"deepseek","apiKey":"k","endpoint":"https://custom/v1","model":"custom-model"}}""");

        Assert.Equal("https://custom/v1", config.Endpoint);
        Assert.Equal("custom-model", config.Model);
    }

    [Fact]
    public void SendApiKey_TreatsFreePlaceholderAsAnonymous()
    {
        var config = Read("""{"ai":{"provider":"kilo-free","apiKey":"free"}}""");

        Assert.False(config.SendApiKey);
    }

    [Fact]
    public void BuildRequestHeaders_OnlyOpenCodeGoGetsHeaders()
    {
        var deepSeek = Read("""{"ai":{"provider":"deepseek","apiKey":"k"}}""");
        Assert.Empty(deepSeek.BuildRequestHeaders());

        var openCode = Read(
            """{"ai":{"provider":"opencode-go","apiKey":"k","model":"m","session":"session-123"}}""");
        var headers = openCode.BuildRequestHeaders();

        Assert.Equal(2, headers.Count);
        Assert.Equal("session-123", headers["x-opencode-session"]);
        Assert.StartsWith("command-park/", headers["User-Agent"]);
    }

    [Fact]
    public void BuildRequestHeaders_ManualModeUsesCustomHeaders()
    {
        var config = Read(
            """{"ai":{"endpoint":"https://example.com/v1","apiKey":"k","model":"m","headers":{"X-Custom":"abc","X-Skip":123}}}""");

        var headers = config.BuildRequestHeaders();

        Assert.Single(headers);
        Assert.Equal("abc", headers["X-Custom"]);
    }

    [Fact]
    public void BuildRequestHeaders_ProviderHeadersWinOnConflict()
    {
        var config = Read(
            """{"ai":{"provider":"opencode-go","apiKey":"k","model":"m","session":"s1","headers":{"x-opencode-session":"manual","X-Extra":"yes"}}}""");

        var headers = config.BuildRequestHeaders();

        Assert.Equal("s1", headers["x-opencode-session"]);
        Assert.Equal("yes", headers["X-Extra"]);
        Assert.StartsWith("command-park/", headers["User-Agent"]);
    }

    [Fact]
    public void BuildRequestHeaders_GeneratesSessionWhenMissing()
    {
        var config = Read("""{"ai":{"provider":"opencode-go","apiKey":"k","model":"m"}}""");

        var first = config.BuildRequestHeaders()["x-opencode-session"];
        var second = config.BuildRequestHeaders()["x-opencode-session"];

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(second));
    }
}
