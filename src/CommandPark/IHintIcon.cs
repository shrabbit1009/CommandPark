namespace CommandPark;

/// <summary>Hint 条目的图标。</summary>
public interface IHintIcon;

/// <summary>本地图片文件（png / jpg / ico …）作为图标。</summary>
public class LocalImageHintIcon(string path) : IHintIcon
{
    public string Path { get; } = path;

    public override string ToString() => Path;
}

/// <summary>可执行文件自带的图标（取最大的那张，由界面层负责提取）。</summary>
public class ExecutableHintIcon(string path) : IHintIcon
{
    public string Path { get; } = path;

    public override string ToString() => Path;
}