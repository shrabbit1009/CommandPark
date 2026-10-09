using System.Text.Json.Nodes;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(QuickSelectConfig))]
public class QuickSelectConfigTest
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("""{"quickSelect":{}}""", true)]
    [InlineData("""{"quickSelect":{"enabled":true}}""", true)]
    [InlineData("""{"quickSelect":{"enabled":false}}""", false)]
    [InlineData("""{"quickSelect":{"enabled":"nope"}}""", true)]
    [InlineData("""{"sources":{}}""", true)]
    public void ReadEnabled_DefaultsToTrue(string? json, bool expected)
    {
        var node = json is null ? null : JsonNode.Parse(json);

        Assert.Equal(expected, QuickSelectConfig.ReadEnabled(node));
    }

    [Theory]
    [InlineData(null, QuickSelectModifier.Alt)]
    [InlineData("""{"quickSelect":{}}""", QuickSelectModifier.Alt)]
    [InlineData("""{"quickSelect":{"modifier":"Alt"}}""", QuickSelectModifier.Alt)]
    [InlineData("""{"quickSelect":{"modifier":"alt"}}""", QuickSelectModifier.Alt)]
    [InlineData("""{"quickSelect":{"modifier":"Ctrl"}}""", QuickSelectModifier.Ctrl)]
    [InlineData("""{"quickSelect":{"modifier":"control"}}""", QuickSelectModifier.Ctrl)]
    [InlineData("""{"quickSelect":{"modifier":"Shift"}}""", QuickSelectModifier.Alt)]
    [InlineData("""{"quickSelect":{"modifier":42}}""", QuickSelectModifier.Alt)]
    public void ReadModifier_DefaultsToAlt(string? json, QuickSelectModifier expected)
    {
        var node = json is null ? null : JsonNode.Parse(json);

        Assert.Equal(expected, QuickSelectConfig.ReadModifier(node));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("""{"quickSelect":{}}""", false)]
    [InlineData("""{"quickSelect":{"showWeight":false}}""", false)]
    [InlineData("""{"quickSelect":{"showWeight":true}}""", true)]
    [InlineData("""{"quickSelect":{"showWeight":"yes"}}""", false)]
    public void ReadShowWeight_DefaultsToFalse(string? json, bool expected)
    {
        var node = json is null ? null : JsonNode.Parse(json);

        Assert.Equal(expected, QuickSelectConfig.ReadShowWeight(node));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 1)]
    [InlineData(8, 9)]
    [InlineData(9, null)]
    [InlineData(100, null)]
    public void GetKey_MapsFirstNineItems(int index, int? expected)
    {
        Assert.Equal(expected, QuickSelectConfig.GetKey(index));
    }
}
