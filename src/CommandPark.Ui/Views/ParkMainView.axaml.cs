using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommandPark.Ui.ViewModels;

namespace CommandPark.Ui.Views;

public partial class ParkMainView : UserControl
{
    public ParkMainView()
    {
        InitializeComponent();
        DataContext = new ParkMainViewModel();
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
            control.Focus();
    }
}