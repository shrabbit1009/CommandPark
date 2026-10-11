using System.Text.Json;
using System.Text.Json.Nodes;
using CommandPark.Strings;
using JetBrains.Annotations;

namespace CommandPark.Tests.Strings;

[TestSubject(typeof(StringCollection))]
public class StringCollectionTest
{
    private const string Sample = """
        {
          "ui": {
            "main": {
              "title": "标题",
              "count": 3,
              "enabled": true,
              "nothing": null
            }
          },
          "items": [
            "first",
            { "name": "nested" }
          ]
        }
        """;

    private static StringCollection Create(string json = Sample) => StringCollection.Parse(json, "test");

    // ------------------------------------------------------------------
    // 路径解析
    // ------------------------------------------------------------------

    [Fact]
    public void GetString_ResolvesNestedPath()
    {
        Assert.Equal("标题", Create().GetString("ui.main.title"));
    }

    [Fact]
    public void GetString_ResolvesPathDeeperThanOneLevel()
    {
        // 回归：早期实现每层都从根节点取，只有一层路径才对。
        var collection = StringCollection.Parse("""{"a":{"b":{"c":{"d":"deep"}}}}""");

        Assert.Equal("deep", collection.GetString("a.b.c.d"));
    }

    [Fact]
    public void GetString_SupportsArrayIndex()
    {
        var collection = Create();

        Assert.Equal("first", collection.GetString("items.0"));
        Assert.Equal("nested", collection.GetString("items.1.name"));
    }

    [Fact]
    public void GetString_WhenArrayIndexOutOfRange_ReturnsPath()
    {
        Assert.Equal("items.9", Create().GetString("items.9"));
    }

    [Fact]
    public void GetString_WhenIntermediateNodeIsAString_ReturnsPath()
    {
        Assert.Equal("ui.main.title.extra", Create().GetString("ui.main.title.extra"));
    }

    // ------------------------------------------------------------------
    // 找不到 / 不是字符串
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("ui.missing")]
    [InlineData("ui.main.missing")]
    [InlineData("missing.deep.path")]
    public void GetString_WhenPathMissing_ReturnsPath(string path)
    {
        Assert.Equal(path, Create().GetString(path));
    }

    [Fact]
    public void GetString_WhenPathMissing_AndThrowIfNotFound_Throws()
    {
        var collection = Create();
        collection.ThrowIfNotFound = true;

        Assert.Throws<StringPathNotFoundException>(() => collection.GetString("ui.missing"));
    }

    [Theory]
    [InlineData("ui.main.count")]
    [InlineData("ui.main.enabled")]
    [InlineData("ui.main")]
    [InlineData("items")]
    public void GetString_WhenValueIsNotString_ReturnsPath(string path)
    {
        Assert.Equal(path, Create().GetString(path));
    }

    [Fact]
    public void GetString_WhenValueIsNotString_AndThrowIfNotString_Throws()
    {
        var collection = Create();
        collection.ThrowIfNotString = true;

        Assert.Throws<StringPathNotStringException>(() => collection.GetString("ui.main.count"));
    }

    [Fact]
    public void GetString_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Create().GetString(null!));
    }

    [Fact]
    public void GetString_WithNullRoot_ReturnsPath()
    {
        var collection = new StringCollection(null, "empty");

        Assert.Equal("ui.main.title", collection.GetString("ui.main.title"));
        Assert.False(collection.TryGetString("ui.main.title", out _));
    }

    // ------------------------------------------------------------------
    // TryGetString / Contains
    // ------------------------------------------------------------------

    [Fact]
    public void TryGetString_OnlyTrueForRealStrings()
    {
        var collection = Create();

        Assert.True(collection.TryGetString("ui.main.title", out var value));
        Assert.Equal("标题", value);

        Assert.False(collection.TryGetString("ui.main.count", out var number));
        Assert.Null(number);

        Assert.False(collection.TryGetString("ui.missing", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Contains_MatchesTryGetString()
    {
        var collection = Create();

        Assert.True(collection.Contains("ui.main.title"));
        Assert.False(collection.Contains("ui.main.count"));
        Assert.False(collection.Contains("ui.main.missing"));
    }

    // ------------------------------------------------------------------
    // 格式化
    // ------------------------------------------------------------------

    [Fact]
    public void GetFormat_FormatsValueWithArguments()
    {
        var collection = StringCollection.Parse("""{"ui":{"send":"发送 ({0}+1 / {1})"}}""");

        Assert.Equal("发送 (Alt+1 / Enter)", collection.GetFormat("ui.send", "Alt", "Enter"));
    }

    [Fact]
    public void GetFormat_WithoutPlaceholders_ReturnsValueAsIs()
    {
        Assert.Equal("标题", Create().GetFormat("ui.main.title"));
    }

    [Fact]
    public void GetFormat_WhenPathMissing_ReturnsPath()
    {
        Assert.Equal("ui.missing", Create().GetFormat("ui.missing", "arg"));
    }

    // ------------------------------------------------------------------
    // 缓存
    // ------------------------------------------------------------------

    [Fact]
    public void CachedResolution_StillHonoursFlagsChangedAfterwards()
    {
        var collection = Create();

        Assert.Equal("ui.missing", collection.GetString("ui.missing"));

        collection.ThrowIfNotFound = true;

        Assert.Throws<StringPathNotFoundException>(() => collection.GetString("ui.missing"));
    }

    [Fact]
    public void CachedResolution_ReturnsSameValueOnRepeatedCalls()
    {
        var collection = Create();

        Assert.Equal("标题", collection.GetString("ui.main.title"));
        Assert.Equal("标题", collection.GetString("ui.main.title"));
        Assert.Equal("标题", collection.GetFormat("ui.main.title"));
    }

    // ------------------------------------------------------------------
    // 文件与名称
    // ------------------------------------------------------------------

    [Fact]
    public void FromFile_ReadsJsonAndUsesFileNameAsName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CommandPark.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "zh_CN.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, """{"ui":{"title":"标题"}}""");

        try
        {
            var collection = StringCollection.FromFile(path);

            Assert.Equal("zh_CN", collection.Name);
            Assert.Equal("标题", collection.GetString("ui.title"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Parse_AllowsCommentsAndTrailingCommas()
    {
        var collection = StringCollection.Parse(
            """
            {
              // 注释
              "ui": { "title": "标题", },
            }
            """);

        Assert.Equal("标题", collection.GetString("ui.title"));
    }

    [Fact]
    public void Parse_WithInvalidJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => StringCollection.Parse("{ not json"));
    }

    [Fact]
    public void Parse_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StringCollection.Parse(null!));
    }

    [Fact]
    public void Constructor_WithoutName_UsesEmptyName()
    {
        var collection = new StringCollection(JsonNode.Parse("{}"));

        Assert.Equal(string.Empty, collection.Name);
    }
}