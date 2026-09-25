using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(Hint))]
public class HintTest
{
    [Fact]
    public void DefaultConstructor_UsesUnknownPlaceholders()
    {
        var hint = new Hint();

        Assert.Equal("unknown", hint.Id);
        Assert.Equal("unknown", hint.Name);
        Assert.Equal("unknown", hint.Description);
        Assert.Equal(0f, hint.Similarity);
    }

    [Fact]
    public async Task Run_InvokesOnRunWithCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var received = CancellationToken.None;
        var hint = new Hint("id", "name", "description", token =>
        {
            received = token;
            return Task.CompletedTask;
        });

        await hint.Run(cts.Token);

        Assert.Equal(cts.Token, received);
        Assert.Equal("id", hint.Id);
        Assert.Equal("name", hint.Name);
        Assert.Equal("description", hint.Description);
    }

    [Fact]
    public async Task Run_WithoutOnRun_Completes()
    {
        var hint = new Hint("id", "name", "description");

        // 默认 OnRun 是空操作，不应抛异常
        await hint.Run(CancellationToken.None);
    }

    [Fact]
    public async Task Run_WhenOnRunAssignedLater_UsesLatestDelegate()
    {
        var hint = new Hint("id", "name", "description");
        var invoked = false;
        hint.OnRun = _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        await hint.Run(CancellationToken.None);

        Assert.True(invoked);
    }
}