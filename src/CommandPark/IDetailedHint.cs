namespace CommandPark;

/// <summary>
/// 带有可在独立窗口中查看的完整详情的 Hint。
/// </summary>
public interface IDetailedHint : IHint
{
    string Details { get; }

    /// <summary>详情窗口中的可选操作名称；为空时不显示操作按钮。</summary>
    string? ActionName { get; }
}