namespace CommandPark;

public interface IHintSource
{
    public IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default);
}