using System.Text.Json.Nodes;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(ThemeConfigReader))]
public class ThemeConfigReaderTest
{
    [Fact]
    public void Read_WhenThemeMissingOrNull_ReturnsDefault()
    {
        Assert.Equal(ThemeMode.Default, ThemeConfigReader.Read(null));
        Assert.Equal(ThemeMode.Default, ThemeConfigReader.Read(JsonNode.Parse("{}")!));
        Assert.Equal(ThemeMode.Default, ThemeConfigReader.Read(JsonNode.Parse("""{"theme":null}""")!));
    }

    [Theory]
    [InlineData("auto", ThemeMode.Default)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("LIGHT", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("unknown", ThemeMode.Default)]
    public void Read_ParsesThemeValue(string value, ThemeMode expected)
    {
        var json = JsonNode.Parse($$"""{"theme":"{{value}}"}""")!;

        Assert.Equal(expected, ThemeConfigReader.Read(json));
    }

    [Fact]
    public void Read_WithNonStringValue_ReturnsDefault()
    {
        Assert.Equal(ThemeMode.Default, ThemeConfigReader.Read(JsonNode.Parse("""{"theme":123}""")!));
    }
}