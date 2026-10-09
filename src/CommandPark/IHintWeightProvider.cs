namespace CommandPark;

/// <summary>
/// 为搜索结果提供附加排序权重。返回值为叠加到 <see cref="IHint.Similarity"/> 上的增量，
/// 0 表示不改变原排序。
/// </summary>
public interface IHintWeightProvider
{
    /// <summary>计算某个 Hint 的附加权重。</summary>
    float GetWeight(IHint hint);
}
