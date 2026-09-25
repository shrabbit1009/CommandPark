using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace CommandPark;

/// <summary>
/// 整合搜索 Hint 源，每源并发，通过回调返回新 Hint。
/// </summary>
public class HintProvider
{
    public List<IHintSource> Sources { get; } = [];
    
    public async Task SearchAsync(string query, Action<IHint[]> onSearched, TimeSpan batchSendTime, CancellationToken cancellationToken = default)
    {
        var queue = new ConcurrentBag<IHint>();
        var list  = Sources.Select(s => s.SearchAsync(query, cancellationToken)).ToArray();

        var tasks = new List<Task>(list.Length);
        
        // 批量创建工作
        tasks.AddRange(list.Select(e => EnumeratorWorker(e, queue.Add, cancellationToken)));
        var masterTask = Task.WhenAll(tasks);
        
        // 轮询检查队列
        await PollWorker(queue, onSearched, masterTask, batchSendTime, cancellationToken);
    }

    private async Task EnumeratorWorker(IAsyncEnumerable<IHint> enumerator, Action<IHint> onSearched, CancellationToken cancellationToken)
    {
        await foreach (var e in enumerator.WithCancellation(cancellationToken))
        {
            onSearched.Invoke(e);
        }
    }

    private async Task PollWorker(ConcurrentBag<IHint> inCollection, Action<IHint[]> onSearched, Task masterTask, TimeSpan batchSendTime, CancellationToken cancellationToken)
    {
        // 等待主任务完成，其中轮询检查并推送
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!inCollection.IsEmpty)
            {
                var arr = inCollection.ToArray();
                inCollection.Clear();
                onSearched.Invoke(arr);
            }
            
            await Task.Delay(batchSendTime, cancellationToken);
            
        } while (!masterTask.IsCompleted);
    }
}