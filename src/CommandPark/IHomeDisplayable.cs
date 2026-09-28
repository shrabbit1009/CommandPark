namespace CommandPark;

/// <summary>
/// 标记只在首屏（空查询）运行的 <see cref="IHintSource"/>。
/// 首屏源不会在用户开始输入后运行。
/// </summary>
public interface IHomeDisplayable;