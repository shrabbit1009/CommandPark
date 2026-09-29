namespace CommandPark;

public interface ISearchEngineHelper
{
    public Task<string[]> GetSuggestedItems(string keyword, CancellationToken cancellationToken = default);
    public string BuildSearchUrl(string keyword);
}