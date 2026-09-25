using System.Collections.Concurrent;

namespace CommandPark;

/// <summary>
/// <see cref="IHintProvider"/> 的默认实现：
/// 每个 <see cref="IHintSource"/> 独立并发枚举，结果先写入线程安全队列，再按批次回调推送。
/// </summary>
public class HintProvider : IHintProvider
{
    /// <summary>
    /// 参与搜索的 Hint 源，可随时增删；每次 <see cref="SearchAsync"/> 开始时会取快照。
    /// </summary>
    public List<IHintSource> Sources { get; } = [];

    /// <summary>通过接口暴露为只读视图，避免调用方拿到具体集合类型。</summary>
    IReadOnlyList<IHintSource> IHintProvider.Sources => Sources;

    /// <inheritdoc />
    public async Task SearchAsync(
        string query,
        Action<IHint[]> onSearched,
        TimeSpan batchSendTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSearched);

        if (batchSendTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSendTime), batchSendTime, "批量推送间隔必须大于 TimeSpan.Zero。");
        }

        // 取快照：搜索期间调用方增删 Sources 既不会影响本次搜索，也不会抛集合修改异常。
        var sources = Sources.ToArray();
        if (sources.Length == 0)
        {
            return;
        }

        var queue = new ConcurrentQueue<IHint>();
        var workers = new Task[sources.Length];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = EnumerateAsync(sources[i], query, queue, cancellationToken);
        }

        var allDone = Task.WhenAll(workers);

        try
        {
            while (!allDone.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 等到"该推送了"或"所有源都结束了"。后者保证结尾不会再多等一个批次间隔，
                // 也就不会把最后一批结果留在队列里丢掉。
                await Task.WhenAny(allDone, Task.Delay(batchSendTime, cancellationToken));

                // 取消之后不再推送任何批次，交给下面的异常分支。
                cancellationToken.ThrowIfCancellationRequested();

                Flush(queue, onSearched);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 收尾冲刷：源结束时队列里可能还剩最后一批结果。
            Flush(queue, onSearched);

            // 把源里的异常冒泡给调用方（此时已产出的批次都已送达）。
            await allDone;
        }
        catch
        {
            // 取消或源异常：不再等待剩余源（源可能忽略取消令牌而导致永久等待），
            // 但要标记异常已观察，避免变成未观察的任务异常。
            ObserveInBackground(allDone);
            throw;
        }
    }

    /// <summary>把单个源的全部结果搬进队列。</summary>
    private static async Task EnumerateAsync(
        IHintSource source,
        string query,
        ConcurrentQueue<IHint> queue,
        CancellationToken cancellationToken)
    {
        await foreach (var hint in source.SearchAsync(query, cancellationToken).WithCancellation(cancellationToken))
        {
            queue.Enqueue(hint);
        }
    }

    /// <summary>
    /// 取空队列并回调一批结果。用 <see cref="ConcurrentQueue{T}.TryDequeue(out IHint)"/> 逐个取出，
    /// 只拿走真正取到的元素，因此不会丢掉并发写入的其它结果。队列为空时不回调。
    /// </summary>
    private static void Flush(ConcurrentQueue<IHint> queue, Action<IHint[]> onSearched)
    {
        if (queue.IsEmpty)
        {
            return;
        }

        var batch = new List<IHint>();
        while (queue.TryDequeue(out var hint))
        {
            batch.Add(hint);
        }

        if (batch.Count > 0)
        {
            onSearched(batch.ToArray());
        }
    }

    /// <summary>不阻塞地观察任务异常。</summary>
    private static void ObserveInBackground(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}