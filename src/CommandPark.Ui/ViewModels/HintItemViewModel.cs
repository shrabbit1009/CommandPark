using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;

namespace CommandPark.Ui.ViewModels;

public partial class HintItemViewModel : ViewModelBase
{
    /// <summary>默认 Lucide 图标（程序图标加载中/缺失时也用它兜底）。</summary>
    public const LucideIconKind DefaultLucideKind = LucideIconKind.Terminal;

    /// <summary>Hint 没有给出图标时使用的默认图标。</summary>
    public static IHintIcon DefaultIcon { get; } = new LucideHintIcon(DefaultLucideKind);

    /// <summary>图标解码/提取结果的缓存（本地图片与程序图标共用，按路径缓存）。</summary>
    private static readonly ConcurrentDictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>限制并发解码/提取数量，避免一次涌入大量结果时抢占 IO 与 CPU。</summary>
    private static readonly SemaphoreSlim IconLoadLimiter = new(initialCount: 4, maxCount: 4);

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>设计期/预览用的空实例。</summary>
    public HintItemViewModel() : this(new Hint())
    {
    }

    public HintItemViewModel(IHint hint)
    {
        Hint = hint;
        Name = hint.Name;
        Description = hint.Description;
        Icon = hint.Icon ?? ResolveIcon(hint) ?? ErrorIcon(hint);
    }

    public IHint Hint { get; init; }

    /// <summary>
    /// 条目图标（<see cref="IHintIcon"/>）。HintItem 按实现类型选择控件渲染：
    /// <see cref="LucideHintIcon"/> → LucideIcon；<see cref="LocalImageHintIcon"/>、
    /// <see cref="ExecutableHintIcon"/>、<see cref="FileIconHintIcon"/>、<see cref="ThumbnailHintIcon"/>
    /// → Image（都在线程池上解码/提取）。设为 null 时整列图标收起。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LucideKind))]
    [NotifyPropertyChangedFor(nameof(ShowLucideIcon))]
    [NotifyPropertyChangedFor(nameof(ShowImageIcon))]
    public partial IHintIcon? Icon { get; set; }

    /// <summary>
    /// Lucide 图标种类。图片类图标（本地图片/程序图标）在还没加载出来时，
    /// 先用默认图标兜底，避免图标位置空一块。<see cref="LucideIdHintIcon"/> 的 Id
    /// 按 <see cref="LucideIconKind"/> 枚举名解析，大小写不敏感，非法值回落 Terminal。
    /// </summary>
    public LucideIconKind? LucideKind => Icon switch
    {
        LucideHintIcon lucide   => lucide.IconKind,
        LucideIdHintIcon lucide => ParseLucideKind(lucide.Id),
        null                    => null,
        _                       => IconImage is null ? DefaultLucideKind : null,
    };

    /// <summary>是否显示 LucideIcon 控件。</summary>
    public bool ShowLucideIcon => LucideKind is not null;

    /// <summary>是否显示 Image 控件（位图已经就绪）。</summary>
    public bool ShowImageIcon => IconImage is not null;

    /// <summary>Image 控件的数据源；尚未加载完成时为 null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LucideKind))]
    [NotifyPropertyChangedFor(nameof(ShowLucideIcon))]
    [NotifyPropertyChangedFor(nameof(ShowImageIcon))]
    public partial Bitmap? IconImage { get; set; }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否显示右侧的权重与快速选择（由 commandpark.json 的 quickSelect.enabled 决定）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuickSelect))]
    public partial bool ShowMeta { get; set; }

    /// <summary>该条目的点击权重（点击数），0 表示没有点击历史。</summary>
    [ObservableProperty]
    public partial int Weight { get; set; }

    /// <summary>快速选择编号（1~9）；超出前 9 条或未开启快速选择时为 null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuickSelect))]
    public partial int? QuickSelectKey { get; set; }

    /// <summary>是否显示快速选择编号。</summary>
    public bool ShowQuickSelect => ShowMeta && QuickSelectKey is > 0;

    /// <summary>执行命令失败时触发。异常在这里被拦住，不会冒到 UI 线程导致崩溃。</summary>
    public event EventHandler<HintRunFailedEventArgs>? RunFailed;

    /// <summary>请求显示 Hint 完整详情时触发。</summary>
    public event EventHandler<HintDetailsRequestedEventArgs>? DetailsRequested;

    /// <summary>
    /// 用户激活本条 Hint（鼠标点击或回车执行）时触发，用于累计点击权重。
    /// 无论执行成功、失败还是转为查看详情，都会先触发一次。
    /// </summary>
    public event EventHandler<HintActivatedEventArgs>? Activated;

    [RelayCommand]
    public Task Run() => TryRunAsync();

    /// <summary>
    /// 执行 Hint。所有异常都在这里拦截并转成 <see cref="RunFailed"/> 事件，返回是否执行成功
    /// （调用方据此决定要不要关闭窗口）。主动取消不算错误，不弹提示。
    /// </summary>
    public async Task<bool> TryRunAsync()
    {
        // 用户确实激活了这条结果：先记一次点击，再执行 / 查看详情。
        Activated?.Invoke(this, new HintActivatedEventArgs(Hint));

        if (TryRequestDetails())
        {
            return false;
        }

        try
        {
            await Hint.Run(_cancellationTokenSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            try
            {
                RunFailed?.Invoke(this, new HintRunFailedEventArgs(Hint, exception));
            }
            catch (Exception)
            {
                // 通知回调自己出错，也不能把执行路径带崩
            }

            return false;
        }
    }

    private bool TryRequestDetails()
    {
        if (Hint is not IDetailedHint detailed ||
            string.IsNullOrWhiteSpace(detailed.Details) ||
            DetailsRequested is null)
        {
            return false;
        }

        DetailsRequested.Invoke(this, new HintDetailsRequestedEventArgs(Hint, detailed.Details));
        return true;
    }

    [RelayCommand]
    public void Cancel() => _cancellationTokenSource.Cancel();

    partial void OnIconChanged(IHintIcon? value)
    {
        IconImage = null;

        switch (value)
        {
            case LocalImageHintIcon localImage:
                _ = LoadIconImageAsync($"image:{localImage.Path}", localImage.Path, TryDecodeImageFile);
                break;

            case ExecutableHintIcon executable:
                _ = LoadIconImageAsync($"exe:{executable.Path}", executable.Path, ProgramIconExtractor.TryExtract);
                break;

            case FileIconHintIcon fileIcon:
                _ = LoadIconImageAsync($"icon:{fileIcon.Path}", fileIcon.Path, path => ShellIconExtractor.TryGetIcon(path));
                break;

            case ThumbnailHintIcon thumbnail:
                _ = LoadIconImageAsync($"thumb:{thumbnail.Path}", thumbnail.Path, LoadThumbnailOrIcon);
                break;
        }
    }

    /// <summary>
    /// 统一的图标加载：由 <paramref name="load"/> 决定怎么取（本地图片解码 / 程序图标 / 系统图标 / 缩略图），
    /// 都在线程池上完成，再切回 UI 线程赋值。结果按 <paramref name="cacheKey"/> 缓存。
    /// </summary>
    private async Task LoadIconImageAsync(string cacheKey, string path, Func<string, Bitmap?> load)
    {
        try
        {
            if (IconCache.TryGetValue(cacheKey, out var cached))
            {
                IconImage = cached;
                return;
            }

            await IconLoadLimiter.WaitAsync().ConfigureAwait(false);

            Bitmap? bitmap;
            try
            {
                bitmap = await Task.Run(() => load(path)).ConfigureAwait(false);
            }
            finally
            {
                IconLoadLimiter.Release();
            }

            if (bitmap is not null)
            {
                // 并发加载同一目标时只保留先放进去的那张
                if (!IconCache.TryAdd(cacheKey, bitmap))
                {
                    bitmap.Dispose();
                    bitmap = IconCache[cacheKey];
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => IconImage = bitmap);
        }
        catch (Exception)
        {
            // 文件不存在 / 解码失败 / 没有权限：不显示图标，不影响条目其它内容
            await Dispatcher.UIThread.InvokeAsync(() => IconImage = null);
        }
    }

    /// <summary>优先取预览缩略图；该文件没有缩略图（或类型不适合）时回落到系统图标。</summary>
    private static Bitmap? LoadThumbnailOrIcon(string path) =>
        ShellIconExtractor.SupportsThumbnail(path)
            ? ShellIconExtractor.TryGetThumbnail(path) ?? ShellIconExtractor.TryGetIcon(path)
            : ShellIconExtractor.TryGetIcon(path);

    private static Bitmap? TryDecodeImageFile(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 按 Hint.Id 推断图标，仅在 <see cref="IHint.Icon"/> 为空时兜底。
    /// 认不出来的一律回退到 <see cref="DefaultIcon"/>（Terminal）。
    /// </summary>
    private static IHintIcon? ResolveIcon(IHint hint) => hint.Id?.ToLowerInvariant() switch
    {
        "open" or "url" or "link" or "browse" or "external" => new LucideHintIcon(LucideIconKind.ExternalLink),
        "copy" or "clipboard" => new LucideHintIcon(LucideIconKind.Copy),
        "search" or "find" => new LucideHintIcon(LucideIconKind.Search),
        "settings" or "config" or "preference" => new LucideHintIcon(LucideIconKind.Settings),
        "file" or "doc" or "document" => new LucideHintIcon(LucideIconKind.FileText),
        "folder" or "dir" or "directory" => new LucideHintIcon(LucideIconKind.Folder),
        "run" or "exec" or "executable:env" or "terminal" or "cmd" or "shell" => new LucideHintIcon(LucideIconKind.Terminal),
        "recent" or "last" => new LucideHintIcon(LucideIconKind.Clock),
        "help" or "whats" or "what" or "ask" or "question" => new LucideHintIcon(LucideIconKind.MessageCircleQuestionMark),
        _ => null,
    };

    private static LucideIconKind ParseLucideKind(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) &&
            Enum.TryParse<LucideIconKind>(id, ignoreCase: true, out var kind) &&
            Enum.IsDefined(kind))
        {
            return kind;
        }

        return DefaultLucideKind;
    }

    private static IHintIcon ErrorIcon(IHint hint)
    {
        if (hint.Id.ToLower().Contains("error"))
            return new LucideHintIcon(LucideIconKind.X);
        
        return new LucideHintIcon(DefaultLucideKind);
    }
}
