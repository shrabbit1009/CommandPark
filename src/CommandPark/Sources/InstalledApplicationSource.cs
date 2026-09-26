namespace CommandPark.Sources;

public class InstalledApplicationSource : IHintWithPrefix
{
    public List<string> Excludes { get; } = [];

    public bool PrefixMode { get; set; }
    
    /// <summary>
    /// 索引软件的拼音（中文环境）
    /// </summary>
    public bool PinyinMode { get; set; }

    public IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        
    }
}