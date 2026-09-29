using System.Text.Json.Nodes;

namespace CommandPark.Sources;

public class BingSearchSource : IHintSource, IHintWithPrefix, IConfigReader
{
    public IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public bool PrefixMode { get; set; }
    public bool ReadConfig(JsonNode json)
    {
        throw new NotImplementedException();
    }
}