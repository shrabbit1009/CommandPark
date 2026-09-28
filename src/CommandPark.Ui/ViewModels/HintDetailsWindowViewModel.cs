namespace CommandPark.Ui.ViewModels;

/// <summary>详情窗口的数据。</summary>
public sealed class HintDetailsWindowViewModel(IHint hint, string details)
{
    public string Title { get; } = hint.Name;
    public string Description { get; } = hint.Description;
    public string Details { get; } = details;
}