using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandPark.Ui.ViewModels;

/// <summary>详情窗口的数据和可选操作。</summary>
public partial class HintDetailsWindowViewModel : ObservableObject
{
    public HintDetailsWindowViewModel(IHint hint, string details, string? actionName = null)
    {
        Hint = hint;
        Title = hint.Name;
        Description = hint.Description;
        Details = details;
        ActionName = actionName;
        RunActionCommand = HasAction ? new AsyncRelayCommand(RunActionAsync) : null;
    }

    public IHint Hint { get; }
    public string Title { get; }
    public string Description { get; }
    public string Details { get; }
    public string? ActionName { get; }
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionName);
    public IAsyncRelayCommand? RunActionCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    public partial string? ActionError { get; set; }

    public bool HasActionError => !string.IsNullOrWhiteSpace(ActionError);

    private async Task RunActionAsync()
    {
        try
        {
            ActionError = null;
            await Hint.Run();
        }
        catch (Exception exception)
        {
            ActionError = exception.Message;
        }
    }
}