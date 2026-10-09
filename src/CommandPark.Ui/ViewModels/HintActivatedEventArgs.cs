using System;

namespace CommandPark.Ui.ViewModels;

/// <summary>用户激活一条 Hint（执行或查看详情）时携带的信息。</summary>
public sealed class HintActivatedEventArgs(IHint hint) : EventArgs
{
    /// <summary>被激活的 Hint。</summary>
    public IHint Hint { get; } = hint;
}
