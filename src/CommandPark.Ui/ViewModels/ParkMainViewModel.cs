using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandPark.Ui.ViewModels;

/// <summary>
/// 搜索主界面视图模型。
/// <para>
/// 首屏速度优先：构造函数不做任何 IO；Hint 源在第一次真正搜索时才在线程池上懒加载。
/// </para>
/// <para>
/// 交互上的三个刻意设计：
/// 1) 搜索期间保留上一次的结果，直到新结果到达才整体替换，避免闪白；
/// 2) "搜索中"只在搜索确实超过 <see cref="SlowSearchThreshold"/> 且还没有结果时才出现，避免每次按键闪一下；
/// 3) 结果流式回填：拿到一批就显示一批，不等全部搜索完。
/// </para>
/// </summary>
public partial class ParkMainViewModel : ViewModelBase
{
    /// <summary>输入防抖时间：连续快速输入只会在停顿后发起一次搜索。</summary>
    public static readonly TimeSpan DebounceTime = TimeSpan.FromMilliseconds(120);

    /// <summary>结果分批回填界面的最大间隔。</summary>
    public static readonly TimeSpan BatchSendTime = TimeSpan.FromMilliseconds(50);

    /// <summary>超过这个时长仍没有结果，才提示"搜索中"（避免快速搜索闪烁）。</summary>
    public static readonly TimeSpan SlowSearchThreshold = TimeSpan.FromMilliseconds(300);

    /// <summary>最多显示的结果条数，避免源返回过多条目拖慢界面。</summary>
    public const int MaxResults = 200;

    private readonly Func<IHintProvider> _providerFactory;
    private readonly Action<Action> _dispatch;

    private Task<IHintProvider>? _providerTask;
    private CancellationTokenSource? _searchCts;
    private bool _homeInitializationStarted;

    /// <summary>默认构造：使用内置默认源，供设计期与默认运行路径使用。</summary>
    public ParkMainViewModel() : this(null, null)
    {
    }

    /// <param name="providerFactory">
    /// 延迟创建 Hint 提供者的工厂。不会在构造函数里被调用（首屏速度优先），
    /// 直到第一次发起搜索时才在线程池上创建。
    /// </param>
    /// <param name="dispatch">
    /// 把回调切回 UI 线程的方式，默认使用 Avalonia Dispatcher；可注入以便测试。
    /// </param>
    public ParkMainViewModel(Func<IHintProvider>? providerFactory, Action<Action>? dispatch = null)
    {
        _providerFactory = providerFactory ?? CreateDefaultProvider;
        _dispatch = dispatch ?? (static action => Dispatcher.UIThread.Post(action));

        Results.CollectionChanged += (_, _) =>
        {
            // 列表内容变化时驱动 HasResults（决定结果列表显示与否）
            OnPropertyChanged(nameof(HasResults));

            if (Results.Count == 0)
            {
                SelectedIndex = -1;
            }
            else if (SelectedIndex < 0)
            {
                // 新内容到达后默认选中第一条，回车即可执行
                SelectedIndex = 0;
            }
        };
    }

    /// <summary>搜索结果，绑定到结果列表。</summary>
    public ObservableCollection<HintItemViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    /// <summary>输入为空（初始态）：显示"输入关键字开始搜索"。</summary>
    [ObservableProperty]
    public partial bool IsIdle { get; set; } = true;

    /// <summary>搜索偏慢时才为 true：显示"搜索中…"。</summary>
    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    /// <summary>搜索结束且没有任何结果内容时才为 true：显示"没有找到匹配的结果"。</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否显示右侧的点击权重与快速选择（来自 commandpark.json 的 quickSelect.enabled，默认开启）。
    /// </summary>
    [ObservableProperty]
    public partial bool QuickSelectEnabled { get; set; } = true;

    /// <summary>快速选择使用的修饰键（默认 Alt，可配 Ctrl）。</summary>
    [ObservableProperty]
    public partial QuickSelectModifier QuickSelectModifier { get; set; } = QuickSelectConfig.DefaultModifier;

    /// <summary>
    /// 是否在结果右侧显示点击权重（来自 commandpark.json 的 quickSelect.showWeight，默认关闭）。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowWeightEnabled { get; set; }

    /// <summary>
    /// 是否额外支持直接按 F1~F9 打开对应条目（来自 quickSelect.functionKeys，默认关闭）。
    /// </summary>
    [ObservableProperty]
    public partial bool FunctionKeysEnabled { get; set; }

    /// <summary>是否有结果可显示（由 <see cref="Results"/> 集合驱动）。</summary>
    public bool HasResults => Results.Count > 0;

    public bool IsFailed => ErrorMessage is not null;

    /// <summary>任意条目执行失败时触发（供界面弹出错误窗口）。</summary>
    public event EventHandler<HintRunFailedEventArgs>? RunFailed;

    /// <summary>任意条目请求显示详情时触发（供界面打开详情窗口）。</summary>
    public event EventHandler<HintDetailsRequestedEventArgs>? DetailsRequested;

    /// <summary>最近一次搜索任务（含防抖与批次回填），主要供测试等待。</summary>
    public Task? CurrentSearch { get; private set; }

    /// <summary>当前选中的结果下标（与结果列表的 SelectedIndex 双向绑定，-1 表示未选中）。</summary>
    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    /// <summary>↑↓ 移动选中项；到边界就停住，不循环。</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        var target = SelectedIndex < 0 ? 0 : SelectedIndex + delta;
        SelectedIndex = Math.Clamp(target, 0, Results.Count - 1);
    }

    /// <summary>
    /// 执行当前选中的命令。返回是否执行成功——调用方据此决定 Enter 之后要不要关闭窗口。
    /// </summary>
    public async Task<bool> RunSelectedAsync()
    {
        var index = SelectedIndex;
        if (index < 0 || index >= Results.Count)
        {
            return false;
        }

        // 异常由 HintItemViewModel 内部拦截并转成 RunFailed 事件（会弹出错误窗口）
        return await Results[index].TryRunAsync();
    }

    /// <summary>执行第 <paramref name="index"/> 条结果（0 起），供 1~9 快速选择使用。</summary>
    public async Task<bool> RunIndexAsync(int index)
    {
        if (index < 0 || index >= Results.Count)
        {
            return false;
        }

        SelectedIndex = index;
        return await RunSelectedAsync();
    }

    /// <summary>在窗口首次加载时显示首屏源；只执行一次。</summary>
    public Task InitializeHomeAsync()
    {
        if (_homeInitializationStarted || !string.IsNullOrWhiteSpace(InputText))
        {
            return CurrentSearch ?? Task.CompletedTask;
        }

        _homeInitializationStarted = true;
        var cts = CreateSearchCancellation();
        StartSearch(string.Empty, cts, isHome: true);
        return CurrentSearch ?? Task.CompletedTask;
    }

    partial void OnInputTextChanged(string value)
    {
        var cts = CreateSearchCancellation();
        var query = value.Trim();

        if (query.Length == 0)
        {
            _homeInitializationStarted = true;
            StartSearch(string.Empty, cts, isHome: true);
            return;
        }

        StartSearch(query, cts, isHome: false);
    }

    private CancellationTokenSource CreateSearchCancellation()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, cts);
        TryCancel(previous);
        return cts;
    }

    private void StartSearch(string query, CancellationTokenSource cts, bool isHome)
    {
        ErrorMessage = null;
        IsEmpty = false;
        IsIdle = isHome && Results.Count == 0;
        CurrentSearch = RunSearchAsync(query, cts, isHome);
    }

    private async Task RunSearchAsync(string query, CancellationTokenSource cts, bool isHome)
    {
        var token = cts.Token;
        var session = new SearchSession();

        try
        {
            // 首屏只加载本地的欢迎项，不做防抖；普通搜索保留防抖。
            if (!isHome)
            {
                await Task.Delay(DebounceTime, token);
            }

            // "搜索中"计时从这里开始：把懒加载 Provider 的耗时也算进去
            var slowIndicatorTask = Task.Delay(SlowSearchThreshold, token);

            // 提供者懒加载：第一次搜索时才创建，且不占用 UI 线程
            var providerTask = _providerTask ??= Task.Run(_providerFactory, CancellationToken.None);
            var provider = await providerTask;

            // 搜索放线程池：Hint 源可能做同步 IO 或 CPU 计算，不能占用 UI 线程
            var searchTask = Task.Run(
                () => provider.SearchAsync(query, batch => AppendBatch(batch, token, session), BatchSendTime, token),
                token);

            // 慢搜索才提示：要么搜索先结束，要么到了提示阈值还没结束
            await Task.WhenAny(searchTask, slowIndicatorTask);
            if (!searchTask.IsCompleted && !session.HasResults)
            {
                IsSearching = true;
            }

            // 先等搜索结果，再等已 Post 出去的批次落地，状态才不会与列表内容不一致
            await searchTask;
            await DrainAsync();

            token.ThrowIfCancellationRequested();
            IsSearching = false;

            if (!session.HasResults)
            {
                if (isHome)
                {
                    Results.Clear();
                    IsIdle = Results.Count == 0;
                    IsEmpty = false;
                }
                else
                {
                    // 普通搜索没有结果时，只有列表本来就空才提示"没有匹配"
                    IsEmpty = Results.Count == 0;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 被新的输入取消：界面与列表都交给新的搜索接管
        }
        catch (Exception exception)
        {
            await DrainAsync();
            IsSearching = false;
            ErrorMessage = exception.Message;
        }
        finally
        {
            cts.Dispose();
        }
    }


    /// <summary>把一批结果切回 UI 线程追加到列表（回调来自线程池线程）。</summary>
    private void AppendBatch(IHint[] batch, CancellationToken token, SearchSession session)
    {
        _dispatch(() =>
        {
            // 迟到的批次（搜索已被新输入取消）直接丢弃，避免污染新结果
            if (token.IsCancellationRequested)
            {
                return;
            }

            // 第一批新结果到达时才整体替换旧内容：搜索期间界面保持上一次的结果
            if (!session.Replaced)
            {
                Results.Clear();
                session.Replaced = true;
            }

            foreach (var hint in batch)
            {
                if (Results.Count >= MaxResults)
                {
                    break;
                }

                var item = new HintItemViewModel(hint);
                item.RunFailed += OnItemRunFailed;
                item.DetailsRequested += OnItemDetailsRequested;
                item.Activated += OnItemActivated;
                ApplyQuickSelectMeta(item, Results.Count);
                Results.Add(item);
            }

            session.HasResults = Results.Count > 0;
            IsSearching = false;   // 已经有内容可看，不必再提示"搜索中"
        });
    }

    private void OnItemRunFailed(object? sender, HintRunFailedEventArgs args) => RunFailed?.Invoke(this, args);

    private void OnItemDetailsRequested(object? sender, HintDetailsRequestedEventArgs args) => DetailsRequested?.Invoke(this, args);

    /// <summary>累计点击权重：点击越多，下次搜索时该条目的排序越靠前。</summary>
    private static void OnItemActivated(object? sender, HintActivatedEventArgs args) => ClickHistory.Store.RecordClick(args.Hint);

    /// <summary>按条目在结果中的位置（0 起）填充右侧的点击权重与快速选择编号。</summary>
    private void ApplyQuickSelectMeta(HintItemViewModel item, int index)
    {
        item.ShowMeta = QuickSelectEnabled;
        item.WeightEnabled = ShowWeightEnabled;

        if (!QuickSelectEnabled)
        {
            item.Weight = 0;
            item.QuickSelectKey = null;
            return;
        }

        item.Weight = ShowWeightEnabled ? ClickHistory.Store.GetClickCount(item.Hint) : 0;
        item.QuickSelectKey = QuickSelectConfig.GetKey(index);
    }

    partial void OnQuickSelectEnabledChanged(bool value) => RefreshQuickSelectMeta();

    partial void OnShowWeightEnabledChanged(bool value) => RefreshQuickSelectMeta();

    private void RefreshQuickSelectMeta()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            ApplyQuickSelectMeta(Results[i], i);
        }
    }

    /// <summary>
    /// 取消上一次搜索。该 CTS 可能已经被它所属的搜索在 finally 里释放，
    /// 因此这里必须容错，不能让它影响新的搜索。
    /// </summary>
    private static void TryCancel(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or AggregateException)
        {
            // 已经结束并释放 / 回调异常聚合：都不影响新搜索
        }
    }

    /// <summary>等待之前 Post 出去的回调按序执行完。</summary>
    private async Task DrainAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatch(() => drained.TrySetResult());
        await drained.Task;
    }

    private static IHintProvider CreateDefaultProvider()
    {
        // 配置文件和真实源都可能做 IO，因此只在第一次搜索时、在线程池上创建。
        // 程序目录中的 commandpark.json 会决定哪些实现 IConfigReader 的源启用；
        // ClickHistory.Store 提供基于点击次数的附加排序权重。
        return ConfigurableHintProviderFactory.Create(weightProvider: ClickHistory.Store);
    }

    /// <summary>一次搜索会话的共享状态（只在 UI 线程上读写）。</summary>
    private sealed class SearchSession
    {
        /// <summary>是否已经用新结果替换过旧内容。</summary>
        public bool Replaced { get; set; }

        /// <summary>本次搜索是否已经拿到过结果。</summary>
        public bool HasResults { get; set; }
    }
}
