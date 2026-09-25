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

        var vm = new MainViewModel
        {
            InnerView = new ParkMainView()
        };
        DataContext  = vm;
    }

    private void InputElement_OnKeyUp(object? sender, KeyEventArgs e)
    {
        // 处理 ESC 关闭
        if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
        {
            Close();
        }
    }
}