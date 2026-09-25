using System.Runtime.CompilerServices;

namespace CommandPark;

public class TestHintSource : IHintSource
{
    public async IAsyncEnumerable<IHint> SearchAsync(
        string query, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new Hint("whats", query, $"What is \"{query}\"?");
        yield return new Hint("open",  query, $"Open {query} ↗");
        yield return new Hint("copy",  query, $"Copy \"{query}\" To Clipboard");
    }
}