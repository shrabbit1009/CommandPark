using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(HintProvider))]
public class HintProviderTest
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(20);

    private static IHint Hint(string id) => new Hint(id, id, id);

    // ------------------------------------------------------------------
    // 基本行为：结果不丢、不多、不重复
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithSingleSource_DeliversAllHints()
    {
        const string query = "shrabbit";
        var compare = await new TestHintSource().SearchAsync(query).ToListAsync();

        var provider = new HintProvider
        {
            Sources = { new TestHintSource() }
        };

        var batches = new BatchCollector();
        await provider.SearchAsync(query, batches.Add, TimeSpan.FromMilliseconds(15));

        // 并发查询可能无法准确返回顺序
        Assert.Equivalent(compare, batches.All);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleSources_MergesAllHints()
    {
        const string query = "shrabbit";
        var compare = new List<IHint>();
        foreach (var source in new IHintSource[] { new MockHintSource1(), new MockHintSource2(), new MockHintSource3() })
        {
            compare.AddRange(await source.SearchAsync(query).ToListAsync());
        }

        var provider = new HintProvider
        {
            Sources =
            {
                new MockHintSource1(),
                new MockHintSource2(),
                new MockHintSource3()
            }
        };

        var batches = new BatchCollector();
        await provider.SearchAsync(query, batches.Add, TimeSpan.FromMilliseconds(15));

        // 并发查询可能无法准确返回顺序
        Assert.Equivalent(compare, batches.All);
    }

    [Fact]
    public async Task SearchAsync_WithoutSources_CompletesWithoutCallback()
    {
        var provider = new HintProvider();
        var batches = new BatchCollector();

        await provider.SearchAsync("q", batches.Add, Fast);

        Assert.Empty(batches.Batches);
    }

    // ------------------------------------------------------------------
    // 批次行为
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WhenSourceFinishesWithinLastBatch_DoesNotLoseLastBatch()
    {
        // 源在"最后一个批次间隔"中途结束：结束时队列里的最后一批必须补推，不能丢。
        var source = new ScriptedHintSource(
            (TimeSpan.Zero, "a"),
            (TimeSpan.FromMilliseconds(35), "b"));
        var provider = new HintProvider { Sources = { source } };

        var batches = new BatchCollector();
        await provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(20));

        Assert.Equal(new[] { "a", "b" }, batches.Ids);
    }

    [Fact]
    public async Task SearchAsync_WithFastSources_SendsSingleNonEmptyBatch()
    {
        var provider = new HintProvider
        {
            Sources = { new MockHintSource1(), new MockHintSource2() }
        };

        var batches = new BatchCollector();
        await provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(500));

        // 源已全部结束，不该为了凑满批次间隔而多等
        Assert.Single(batches.Batches);
        Assert.Equal(6, batches.HintCount);
    }

    [Fact]
    public async Task SearchAsync_WithSlowSource_StreamsMultipleBatches()
    {
        var source = new ScriptedHintSource(
            (TimeSpan.Zero, "a"),
            (TimeSpan.FromMilliseconds(30), "b"),
            (TimeSpan.FromMilliseconds(30), "c"));
        var provider = new HintProvider { Sources = { source } };

        var batches = new BatchCollector();
        await provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(10));

        Assert.Equal(new[] { "a", "b", "c" }, batches.Ids);
        Assert.True(batches.Count >= 2, $"慢源应分多批推送，实际只有 {batches.Count} 批");
        Assert.All(batches.Batches, batch => Assert.NotEmpty(batch));
    }

    [Fact]
    public async Task SearchAsync_HighVolume_DoesNotDropOrDuplicateHints()
    {
        const int perSource = 200;
        var provider = new HintProvider
        {
            Sources =
            {
                new BulkHintSource("s1", perSource),
                new BulkHintSource("s2", perSource),
                new BulkHintSource("s3", perSource)
            }
        };

        var batches = new BatchCollector();
        await provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(1));

        // 结果入队与"取空队列"是并发的，这里必须一个不丢、一个不重
        Assert.Equal(perSource * 3, batches.HintCount);
        Assert.Equal(perSource * 3, batches.Ids.Distinct().Count());
    }

    // ------------------------------------------------------------------
    // 并发
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_EnumeratesSourcesConcurrently()
    {
        const int sourceCount = 3;
        var rendezvous = new Rendezvous(sourceCount);
        var provider = new HintProvider
        {
            Sources =
            {
                new RendezvousHintSource("a", rendezvous),
                new RendezvousHintSource("b", rendezvous),
                new RendezvousHintSource("c", rendezvous)
            }
        };

        var batches = new BatchCollector();

        // 若源是串行枚举的，第一个源会一直等不到其它源而超时，本测试即失败。
        await provider.SearchAsync("q", batches.Add, Fast).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "a", "b", "c" }, batches.Ids);
    }

    [Fact]
    public async Task SearchAsync_PassesQueryToEverySource()
    {
        var first = new RecordingHintSource("a");
        var second = new RecordingHintSource("b");
        var provider = new HintProvider { Sources = { first, second } };

        await provider.SearchAsync("shrabbit", new BatchCollector().Add, Fast);

        Assert.Equal("shrabbit", first.LastQuery);
        Assert.Equal("shrabbit", second.LastQuery);
    }

    // ------------------------------------------------------------------
    // 取消
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WhenCancelled_ThrowsAndStopsDeliveringBatches()
    {
        using var cts = new CancellationTokenSource();
        var source = new ScriptedHintSource(
            (TimeSpan.Zero, "a"),
            (TimeSpan.FromSeconds(30), "b"));
        var provider = new HintProvider { Sources = { source } };
        var batches = new BatchCollector();

        var search = provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(10), cts.Token);
        await source.Started;
        cts.Cancel();

        await AssertThrowsWithinAsync<OperationCanceledException>(() => search);

        Assert.DoesNotContain("b", batches.Ids);

        // 方法返回后不应再有任何"迟到"的回调
        var delivered = batches.Count;
        await Task.Delay(30);
        Assert.Equal(delivered, batches.Count);
    }

    [Fact]
    public async Task SearchAsync_WhenTokenAlreadyCancelled_DoesNotInvokeCallback()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var provider = new HintProvider { Sources = { new ScriptedHintSource((TimeSpan.Zero, "a")) } };
        var batches = new BatchCollector();

        await AssertThrowsWithinAsync<OperationCanceledException>(
            () => provider.SearchAsync("q", batches.Add, Fast, cts.Token));

        Assert.Empty(batches.Batches);
    }

    [Fact]
    public async Task SearchAsync_WhenCancelledAndSourceIgnoresToken_ReturnsPromptly()
    {
        using var cts = new CancellationTokenSource();
        var source = new StubbornHintSource();
        var provider = new HintProvider { Sources = { source } };
        var batches = new BatchCollector();

        var search = provider.SearchAsync("q", batches.Add, TimeSpan.FromMilliseconds(10), cts.Token);
        await source.Started;
        cts.Cancel();

        // 源忽略取消令牌也不能让提供者一直等下去
        await AssertThrowsWithinAsync<OperationCanceledException>(() => search);

        Assert.Empty(batches.Batches);
        source.Release();
    }

    // ------------------------------------------------------------------
    // 异常与参数校验
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WhenSourceThrows_PropagatesExceptionAndKeepsDeliveredBatches()
    {
        var provider = new HintProvider
        {
            Sources = { new ScriptedHintSource((TimeSpan.Zero, "a")), new FailingHintSource() }
        };
        var batches = new BatchCollector();

        var exception = await AssertThrowsWithinAsync<InvalidOperationException>(
            () => provider.SearchAsync("q", batches.Add, Fast));

        Assert.Equal("boom", exception.Message);

        // 出错的源已经产出的结果、以及其它源的结果，都先送达给了调用方
        Assert.Equal(new[] { "a", "partial" }, batches.Ids);
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var provider = new HintProvider();

        await AssertThrowsWithinAsync<ArgumentNullException>(
            () => provider.SearchAsync(null!, _ => { }, Fast));
    }

    [Fact]
    public async Task SearchAsync_WithNullCallback_ThrowsArgumentNullException()
    {
        var provider = new HintProvider();

        await AssertThrowsWithinAsync<ArgumentNullException>(
            () => provider.SearchAsync("q", null!, Fast));
    }

    /// <param name="milliseconds">
    /// -1ms 就是 <see cref="Timeout.InfiniteTimeSpan"/>，缺少校验的实现会在这里永久挂起，
    /// 所以本测试通过带超时的 <see cref="AssertThrowsWithinAsync{TException}"/> 断言，超时即失败而不是挂死。
    /// </param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public async Task SearchAsync_WithNonPositiveBatchSendTime_ThrowsArgumentOutOfRangeException(int milliseconds)
    {
        var provider = new HintProvider { Sources = { new ScriptedHintSource((TimeSpan.Zero, "a")) } };

        await AssertThrowsWithinAsync<ArgumentOutOfRangeException>(
            () => provider.SearchAsync("q", _ => { }, TimeSpan.FromMilliseconds(milliseconds)));
    }

    // ------------------------------------------------------------------
    // 接口
    // ------------------------------------------------------------------

    [Fact]
    public async Task Provider_CanBeUsedThroughInterface()
    {
        IHintProvider provider = new HintProvider { Sources = { new MockHintSource1() } };
        var batches = new BatchCollector();

        await provider.SearchAsync("q", batches.Add, Fast);

        Assert.Equal(3, batches.HintCount);
        Assert.Single(provider.Sources);
    }

    [Fact]
    public void Provider_ExposesSourcesAsReadOnlyInterface()
    {
        var provider = new HintProvider { Sources = { new MockHintSource1() } };

        // 通过接口拿到的是只读视图，实际就是同一个集合
        Assert.Same(provider.Sources, ((IHintProvider)provider).Sources);
    }

    // ------------------------------------------------------------------
    // 测试基础设施
    // ------------------------------------------------------------------

    /// <summary>等待任务结束并断言异常类型，超时则直接失败（避免测试挂死）。</summary>
    private static async Task<TException> AssertThrowsWithinAsync<TException>(
        Func<Task> action,
        int timeoutSeconds = 5)
        where TException : Exception
    {
        // 注意：超时用的 Task.Delay 不能复用被测任务的取消令牌（已取消的令牌会让它立刻完成，导致误判）。
        var task = action();
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        Assert.True(ReferenceEquals(task, finished), $"任务在 {timeoutSeconds}s 内未结束（可能卡死）");

        return await Assert.ThrowsAnyAsync<TException>(() => task);
    }

    /// <summary>线程安全的批次收集器：回调可能来自线程池线程。</summary>
    private sealed class BatchCollector
    {
        private readonly object _gate = new();
        private readonly List<IHint[]> _batches = [];

        public void Add(IHint[] batch)
        {
            lock (_gate)
            {
                _batches.Add(batch);
            }
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _batches.Count;
                }
            }
        }

        public int HintCount
        {
            get
            {
                lock (_gate)
                {
                    return _batches.Sum(batch => batch.Length);
                }
            }
        }

        public IHint[][] Batches
        {
            get
            {
                lock (_gate)
                {
                    return [.. _batches];
                }
            }
        }

        public IHint[] All
        {
            get
            {
                lock (_gate)
                {
                    return _batches.SelectMany(batch => batch).ToArray();
                }
            }
        }

        public string[] Ids
        {
            get
            {
                lock (_gate)
                {
                    return _batches.SelectMany(batch => batch).Select(hint => hint.Id).Order().ToArray();
                }
            }
        }
    }

    /// <summary>按脚本 (延迟, Id) 依次产出结果的测试源。</summary>
    private sealed class ScriptedHintSource(params (TimeSpan Delay, string Id)[] steps) : IHintSource
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();

            foreach (var (delay, id) in steps)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                yield return Hint(id);
            }
        }
    }

    /// <summary>产出固定数量的结果，并在中途让出线程，制造与消费者交错的场景。</summary>
    private sealed class BulkHintSource(string prefix, int count) : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < count; i++)
            {
                if (i % 50 == 0)
                {
                    await Task.Yield();
                }

                yield return Hint($"{prefix}-{i}");
            }
        }
    }

    /// <summary>记录收到的 query。</summary>
    private sealed class RecordingHintSource(string id) : IHintSource
    {
        public string? LastQuery { get; private set; }

        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            await Task.Yield();
            yield return Hint(id);
        }
    }

    /// <summary>先产出一个结果，然后抛异常。</summary>
    private sealed class FailingHintSource : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Hint("partial");
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>完全忽略取消令牌的源。</summary>
    private sealed class StubbornHintSource : IHintSource
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task;
            yield return Hint("late");
        }
    }

    /// <summary>等待所有源都开始后才继续的集合点。</summary>
    private sealed class Rendezvous(int expected)
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public void Arrive()
        {
            if (Interlocked.Increment(ref _count) == expected)
            {
                _allArrived.TrySetResult();
            }
        }

        public Task WaitAllAsync(CancellationToken cancellationToken) =>
            _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
    }

    /// <summary>串行执行时会因为等不到其它源而超时。</summary>
    private sealed class RendezvousHintSource(string id, Rendezvous rendezvous) : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            rendezvous.Arrive();
            await rendezvous.WaitAllAsync(cancellationToken);
            yield return Hint(id);
        }
    }

    public class MockHintSource1 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Hint("a");
            yield return Hint("b");
            yield return Hint("c");
        }
    }

    public class MockHintSource2 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Hint("d");
            yield return Hint("e");
            yield return Hint("f");
        }
    }

    public class MockHintSource3 : IHintSource
    {
        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return Hint("g");
            yield return Hint("h");
            yield return Hint("i");
        }
    }
}