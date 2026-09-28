using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(HintProvider))]
public class HomeDisplayProviderTest
{
    [Fact]
    public async Task SearchAsync_WithEmptyQuery_RunsOnlyHomeSources()
    {
        var provider = new HintProvider
        {
            Sources = { new HomeSource(), new RegularSource() },
        };
        var received = new List<IHint>();

        await provider.SearchAsync(
            string.Empty,
            batch => received.AddRange(batch),
            TimeSpan.FromMilliseconds(10));

        var hint = Assert.Single(received);
        Assert.Equal("home", hint.Id);
    }

    [Fact]
    public async Task SearchAsync_WithNonEmptyQuery_SkipsHomeSources()
    {
        var provider = new HintProvider
        {
            Sources = { new HomeSource(), new RegularSource() },
        };
        var received = new List<IHint>();

        await provider.SearchAsync(
            "query",
            batch => received.AddRange(batch),
            TimeSpan.FromMilliseconds(10));

        var hint = Assert.Single(received);
        Assert.Equal("regular", hint.Id);
    }

    private sealed class HomeSource : IHintSource, IHomeDisplayable
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new Hint("home", "home", "home");
        }
    }

    private sealed class RegularSource : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new Hint("regular", "regular", "regular");
        }
    }
}