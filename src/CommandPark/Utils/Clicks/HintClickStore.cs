using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace CommandPark.Utils.Clicks;

/// <summary>
/// 基于 htt 的点击权重存储：以“id + 名称 + 描述”的 SHA256 作为键记录点击次数，
/// 并把它换算成叠加在相似度上的排序权重。点击越多，权重越高，结果越靠前。
/// <para>
/// 本地只保存摘要与计数，不保存 Hint 明文，因此无法由本地 .htt 反推用户点了什么。
/// 实例对读写都做了加锁，可安全地从搜索线程读取、从 UI 线程记录点击。
/// </para>
/// </summary>
public sealed class HintClickStore : IHintWeightProvider
{
    /// <summary>默认最多保留的条目数。</summary>
    public const int DefaultMaxEntries = HashToTimes.DefaultMaxEntries;

    /// <summary>每一次点击带来的相似度加成。</summary>
    public const float BoostPerClick = 0.05f;

    /// <summary>点击加成的上限，避免高频项完全压过搜索相关度。</summary>
    public const float MaxBoost = 0.25f;

    /// <summary>拼接三个字段时使用的分隔符（ASCII Unit Separator），避免字段边界产生歧义。</summary>
    public const byte FieldSeparator = 0x1F;

    /// <summary>短文本哈希时使用的栈缓冲区大小，超过则改用池化数组。</summary>
    private const int StackBufferSize = 512;

    private readonly HashToTimes _times;
    private readonly object _gate = new();

    public HintClickStore(HashToTimes? times = null)
    {
        _times = times ?? new HashToTimes();
    }

    /// <summary>当前记录条数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _times.Count;
            }
        }
    }

    /// <summary>最多保留的条目数。</summary>
    public int MaxEntries
    {
        get
        {
            lock (_gate)
            {
                return _times.MaxEntries;
            }
        }
        set
        {
            lock (_gate)
            {
                _times.MaxEntries = value;
            }
        }
    }

    /// <summary>记录一次点击：已有条目计数 +1，新条目排到队尾，超过上限时淘汰最旧项。</summary>
    public void RecordClick(IHint hint) => RecordClick(HashOf(hint));

    /// <summary>按摘要记录一次点击。</summary>
    public void RecordClick(SHA256Store hash)
    {
        lock (_gate)
        {
            _times.Add(hash);
        }
    }

    /// <summary>读取某个 Hint 的点击次数。</summary>
    public int GetClickCount(IHint hint)
    {
        var hash = HashOf(hint);
        lock (_gate)
        {
            return _times.TryGetValue(hash, out var count) ? count : 0;
        }
    }

    /// <summary>
    /// 计算排序加成：<c>min(点击次数 × <see cref="BoostPerClick"/>, <see cref="MaxBoost"/>)</c>。
    /// 没有任何记录时直接返回 0，连摘要都不必计算。
    /// </summary>
    public float GetWeight(IHint hint)
    {
        lock (_gate)
        {
            if (_times.Count == 0)
            {
                return 0f;
            }
        }

        var hash = HashOf(hint);
        lock (_gate)
        {
            return _times.TryGetValue(hash, out var count) ? WeightOf(count) : 0f;
        }
    }

    /// <summary>把点击数换算成排序加成。</summary>
    public static float WeightOf(int clicks) =>
        clicks <= 0 ? 0f : MathF.Min(MaxBoost, BoostPerClick * clicks);

    /// <summary>按“id + 名称 + 描述”计算摘要。</summary>
    public static SHA256Store HashOf(IHint hint) => HashOf(hint.Id, hint.Name, hint.Description);

    /// <summary>
    /// 按“id + 名称 + 描述”计算 SHA256 摘要。字段之间用 <see cref="FieldSeparator"/> 连接，
    /// 因此 ("ab","c") 与 ("a","bc") 不会算出同一个摘要。
    /// </summary>
    public static SHA256Store HashOf(string? id, string? name, string? description)
    {
        id          ??= string.Empty;
        name        ??= string.Empty;
        description ??= string.Empty;

        var byteCount =
            Encoding.UTF8.GetByteCount(id)          + sizeof(byte) +
            Encoding.UTF8.GetByteCount(name)        + sizeof(byte) +
            Encoding.UTF8.GetByteCount(description);

        Span<byte> digest = stackalloc byte[SHA256Store.Size];

        byte[]? rented = null;
        Span<byte> buffer = stackalloc byte[StackBufferSize];
        if (byteCount > StackBufferSize)
        {
            rented = ArrayPool<byte>.Shared.Rent(byteCount);
            buffer = rented;
        }

        try
        {
            var written = 0;
            written += Encoding.UTF8.GetBytes(id,          buffer[written..]);
            buffer[written++] = FieldSeparator;
            written += Encoding.UTF8.GetBytes(name,        buffer[written..]);
            buffer[written++] = FieldSeparator;
            written += Encoding.UTF8.GetBytes(description, buffer[written..]);

            SHA256.HashData(buffer[..written], digest);
            return new SHA256Store(digest);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>从 htt 流读取（不会清空流之外的内容）。</summary>
    public static async Task<HintClickStore> LoadAsync(
        Stream stream,
        int maxEntries = DefaultMaxEntries,
        CancellationToken cancellationToken = default)
    {
        var times = await HashToTimesReader
            .ReadAsync(stream, maxEntries, cancellationToken)
            .ConfigureAwait(false);
        return new HintClickStore(times);
    }

    /// <summary>把当前内容写回 htt 流。</summary>
    public async Task SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // 先在锁内做一份快照，再在锁外写盘，避免长时间持锁阻塞点击统计。
        HashToTimes snapshot;
        lock (_gate)
        {
            snapshot = _times.Snapshot();
        }

        await HashToTimesReader.WriteAsync(snapshot, stream, cancellationToken).ConfigureAwait(false);
    }
}
