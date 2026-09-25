namespace CommandPark;

/// <summary>
/// Hint 提供者：并发聚合多个 <see cref="IHintSource"/> 的搜索结果，并按批次回调推送。
/// </summary>
public interface IHintProvider
{
    /// <summary>
    /// 参与搜索的 Hint 源。
    /// </summary>
    public IReadOnlyList<IHintSource> Sources { get; }

    /// <summary>
    /// 并发搜索所有 <see cref="Sources"/>，把新结果分批交给 <paramref name="onSearched"/>。
    /// </summary>
    /// <param name="query">搜索关键字，不能为 null。</param>
    /// <param name="onSearched">
    /// 收到一批新 Hint 时的回调，不能为 null。回调串行执行（不会并发回调），且批次内至少包含一个元素；
    /// 若调用方在自己线程上等待本方法，回调也会回到该线程上下文（在 UI 线程调用即可直接更新界面）。
    /// </param>
    /// <param name="batchSendTime">
    /// 相邻两批之间的最大间隔，必须大于 <see cref="TimeSpan.Zero"/>。
    /// 所有源结束后会立即推送最后一批，不会再额外等待一个间隔。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。取消后不再推送新批次，并以 <see cref="OperationCanceledException"/> 结束。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> 或 <paramref name="onSearched"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSendTime"/> 不大于 <see cref="TimeSpan.Zero"/>。</exception>
    public Task SearchAsync(
        string query,
        Action<IHint[]> onSearched,
        TimeSpan batchSendTime,
        CancellationToken cancellationToken = default);
}