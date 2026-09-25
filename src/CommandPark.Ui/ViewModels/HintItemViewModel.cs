using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPark.Ui.ViewModels;

public partial class HintItemViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cancellationTokenSource = new ();
    
    public IHint Hint { get; init; }
    
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial Bitmap? Icon { get; set; }
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;

    [RelayCommand]
    public Task Run() => Hint.Run(_cancellationTokenSource.Token);
    
    [RelayCommand]
    public void Cancel() => _cancellationTokenSource.Cancel();
}