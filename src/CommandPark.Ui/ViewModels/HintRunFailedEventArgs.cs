using System;

namespace CommandPark.Ui.ViewModels;

/// <summary>执行 Hint 失败时携带的信息。</summary>
public sealed class HintRunFailedEventArgs(IHint hint, Exception exception) : EventArgs
{
    /// <summary>执行失败的 Hint。</summary>
    public IHint Hint { get; } = hint;

    /// <summary>原始异常（含堆栈）。</summary>
    public Exception Exception { get; } = exception;
}