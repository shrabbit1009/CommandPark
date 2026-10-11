using System.Globalization;
using System.Text.Json.Nodes;
using CommandPark.Strings;
using JetBrains.Annotations;

namespace CommandPark.Tests.Strings;

[TestSubject(typeof(LanguageConfig))]
public class LanguageConfigTest
{
    private static LanguageConfig Read(string json) => LanguageConfig.Read(JsonNode.Parse(json));

    [Fact]
    public void Default_FollowsSystemAndFallsBackToChinese()
    {
        Assert.Null(LanguageConfig.Default.Current);
        Assert.Equal(["zh_CN"], LanguageConfig.Default.FallbackOrder);
        Assert.Equal(LanguageConfig.DefaultBaseLanguage, LanguageConfig.Default.BaseLanguage);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"theme":"dark"}""")]
    [InlineData("""{"language":null}""")]
    [InlineData("""{"language":"zh_CN"}""")]
    [InlineData("""{"language":[]}""")]
    public void Read_WithoutLanguageSection_ReturnsDefault(string json)
    {
        var config = Read(json);

        Assert.Null(config.Current);
        Assert.Equal(["zh_CN"], config.FallbackOrder);
        Assert.Equal("zh_CN", config.BaseLanguage);
    }

    [Fact]
    public void Read_NullConfig_ReturnsDefault()
    {
        Assert.Equal(LanguageConfig.Default, LanguageConfig.Read(null));
    }

    [Theory]
    [InlineData("""{"language":{"current":"en-US"}}""", "en-US")]
    [InlineData("""{"language":{"current":" en-US "}}""", "en-US")]
    [InlineData("""{"language":{"current":"zh_CN"}}""", "zh_CN")]
    public void Read_KeepsExplicitCurrentLanguage(string json, string expected)
    {
        Assert.Equal(expected, Read(json).ResolveCurrentLanguage());
    }

    [Theory]
    [InlineData("""{"language":{"current":"auto"}}""")]
    [InlineData("""{"language":{"current":"AUTO"}}""")]
    [InlineData("""{"language":{"current":"system"}}""")]
    [InlineData("""{"language":{"current":null}}""")]
    [InlineData("""{"language":{"current":""}}""")]
    [InlineData("""{"language":{"current":123}}""")]
    public void Read_AutoOrMissingCurrent_FollowsSystemCulture(string json)
    {
        var config = Read(json);

        Assert.Null(config.Current);
        Assert.Equal(CultureInfo.CurrentUICulture.Name, config.ResolveCurrentLanguage());
    }

    [Fact]
    public void Read_FallbackAsSingleString()
    {
        Assert.Equal(["en_US"], Read("""{"language":{"fallback":"en_US"}}""").FallbackOrder);
    }

    [Fact]
    public void Read_FallbackAsArrayKeepsOrder()
    {
        Assert.Equal(
            ["en_US", "ja_JP", "zh_TW"],
            Read("""{"language":{"fallback":["en_US","ja_JP","zh_TW"]}}""").FallbackOrder);
    }

    [Fact]
    public void Read_FallbackIgnoresNonStringAndEmptyEntries()
    {
        Assert.Equal(
            ["ja_JP"],
            Read("""{"language":{"fallback":[123,"","  ","ja_JP"]}}""").FallbackOrder);
    }

    [Fact]
    public void Read_CustomBaseLanguage()
    {
        Assert.Equal("en_US", Read("""{"language":{"base":"en_US"}}""").BaseLanguage);
    }

    [Fact]
    public void ResolveOrder_IsCurrentThenFallbackThenBase()
    {
        var config = Read("""{"language":{"current":"zh_TW","fallback":["en_US","ja_JP"],"base":"zh_CN"}}""");

        Assert.Equal(["zh_TW", "en_US", "ja_JP", "zh_CN"], config.ResolveOrder());
    }

    [Fact]
    public void ResolveOrder_DeduplicatesIgnoringCaseAndSkippingEmpty()
    {
        var config = Read(
            """{"language":{"current":"zh_cn","fallback":["zh_CN","en_US",""],"base":"zh_CN"}}""");

        Assert.Equal(["zh_cn", "en_US"], config.ResolveOrder());
    }

    [Fact]
    public void ResolveOrder_WithoutConfig_UsesSystemCultureThenChinese()
    {
        var order = LanguageConfig.Default.ResolveOrder();

        Assert.Equal([CultureInfo.CurrentUICulture.Name, "zh_CN"], order);
    }
}