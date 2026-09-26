namespace CommandPark;

public interface IHintWithPrefix : IHintSource
{
    /// <summary>
    /// 前缀匹配模式。false 下为不输入前缀即可匹配，输入前缀会提升显示权重；true 则只在前缀输入后触发。
    /// </summary>
    bool PrefixMode { get; set; }
}