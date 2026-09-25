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

/// <summary>文件 / 文件夹 / 驱动器的系统图标（按文件类型取，由界面层负责提取）。</summary>
public class FileIconHintIcon(string path) : IHintIcon
{
    public string Path { get; } = path;

    public override string ToString() => Path;
}

/// <summary>
/// 文件的预览缩略图（图片/视频/PDF 等由系统缩略图提供程序生成）；
/// 没有缩略图时由界面层回落到 <see cref="FileIconHintIcon"/> 的图标。
/// </summary>
public class ThumbnailHintIcon(string path) : IHintIcon
{
    public string Path { get; } = path;

    public override string ToString() => Path;
}