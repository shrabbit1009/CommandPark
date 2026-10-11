using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommandPark.Strings;
using JetBrains.Annotations;

namespace CommandPark.Tests.Strings;

[TestSubject(typeof(StringCollectionBuilder))]
public class StringCollectionBuilderTest
{
    private static string CreateLanguageDirectory(params (string Language, string Json)[] files)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CommandPark.Tests", Guid.NewGuid().ToString("N"));
        var languageDirectory = Path.Combine(directory, StringCollectionBuilder.LanguageDirectoryName);
        Directory.CreateDirectory(languageDirectory);

        foreach (var (language, json) in files)
        {
            File.WriteAllText(Path.Combine(languageDirectory, language + ".json"), json);
        }

        return directory;
    }

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // 链的顺序
    // ------------------------------------------------------------------

    [Fact]
    public void Build_UsesCurrentLanguageFirstThenFallbackThenBase()
    {
        var directory = CreateLanguageDirectory(
            ("en_US", """{"ui":{"title":"English","onlyUs":"US only"}}"""),
            ("ja_JP", """{"ui":{"title":"日本語","onlyJp":"JP only"}}"""),
            ("zh_CN", """{"ui":{"title":"中文","onlyBase":"中文基准"}}"""));

        try
        {
            var chain = StringCollectionBuilder.Create(directory)
                                               .WithLanguage("en_US")
                                               .WithFallback("ja_JP")
                                               .Build();

            Assert.Equal(["en_US", "ja_JP", "zh_CN"], ProviderNames(chain));

            Assert.Equal("English", chain.GetString("ui.title"));       // 当前语言优先
            Assert.Equal("JP only", chain.GetString("ui.onlyJp"));      // 回退顺序第二档
            Assert.Equal("中文基准", chain.GetString("ui.onlyBase"));   // 基准语言兜底
            Assert.Equal("ui.missing", chain.GetString("ui.missing"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Build_DefaultBaseLanguageIsChinese()
    {
        var directory = CreateLanguageDirectory(("zh_CN", """{"ui":{"title":"中文"}}"""));

        try
        {
            var chain = StringCollectionBuilder.Create(directory).WithLanguage("en_US").Build();

            Assert.Equal(["zh_CN"], ProviderNames(chain));
            Assert.Equal("中文", chain.GetString("ui.title"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Build_SkipsMissingLanguageFiles()
    {
        var directory = CreateLanguageDirectory(("zh_CN", """{"ui":{"title":"中文"}}"""));

        try
        {
            var chain = StringCollectionBuilder.Create(directory)
                                               .WithLanguage("fr_FR")
                                               .WithFallback("de_DE", "zh_CN")
                                               .Build();

            Assert.Equal(["zh_CN"], ProviderNames(chain));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Build_DeduplicatesLanguagesThatResolveToTheSameFile()
    {
        var directory = CreateLanguageDirectory(
            ("zh_CN", """{"ui":{"title":"中文"}}"""),
            ("en_US", """{"ui":{"title":"English"}}"""));

        try
        {
            var chain = StringCollectionBuilder.Create(directory)
                                               .WithLanguage("en-US")
                                               .WithFallback("en_US", "zh_CN", "zh-CN")
                                               .Build();

            Assert.Equal(["en_US", "zh_CN"], ProviderNames(chain));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Build_SkipsCorruptLanguageFile()
    {
        var directory = CreateLanguageDirectory(
            ("en_US", "{ not json"),
            ("zh_CN", """{"ui":{"title":"中文"}}"""));

        try
        {
            var chain = StringCollectionBuilder.Create(directory).WithLanguage("en_US").Build();

            Assert.Equal(["zh_CN"], ProviderNames(chain));
            Assert.Equal("中文", chain.GetString("ui.title"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Build_WithoutLanguageDirectory_ReturnsEmptyChain()
    {
        var chain = StringCollectionBuilder
            .Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .Build();

        Assert.True(chain.IsEmpty);
        Assert.Equal("ui.title", chain.GetString("ui.title"));
    }

    [Fact]
    public void Build_PropagatesThrowFlagsToProviders()
    {
        var directory = CreateLanguageDirectory(("zh_CN", """{"ui":{"title":"中文","count":3}}"""));

        try
        {
            var builder = StringCollectionBuilder.Create(directory).WithLanguage("zh_CN");
            builder.ThrowIfNotFound = true;
            builder.ThrowIfNotString = true;

            var provider = (StringCollection)builder.Build().Chain[0];

            Assert.True(provider.ThrowIfNotFound);
            Assert.True(provider.ThrowIfNotString);
            Assert.Throws<StringPathNotFoundException>(() => provider.GetString("ui.missing"));
            Assert.Throws<StringPathNotStringException>(() => provider.GetString("ui.count"));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ------------------------------------------------------------------
    // 顺序 / 语言标记解析
    // ------------------------------------------------------------------

    [Fact]
    public void BuildOrder_IsCurrentThenAddedFallbacksThenBase()
    {
        var builder = StringCollectionBuilder.Create()
                                             .WithLanguage("en_US")
                                             .AddFallback("ja_JP")
                                             .AddFallback("ko_KR")
                                             .WithBaseLanguage("zh_CN");

        Assert.Equal(["en_US", "ja_JP", "ko_KR", "zh_CN"], builder.BuildOrder());
        Assert.Equal(["ja_JP", "ko_KR"], builder.FallbackOrder);
    }

    [Fact]
    public void WithFallback_ReplacesTheWholeOrder()
    {
        var builder = StringCollectionBuilder.Create().AddFallback("de_DE").WithFallback("fr_FR");

        Assert.Equal(["fr_FR"], builder.FallbackOrder);
    }

    [Fact]
    public void WithLanguage_Null_FollowsSystemCulture()
    {
        var builder = StringCollectionBuilder.Create().WithLanguage("en_US").WithLanguage(null);

        Assert.Null(builder.Language);
        Assert.Equal(CultureInfo.CurrentUICulture.Name, builder.BuildOrder()[0]);
    }

    [Theory]
    [InlineData("en_US", "en_US")]
    [InlineData("en-US", "en_US")]
    [InlineData("EN_us", "en_US")]
    [InlineData("en", "en_US")]
    [InlineData("en-GB", "en_US")]
    [InlineData("zh-Hans-CN", "zh_CN")]
    [InlineData("zh_CN", "zh_CN")]
    public void TryResolveLanguageFile_MatchesFilesLoosely(string requested, string expectedFile)
    {
        var directory = CreateLanguageDirectory(
            ("en_US", "{}"),
            ("zh_CN", "{}"));

        try
        {
            var builder = StringCollectionBuilder.Create(directory);

            Assert.True(builder.TryResolveLanguageFile(requested, out var path));
            Assert.Equal(expectedFile, Path.GetFileNameWithoutExtension(path));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("de_DE")]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolveLanguageFile_UnknownOrEmpty_ReturnsFalse(string? requested)
    {
        var directory = CreateLanguageDirectory(("zh_CN", "{}"));

        try
        {
            Assert.False(StringCollectionBuilder.Create(directory).TryResolveLanguageFile(requested, out var path));
            Assert.Equal(string.Empty, path);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void AvailableLanguages_ListsEveryLanguageFile()
    {
        var directory = CreateLanguageDirectory(
            ("zh_CN", "{}"),
            ("en_US", "{}"),
            ("ja_JP", "{}"));

        try
        {
            Assert.Equal(["en_US", "ja_JP", "zh_CN"], StringCollectionBuilder.Create(directory).AvailableLanguages);
        }
        finally
        {
            Delete(directory);
        }
    }

    // ------------------------------------------------------------------
    // 配置
    // ------------------------------------------------------------------

    [Fact]
    public void ReadConfig_AppliesLanguageSection()
    {
        var directory = CreateLanguageDirectory(
            ("en_US", "{}"),
            ("ja_JP", "{}"),
            ("ko_KR", "{}"));

        try
        {
            var builder = StringCollectionBuilder.Create(directory).ReadConfig(JsonNode.Parse(
                """{"language":{"current":"ja_JP","fallback":["ko_KR"],"base":"en_US"}}"""));

            Assert.Equal("ja_JP", builder.Language);
            Assert.Equal(["ko_KR"], builder.FallbackOrder);
            Assert.Equal("en_US", builder.BaseLanguage);
            Assert.Equal(["ja_JP", "ko_KR", "en_US"], builder.BuildOrder());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ReadConfig_WithoutLanguageSection_KeepsDefaultBaseLanguage()
    {
        var builder = StringCollectionBuilder.Create().ReadConfig(JsonNode.Parse("{}"));

        Assert.Null(builder.Language);
        Assert.Equal(["zh_CN"], builder.FallbackOrder);
        Assert.Equal(LanguageConfig.DefaultBaseLanguage, builder.BaseLanguage);
    }

    [Fact]
    public void WithDirectory_SwitchesLanguageDirectory()
    {
        var directory = CreateLanguageDirectory(("zh_CN", """{"ui":{"title":"中文"}}"""));

        try
        {
            var builder = StringCollectionBuilder
                .Create()
                .WithDirectory(Path.Combine(directory, StringCollectionBuilder.LanguageDirectoryName))
                .WithLanguage("zh_CN");

            Assert.Equal(Path.Combine(directory, "lang"), builder.LanguageDirectory);
            Assert.Equal("中文", builder.Build().GetString("ui.title"));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ------------------------------------------------------------------
    // 随程序发布的语言文件
    // ------------------------------------------------------------------

    private static string ShippedLanguageDirectory =>
        Path.Combine(AppContext.BaseDirectory, StringCollectionBuilder.LanguageDirectoryName);

    [Fact]
    public void ShippedLanguageFiles_ShareTheSameKeysAndPlaceholders()
    {
        var files = Directory.GetFiles(ShippedLanguageDirectory, "*.json");
        Assert.True(files.Length >= 2, "至少要有中文基准与一份其他语言的翻译。");

        var baseFile = Path.Combine(ShippedLanguageDirectory, "zh_CN.json");
        Assert.True(File.Exists(baseFile), "必须以 zh_CN.json 作为基准语言。");

        var baseEntries = ReadEntries(baseFile);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var entries = ReadEntries(file);

            Assert.Equal(
                baseEntries.Keys.OrderBy(key => key, StringComparer.Ordinal),
                entries.Keys.OrderBy(key => key, StringComparer.Ordinal));

            foreach (var (key, value) in entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{name}: {key} 不能为空");
                Assert.Equal(Placeholders(baseEntries[key]), Placeholders(value));
            }
        }
    }

    [Fact]
    public void ShippedLanguageFiles_CanSwitchToEnglish()
    {
        var chain = StringCollectionBuilder.Create()
                                           .WithLanguage("en-US")
                                           .WithFallback("zh_CN")
                                           .Build();

        Assert.Equal(["en_US", "zh_CN"], ProviderNames(chain));
        Assert.Equal("AI chat", chain.GetString("ui.ai.title"));
        Assert.Equal("Send (Alt+1 / Enter)", chain.GetFormat("ui.ai.send", "Alt"));

        // 打包的语言文件都是完整的，所以这里每一条都来自英文；
        // 缺键回退（英文 → 中文基准）由上面的临时目录用例覆盖。
        Assert.Equal("Open {0}", chain.GetString("sources.installedApplication.name"));
    }
    [Fact]
    public void ShippedLanguageFiles_DriveTheGlobalLocalization()
    {
        // TestLocalization 已把全局链固定成 zh_CN，这里顺带验证真实语言文件能被取到。
        Assert.Equal("AI 聊天", Localization.Get("ui.ai.title"));
        Assert.Equal("发送 (Alt+1 / Enter)", Localization.Format("ui.ai.send", "Alt"));
        Assert.False(Localization.TryGet("ui.not.exists", out _));
    }

    private static Dictionary<string, string> ReadEntries(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        void Walk(JsonNode? node, string prefix)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj)
                    {
                        Walk(child, prefix.Length == 0 ? key : $"{prefix}.{key}");
                    }

                    break;

                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    entries[prefix] = value.GetValue<string>();
                    break;
            }
        }

        Walk(JsonNode.Parse(File.ReadAllText(path)), string.Empty);
        return entries;
    }

    private static List<int> Placeholders(string value) =>
    [
        .. Regex.Matches(value, @"\{(\d+)\}")
                .Select(match => int.Parse(match.Groups[1].Value))
                .Distinct()
                .Order(),
    ];

    private static List<string> ProviderNames(MultiStringCollection chain) =>
        [.. chain.Chain.OfType<StringCollection>().Select(collection => collection.Name)];
}