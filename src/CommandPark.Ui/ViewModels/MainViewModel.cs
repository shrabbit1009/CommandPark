using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandPark.Ui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Greeting { get; set; } = "Welcome to Avalonia!";
    [ObservableProperty] public partial Control? InnerView { get; set; } = null;
}