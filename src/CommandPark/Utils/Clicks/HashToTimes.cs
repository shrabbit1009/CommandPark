using System.Collections;

namespace CommandPark.Utils.Clicks;

/// <summary>
/// SHA256 → 点击次数的有序映射（htt 格式的内存表示）。
/// <para>
/// 除了键值对本身，还按“首次插入顺序”额外维护一条队列，
/// 因此超过 <see cref="MaxEntries"/> 时总能稳定地淘汰最旧的条目（从前开始删），
/// 写回文件时也按同一顺序落盘。
/// </para>
/// <para>
/// 全程只保存摘要与整数，不保存任何 Hint 明文，无法由本地 .htt 反推用户点击了什么。
/// </para>
/// </summary>
public sealed class HashToTimes : IEnumerable<KeyValuePair<SHA256Store, int>>
{
    /// <summary>默认最多保留的条目数。</summary>
    public const int DefaultMaxEntries = 10_000;

    private readonly Dictionary<SHA256Store, int> _values = [];
    private readonly List<SHA256Store> _order = new();

    private int _head;          // 逻辑队首在 _order 中的下标
    private int _maxEntries = DefaultMaxEntries;

    public HashToTimes(int maxEntries = DefaultMaxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    /// <summary>最多保留的条目数；调小会立刻按最旧优先淘汰多余条目。</summary>
    public int MaxEntries
    {
        get => _maxEntries;
        set
        {
            _maxEntries = Math.Max(1, value);
            TrimToMax();
        }
    }

    /// <summary>当前条目数。</summary>
    public int Count => _values.Count;

    /// <summary>读取/写入某个摘要的点击次数；写入新键会排到队尾。</summary>
    public int this[SHA256Store key]
    {
        get => _values[key];
        set => Set(key, value);
    }

    public bool TryGetValue(SHA256Store key, out int value) => _values.TryGetValue(key, out value);

    public bool ContainsKey(SHA256Store key) => _values.ContainsKey(key);

    /// <summary>写入或覆盖一个计数；新键按首次插入顺序排队，必要时淘汰最旧项。</summary>
    public void Set(SHA256Store key, int value)
    {
        if (_values.TryAdd(key, value))
        {
            _order.Add(key);
            if (_values.Count > _maxEntries)
            {
                TrimToMax();
            }

            return;
        }

        _values[key] = value;
    }

    /// <summary>
    /// 累加点击次数（默认 +1）。已有键只更新计数、不会改变顺序；
    /// 新键排到队尾，超过上限时从队首淘汰。返回累加后的计数。
    /// </summary>
    public int Add(SHA256Store key, int delta = 1)
    {
        if (_values.TryGetValue(key, out var current))
        {
            current += delta;
            _values[key] = current;
            return current;
        }

        _values[key] = delta;
        _order.Add(key);
        if (_values.Count > _maxEntries)
        {
            TrimToMax();
        }

        return delta;
    }

    /// <summary>复制一份独立实例，用于在并发场景下安全地快照后写盘。</summary>
    public HashToTimes Snapshot()
    {
        var copy = new HashToTimes(MaxEntries);
        foreach (var (key, value) in this)
        {
            copy.Set(key, value);
        }

        return copy;
    }

    /// <summary>按首次插入顺序枚举（最旧在前）；返回结构体枚举器，热路径不产生分配。</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<SHA256Store, int>> IEnumerable<KeyValuePair<SHA256Store, int>>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void TrimToMax()
    {
        while (_values.Count > _maxEntries && _head < _order.Count)
        {
            _values.Remove(_order[_head]);
            _order[_head] = default;
            _head++;
        }

        // 已全部淘汰：直接复位。否则在头部空洞太多时压缩一次，避免 _order 无限增长。
        if (_head >= _order.Count)
        {
            _order.Clear();
            _head = 0;
        }
        else if (_head > 64 && _head * 2 > _order.Count)
        {
            _order.RemoveRange(0, _head);
            _head = 0;
        }
    }

    /// <summary><see cref="HashToTimes"/> 的结构体枚举器，按插入顺序遍历。</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<SHA256Store, int>>
    {
        private readonly HashToTimes _owner;
        private int _index;

        internal Enumerator(HashToTimes owner)
        {
            _owner = owner;
            _index = owner._head - 1;
        }

        public KeyValuePair<SHA256Store, int> Current
        {
            get
            {
                var key = _owner._order[_index];
                return new KeyValuePair<SHA256Store, int>(key, _owner._values[key]);
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _owner._order.Count;

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
