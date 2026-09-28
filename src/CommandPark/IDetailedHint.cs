namespace CommandPark;

/// <summary>
/// 带有可在独立窗口中查看的完整详情的 Hint。
/// </summary>
public interface IDetailedHint : IHint
{
    string Details { get; }
}