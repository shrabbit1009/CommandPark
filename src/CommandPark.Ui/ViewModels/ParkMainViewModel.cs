using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandPark.Ui.ViewModels;

public partial class ParkMainViewModel : ViewModelBase
{
    [ObservableProperty] public partial string InputText { get; set; }
    
}