using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommandPark.Ui.ViewModels;

namespace CommandPark.Ui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var parkViewModel = new ParkMainViewModel();
        var vm = new MainViewModel
        {
            InnerView = new ParkMainView(parkViewModel)
        };
        DataContext = vm;

        // AI 处理模式下窗口显示彩虹描边：ParkMainViewModel.IsAiMode 是唯一状态源，
        // MainViewModel.ShowRainbow 只承担窗口级表现（MainWindow.axaml 用它触发描边的淡入淡出）。
        SyncRainbow(parkViewModel, vm);
        parkViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ParkMainViewModel.IsAiMode))
            {
                SyncRainbow(parkViewModel, vm);
            }
        };
    }

    /// <summary>把 AI 处理模式的开关同步到窗口的彩虹描边。</summary>
    private static void SyncRainbow(ParkMainViewModel parkViewModel, MainViewModel windowViewModel) =>
        windowViewModel.ShowRainbow = parkViewModel.IsAiMode;

    private void InputElement_OnKeyUp(object? sender, KeyEventArgs e)
    {
        // 处理 ESC 关闭
        if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
        {
            Close();
        }
    }
}