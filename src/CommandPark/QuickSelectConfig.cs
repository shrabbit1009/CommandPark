using System.Text.Json.Nodes;

namespace CommandPark;

/// <summary>快速选择使用的修饰键。</summary>
public enum QuickSelectModifier
{
    /// <summary>Alt。</summary>
    Alt,

    /// <summary>Ctrl。</summary>
    Ctrl,
}

/// <summary>
/// 快速选择视图的配置，位于 <c>commandpark.json</c> 顶层的 <c>quickSelect</c> 节点：
/// <code>
/// { "quickSelect": { "enabled": true, "modifier": "Alt" } }
/// </code>
/// <para>
/// 开启后结果列表右侧显示点击权重与前 9 条的快速选择编号；
/// 按住修饰键（默认 Alt，可改 Ctrl）再按 1~9 直接打开对应条目。默认开启。
/// </para>
/// </summary>
public static class QuickSelectConfig
{
    /// <summary>配置节点名。</summary>
    public const string SectionName = "quickSelect";

    /// <summary>是否开启的配置项名。</summary>
    public const string EnabledPropertyName = "enabled";

    /// <summary>修饰键的配置项名。</summary>
    public const string ModifierPropertyName = "modifier";

    /// <summary>默认是否开启。</summary>
    public const bool DefaultEnabled = true;

    /// <summary>默认修饰键。</summary>
    public const QuickSelectModifier DefaultModifier = QuickSelectModifier.Alt;

    /// <summary>最多提供快速选择的条目数（1~9）。</summary>
    public const int MaxItems = 9;

    /// <summary>
    /// 读取 <c>quickSelect.enabled</c>。缺失、类型错误时回落到 <see cref="DefaultEnabled"/>。
    /// </summary>
    public static bool ReadEnabled(JsonNode? config)
    {
        if (GetSection(config)?[EnabledPropertyName] is not JsonValue value ||
            !value.TryGetValue<bool>(out var enabled))
        {
            return DefaultEnabled;
        }

        return enabled;
    }

    /// <summary>
    /// 读取 <c>quickSelect.modifier</c>，接受 <c>Alt</c> / <c>Ctrl</c>（大小写不敏感）。
    /// 缺失或无法识别时回落到 <see cref="DefaultModifier"/>。
    /// </summary>
    public static QuickSelectModifier ReadModifier(JsonNode? config)
    {
        if (GetSection(config)?[ModifierPropertyName] is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return DefaultModifier;
        }

        return text?.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => QuickSelectModifier.Ctrl,
            "alt"               => QuickSelectModifier.Alt,
            _                   => DefaultModifier,
        };
    }

    /// <summary>
    /// 返回第 <paramref name="index"/> 条（0 起）对应的快速选择编号；超出前 <see cref="MaxItems"/> 条时为 null。
    /// </summary>
    public static int? GetKey(int index) => index >= 0 && index < MaxItems ? index + 1 : null;

    private static JsonObject? GetSection(JsonNode? config) =>
        config?[SectionName] as JsonObject;
}
