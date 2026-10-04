using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandPark.Utils.SourceHelpers;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(DesmosHelper))]
public class DesmosHelperTest
{
    [Fact]
    public void BodyDto_DefaultValues_AreValid()
    {
        var dto = new DesmosHelper.DesmosBodyDto();

        AssertRandomToken(dto.Hash, 10);
        Assert.Equal(CultureInfo.CurrentUICulture.IetfLanguageTag, dto.Lang);
        Assert.Equal("graphing", dto.Product);
        Assert.Equal("quick-link", dto.Type);
        Assert.Equal("{}", dto.State);
        Assert.StartsWith("data:image/png;base64,", dto.ThumbnailData);
    }

    [Fact]
    public void BodyDto_SerializesExpectedJsonPropertyNames()
    {
        var dto = new DesmosHelper.DesmosBodyDto
        {
            Hash = "abc123xyz9",
            Lang = "zh-CN",
            State = """{"version":11}""",
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(dto))!.AsObject();

        Assert.Equal("abc123xyz9", json["hash"]!.GetValue<string>());
        Assert.Equal("zh-CN", json["lang"]!.GetValue<string>());
        Assert.Equal("graphing", json["product"]!.GetValue<string>());
        Assert.Equal("""{"version":11}""", json["state"]!.GetValue<string>());
        Assert.Equal("quick-link", json["type"]!.GetValue<string>());
        Assert.StartsWith("data:image/png;base64,", json["thumbnailData"]!.GetValue<string>());
    }

    [Fact]
    public void StateDto_SerializesLatexAndViewport()
    {
        var dto = new DesmosHelper.DesmosStateDto("y=x^2");

        var state = JsonNode.Parse(JsonSerializer.Serialize(dto))!.AsObject();

        Assert.Equal(11, state["version"]!.GetValue<int>());
        AssertRandomToken(state["randomSeed"]!.GetValue<string>(), 32);
        Assert.True(state["includeFunctionParametersInRandomSeed"]!.GetValue<bool>());
        Assert.True(state["doNotMigrateMovablePointStyle"]!.GetValue<bool>());
        Assert.Equal(14, state["backmigrated"]!.GetValue<int>());

        var graph = state["graph"]!.AsObject();
        var viewport = graph["viewport"]!.AsObject();
        Assert.Equal(-10, viewport["xmin"]!.GetValue<int>());
        Assert.Equal(10, viewport["xmax"]!.GetValue<int>());
        Assert.Equal(-6.305858987090367, viewport["ymin"]!.GetValue<double>(), precision: 12);
        Assert.Equal(6.305858987090367, viewport["ymax"]!.GetValue<double>(), precision: 12);

        var latexStash = graph["__v12ViewportLatexStash"]!.AsObject();
        Assert.Equal("-10", latexStash["xmin"]!.GetValue<string>());
        Assert.Equal("10", latexStash["xmax"]!.GetValue<string>());

        var expression = state["expressions"]!["list"]!.AsArray().Single()!.AsObject();
        Assert.Equal("expression", expression["type"]!.GetValue<string>());
        Assert.Equal("1", expression["id"]!.GetValue<string>());
        Assert.Equal("#c74440", expression["color"]!.GetValue<string>());
        Assert.Equal("y=x^2", expression["latex"]!.GetValue<string>());
    }

    [Fact]
    public void StateDto_DefaultConstructor_UsesEmptyLatex()
    {
        var dto = new DesmosHelper.DesmosStateDto();

        var state = JsonNode.Parse(JsonSerializer.Serialize(dto))!.AsObject();
        var expression = state["expressions"]!["list"]!.AsArray().Single()!.AsObject();

        Assert.Equal(string.Empty, expression["latex"]!.GetValue<string>());
    }

    private static void AssertRandomToken(string value, int length)
    {
        Assert.Equal(length, value.Length);
        Assert.All(value, character => Assert.Contains(character, "0123456789abcdefghijklmnopqrstuvwxyz"));
    }
}