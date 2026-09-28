using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(CalcSource))]
public class CalcSourceTest
{
    [Fact]
    public async Task SearchAsync_NormalExpression_ReturnsDetailedResult()
    {
        var source = new CalcSource();

        var hints = await source.SearchAsync("c:2 + 3").ToListAsync();

        var hint = Assert.Single(hints, item => item.Id == "calc:result");
        Assert.StartsWith("= 5", hint.Name);
        var detailed = Assert.IsAssignableFrom<IDetailedHint>(hint);
        Assert.Contains("输入: 2 + 3", detailed.Details);
        Assert.Contains("结果: 5", detailed.Details);

        var desmosHint = Assert.Single(hints, item => item.Id == "calc:desmos");
        Assert.Equal("在 Desmos 中打开", desmosHint.Name);
    }

    [Fact]
    public void ReadConfig_AppliesLatexSettings()
    {
        var source = new CalcSource();
        var json = JsonNode.Parse(
            """
            {
              "sources": {
                "latexCalc": {
                  "prefixMode": false,
                  "timeoutSeconds": 12
                }
              }
            }
            """)!;

        Assert.True(source.ReadConfig(json));
        Assert.False(source.PrefixMode);
        Assert.Equal(12, source.TimeoutSeconds);
    }

    [Fact]
    public void ReadConfig_WithoutLatexSection_ReturnsFalse()
    {
        var source = new CalcSource();

        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }

    [Fact]
    public async Task SearchAsync_WhenCancellationAlreadyRequested_ThrowsPromptly()
    {
        var source = new CalcSource();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.SearchAsync("c:1 + 1", cts.Token).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_HugeExpression_ReturnsPromptlyWithoutHanging()
    {
        var source = new CalcSource
        {
            TimeoutSeconds = 1,
        };

        var hints = await source
            .SearchAsync("c:99999999999999999999!")
            .ToListAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(6));

        Assert.Contains(hints, hint => hint.Id is "lcalc:timeout" or "lcalc:error");
        Assert.Contains(hints, hint => hint.Id == "calc:desmos");
    }
}
