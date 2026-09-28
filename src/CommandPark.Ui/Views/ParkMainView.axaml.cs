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
        DataContext = new ParkMainViewModel();
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            control.Focus();

        if (DataContext is ParkMainViewModel viewModel)
            _ = viewModel.InitializeHomeAsync();
    }

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
                DataContext = new HintDetailsWindowViewModel(args.Hint, args.Details),
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