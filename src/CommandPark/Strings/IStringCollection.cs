namespace CommandPark.Strings;

/// <summary>
/// 字符串提供者：按 <c>a.b.c</c> 形式的路径取字符串。
/// <para>
/// 路径用 <c>.</c> 分隔，逐级进入 JSON 对象；进入数组时可以用数字下标（例如 <c>items.0</c>）。
/// 键名本身包含 <c>.</c> 时无法直接寻址。
/// </para>
/// </summary>
public interface IStringCollection
{
    /// <summary>
    /// 取字符串。找不到（或不是字符串）时的行为由实现决定：通常回退成路径本身，
    /// 便于一眼看出缺失的键。
    /// </summary>
    string GetString(string path);

    /// <summary>
    /// 取字符串并按 <see cref="string.Format(string, object?[])"/> 格式化。
    /// </summary>
    /// <param name="path">字符串路径。</param>
    /// <param name="args">格式化参数。</param>
    string GetFormat(string path, params object?[] args);

    /// <summary>只在路径命中一个真正的字符串时返回 <see langword="true"/>。</summary>
    bool TryGetString(string path, out string? value);

    /// <summary>路径是否命中一个真正的字符串。</summary>
    bool Contains(string path);
}