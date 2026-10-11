namespace CommandPark.Strings;

/// <summary>
/// 全程序统一的字符串入口：Hint 源、视图模型、XAML 标记扩展都从这里取文案。
/// <para>
/// 默认实例是懒加载的，按 <c>commandpark.json</c> 的 <c>language</c> 节点 + 程序目录下的
/// <c>lang</c> 目录构建（当前语言 → 回退顺序 → 中文基准）。启动时可以显式
/// <see cref="Configure"/> 覆盖，测试里也用它把语言固定下来。
/// </para>
/// <para>
/// 配置损坏、语言文件缺失都不会拦住启动：最差的情况是取到什么就显示什么（缺键时显示键路径）。
/// </para>
/// </summary>
public static class Localization
{
    /// <summary>语言目录名（相对程序目录）。</summary>
    public const string LanguageDirectoryName = StringCollectionBuilder.LanguageDirectoryName;

    private static readonly IStringCollection PathFallback = new MultiStringCollection();

    private static IStringCollection? _current;

    /// <summary>是否已经显式配置过（未配置时会按配置文件懒加载）。</summary>
    public static bool IsConfigured => _current is not null;

    /// <summary>当前使用的字符串提供者。</summary>
    public static IStringCollection Current => _current ??= CreateDefault();

    /// <summary>显式指定字符串提供者（程序启动、测试固定语言时使用）。</summary>
    public static void Configure(IStringCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        _current = collection;
    }

    /// <summary>恢复到「按配置文件懒加载」的状态。</summary>
    public static void Reset() => _current = null;

    /// <summary>取字符串；找不到时返回路径本身。</summary>
    public static string Get(string path) => Current.GetString(path);

    /// <summary>取字符串并格式化；找不到时回退成路径（不会因为缺键抛出）。</summary>
    public static string Format(string path, params object?[] args) => Current.GetFormat(path, args);

    /// <summary>只在路径命中一个真正的字符串时返回 <see langword="true"/>。</summary>
    public static bool TryGet(string path, out string? value) => Current.TryGetString(path, out value);

    /// <summary>按 <c>commandpark.json</c> 与 <c>lang</c> 目录构建默认提供者。</summary>
    public static IStringCollection CreateDefault()
    {
        try
        {
            return StringCollectionBuilder.Create().ReadConfig(ConfigFile.Load()).Build();
        }
        catch (Exception)
        {
            // 配置文件坏了不能让界面变成一堆异常：退化成「路径即文案」。
            return PathFallback;
        }
    }
}