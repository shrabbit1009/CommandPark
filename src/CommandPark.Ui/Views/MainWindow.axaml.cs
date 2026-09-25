using Avalonia.Controls;
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
}