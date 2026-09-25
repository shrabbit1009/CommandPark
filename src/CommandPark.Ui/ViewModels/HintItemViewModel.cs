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
    /// <see cref="LucideHintIcon"/> → LucideIcon，<see cref="LocalImageHintIcon"/> 与
    /// <see cref="ExecutableHintIcon"/> → Image（都在线程池上解码/提取）。
    /// 设为 null 时整列图标收起。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LucideKind))]
    [NotifyPropertyChangedFor(nameof(ShowLucideIcon))]
    [NotifyPropertyChangedFor(nameof(ShowImageIcon))]
    public partial IHintIcon? Icon { get; set; }

    /// <summary>
    /// Lucide 图标种类。图片类图标（本地图片/程序图标）在还没加载出来时，
    /// 先用默认图标兜底，避免图标位置空一块。
    /// </summary>
    public LucideIconKind? LucideKind => Icon switch
    {
        LucideHintIcon lucide => lucide.IconKind,
        null                  => null,
        _                     => IconImage is null ? DefaultLucideKind : null,
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

    /// <summary>执行命令失败时触发。异常在这里被拦住，不会冒到 UI 线程导致崩溃。</summary>
    public event EventHandler<HintRunFailedEventArgs>? RunFailed;

    [RelayCommand]
    public Task Run() => TryRunAsync();

    /// <summary>
    /// 执行 Hint。所有异常都在这里拦截并转成 <see cref="RunFailed"/> 事件，返回是否执行成功
    /// （调用方据此决定要不要关闭窗口）。主动取消不算错误，不弹提示。
    /// </summary>
    public async Task<bool> TryRunAsync()
    {
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

    [RelayCommand]
    public void Cancel() => _cancellationTokenSource.Cancel();

    partial void OnIconChanged(IHintIcon? value)
    {
        IconImage = null;

        switch (value)
        {
            case LocalImageHintIcon localImage:
                _ = LoadIconImageAsync(localImage.Path, fromExecutable: false);
                break;

            case ExecutableHintIcon executable:
                _ = LoadIconImageAsync(executable.Path, fromExecutable: true);
                break;
        }
    }

    /// <summary>
    /// 统一的图标加载：本地图片直接解码，程序图标走 Win32 提取"最大的那张"。
    /// 解码/提取在线程池上做，完成后切回 UI 线程赋值。
    /// </summary>
    private async Task LoadIconImageAsync(string path, bool fromExecutable)
    {
        try
        {
            if (IconCache.TryGetValue(path, out var cached))
            {
                IconImage = cached;
                return;
            }

            await IconLoadLimiter.WaitAsync().ConfigureAwait(false);

            Bitmap? bitmap;
            try
            {
                bitmap = await Task.Run(() => fromExecutable
                    ? ProgramIconExtractor.TryExtract(path)
                    : TryDecodeImageFile(path)).ConfigureAwait(false);
            }
            finally
            {
                IconLoadLimiter.Release();
            }

            if (bitmap is not null)
            {
                // 并发加载同一路径时只保留先放进去的那张
                if (!IconCache.TryAdd(path, bitmap))
                {
                    bitmap.Dispose();
                    bitmap = IconCache[path];
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

    private static IHintIcon ErrorIcon(IHint hint)
    {
        if (hint.Id.ToLower().Contains("error"))
            return new LucideHintIcon(LucideIconKind.X);
        
        return new LucideHintIcon(DefaultLucideKind);
    }
}