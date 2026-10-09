using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommandPark.Ui.ViewModels;

namespace CommandPark.Ui.Views;

public partial class ParkMainView : UserControl
{
    private ParkMainViewModel? _subscribedViewModel;

    public ParkMainView()
    {
        InitializeComponent();

        // 快速选择是「修饰键 + 1~9」：用隧道事件抢在 TextBox 之前处理，
        // 这样数字不会被当成搜索文本输入。
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        DataContext = new ParkMainViewModel();
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            control.Focus();

        if (DataContext is ParkMainViewModel viewModel)
        {
            ApplyQuickSelectConfig(viewModel);
            _ = viewModel.InitializeHomeAsync();
        }
    }

    /// <summary>读取 commandpark.json 的 quickSelect 配置；读取出错时回落到默认值。</summary>
    private static void ApplyQuickSelectConfig(ParkMainViewModel viewModel)
    {
        try
        {
            var config = ConfigFile.Load();
            viewModel.QuickSelectEnabled = QuickSelectConfig.ReadEnabled(config);
            viewModel.QuickSelectModifier = QuickSelectConfig.ReadModifier(config);
        }
        catch (Exception)
        {
            viewModel.QuickSelectEnabled = QuickSelectConfig.DefaultEnabled;
            viewModel.QuickSelectModifier = QuickSelectConfig.DefaultModifier;
        }
    }

    /// <summary>
    /// 快速选择：按住配置的修饰键（默认 Alt，可配 Ctrl）再按 1~9，直接执行对应结果并关闭窗口。
    /// 在隧道阶段处理，避免数字被输入框当成搜索文本。
    /// </summary>
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled ||
            DataContext is not ParkMainViewModel viewModel ||
            !viewModel.QuickSelectEnabled ||
            e.KeyModifiers != ToKeyModifiers(viewModel.QuickSelectModifier))
        {
            return;
        }

        var index = e.Key switch
        {
            >= Key.D1 and <= Key.D9           => (int)e.Key - (int)Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => (int)e.Key - (int)Key.NumPad1,
            _                                 => -1,
        };

        if (index < 0 || index >= viewModel.Results.Count)
        {
            return;
        }

        e.Handled = true;
        if (await viewModel.RunIndexAsync(index))
        {
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
    }

    private static KeyModifiers ToKeyModifiers(QuickSelectModifier modifier) => modifier switch
    {
        QuickSelectModifier.Ctrl => KeyModifiers.Control,
        _                        => KeyModifiers.Alt,
    };

    /// <summary>DataContext 可能被外部替换，这里保证只订阅当前 VM 的失败事件。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.RunFailed -= OnRunFailed;
            _subscribedViewModel.DetailsRequested -= OnDetailsRequested;
        }

        _subscribedViewModel = DataContext as ParkMainViewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.RunFailed += OnRunFailed;
            _subscribedViewModel.DetailsRequested += OnDetailsRequested;
        }
    }

    /// <summary>
    /// 结果区的键盘操作（事件从输入框冒泡上来）：
    /// ↑↓ 选中命令，Enter 执行并在完成后关闭窗口，Ctrl+Enter 执行但不关闭窗口。
    /// 没有结果时不处理，方向键留给输入框自己移动光标。
    /// </summary>
    private async void View_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ParkMainViewModel viewModel || viewModel.Results.Count == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                viewModel.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                e.Handled = true;
                var closeAfterRun = !e.KeyModifiers.HasFlag(KeyModifiers.Control);
                await RunSelectedAsync(viewModel, closeAfterRun);
                break;
        }

        if (e.Handled)
        {
            ScrollSelectionIntoView(viewModel.SelectedIndex);
        }
    }

    private async Task RunSelectedAsync(ParkMainViewModel viewModel, bool closeAfterRun)
    {
        // 执行失败时不关窗口（错误窗口会弹出提示），成功且需要关闭时才关
        if (await viewModel.RunSelectedAsync() && closeAfterRun)
        {
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
    }

    /// <summary>命令执行失败：弹出一个可用键盘全程操作的错误窗口（ESC / Enter 关闭）。</summary>
    private async void OnRunFailed(object? sender, HintRunFailedEventArgs args)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var errorWindow = new ErrorWindow
            {
                DataContext = new ErrorWindowViewModel(args),
            };

            await errorWindow.ShowDialog(owner);
        }
        catch (Exception)
        {
            // 弹窗自身失败也不能把应用带崩
        }
    }

    /// <summary>显示 Hint 完整详情；不关闭主窗口。</summary>
    private async void OnDetailsRequested(object? sender, HintDetailsRequestedEventArgs args)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var detailsWindow = new HintDetailsWindow
            {
                DataContext = new HintDetailsWindowViewModel(args.Hint, args.Details, args.ActionName),
            };

            await detailsWindow.ShowDialog(owner);
        }
        catch (Exception)
        {
            // 详情窗口失败不能影响主界面。
        }
    }

    private void ScrollSelectionIntoView(int index)
    {
        if (index >= 0 && this.FindControl<ListBox>("ResultsList") is { } listBox)
        {
            listBox.ScrollIntoView(index);
        }
    }
}