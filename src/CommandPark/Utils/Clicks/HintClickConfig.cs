using System.Text.Json.Nodes;

namespace CommandPark.Utils.Clicks;

/// <summary>
/// 读取 htt 点击权重的配置。配置位于 <c>commandpark.json</c> 顶层的 <c>clicks</c> 节点：
/// <code>
/// {
///   "clicks": { "maxEntries": 10000 }
/// }
/// </code>
/// </summary>
public static class HintClickConfig
{
    /// <summary>配置节点名。</summary>
    public const string SectionName = "clicks";

    /// <summary>配置项名。</summary>
    public const string MaxEntriesPropertyName = "maxEntries";

    /// <summary>默认保留条目数。</summary>
    public const int DefaultMaxEntries = HintClickStore.DefaultMaxEntries;

    /// <summary><c>maxEntries</c> 允许的下限。</summary>
    public const int MinMaxEntries = 1;

    /// <summary><c>maxEntries</c> 允许的上限（防止误配成超大值撑爆内存/文件）。</summary>
    public const int MaxMaxEntries = 1_000_000;

    /// <summary>
    /// 读取 <c>clicks.maxEntries</c>。缺失、类型错误或非法时回落到
    /// <see cref="DefaultMaxEntries"/>，合法值会被夹在 [<see cref="MinMaxEntries"/>, <see cref="MaxMaxEntries"/>]。
    /// </summary>
    public static int ReadMaxEntries(JsonNode? config)
    {
        if (config?[SectionName] is not JsonObject section)
        {
            return DefaultMaxEntries;
        }

        if (section[MaxEntriesPropertyName] is not JsonValue value ||
            !value.TryGetValue<int>(out var maxEntries))
        {
            return DefaultMaxEntries;
        }

        return Math.Clamp(maxEntries, MinMaxEntries, MaxMaxEntries);
    }
}
