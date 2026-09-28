using System;
namespace CommandPark.Ui.ViewModels;

/// <summary>请求显示 Hint 完整详情的参数。</summary>
public sealed class HintDetailsRequestedEventArgs(IHint hint, string details) : EventArgs
{
    public IHint Hint { get; } = hint;
    public string Details { get; } = details;
}