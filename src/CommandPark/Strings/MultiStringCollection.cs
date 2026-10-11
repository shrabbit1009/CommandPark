namespace CommandPark.Strings;

/// <summary>
/// 把多个 <see cref="IStringCollection"/> 按优先级拼成一条链。
/// <para>
/// 查找时从前往后问，第一个命中的提供者胜出；全都没命中时回退成路径本身
/// （与 <see cref="StringCollection"/> 不抛异常时的行为一致）。
/// </para>
/// <para>
/// 链的顺序就是优先级：例如「当前语言 → 回退语言 → 中文基准」。
/// </para>
/// </summary>
public sealed class MultiStringCollection : IStringCollection
{
    private readonly List<IStringCollection> _chain = [];

    public MultiStringCollection()
    {
    }

    /// <param name="chain">按优先级从高到低排列的提供者。</param>
    public MultiStringCollection(IEnumerable<IStringCollection> chain)
    {
        AddRange(chain);
    }

    /// <summary>提供者链，按优先级从高到低排列。</summary>
    public IReadOnlyList<IStringCollection> Chain => _chain;

    /// <summary>链上的提供者个数。</summary>
    public int Count => _chain.Count;

    /// <summary>链上没有任何提供者。</summary>
    public bool IsEmpty => _chain.Count == 0;

    /// <summary>追加一个提供者（追加在末尾，优先级最低）。</summary>
    public MultiStringCollection Add(IStringCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        _chain.Add(collection);
        return this;
    }

    /// <summary>按顺序追加多个提供者。</summary>
    public MultiStringCollection AddRange(IEnumerable<IStringCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        foreach (var collection in collections)
        {
            Add(collection);
        }

        return this;
    }

    /// <inheritdoc />
    public bool TryGetString(string path, out string? value)
    {
        foreach (var collection in _chain)
        {
            if (collection.TryGetString(path, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public bool Contains(string path) => TryGetString(path, out _);

    /// <inheritdoc />
    public string GetString(string path) => TryGetString(path, out var value) ? value! : path;

    /// <inheritdoc />
    public string GetFormat(string path, params object?[] args) =>
        string.Format(GetString(path), args);
}