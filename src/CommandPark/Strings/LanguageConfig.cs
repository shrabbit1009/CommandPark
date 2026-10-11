using System.Globalization;
using System.Text.Json.Nodes;

namespace CommandPark.Strings;

/// <summary>
/// <c>commandpark.json</c> 顶层的 <c>language</c> 配置：
/// <code>
/// {
///   "language": {
///     "current": "auto",
///     "fallback": ["zh_CN"],
///     "base": "zh_CN"
///   }
/// }
/// </code>
/// <para>
/// <list type="bullet">
/// <item><c>current</c>：界面语言；写 <c>auto</c>、null 或干脆不填时跟随系统 UI 语言。</item>
/// <item><c>fallback</c>：当前语言里找不到键时的回退顺序，可以写一个字符串或字符串数组；
/// 默认 <c>["zh_CN"]</c>，也就是默认以中文为基准。</item>
/// <item><c>base</c>：基准语言，始终排在查找顺序的最后一位；默认 <c>zh_CN</c>。</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Current">配置的界面语言；null 表示跟随系统。</param>
/// <param name="FallbackOrder">回退顺序（不含基准语言）。</param>
/// <param name="BaseLanguage">基准语言。</param>
public sealed record LanguageConfig(string? Current, IReadOnlyList<string> FallbackOrder, string BaseLanguage)
{
    /// <summary>配置节点名。</summary>
    public const string SectionName = "language";

    /// <summary><c>current</c> 配置项名。</summary>
    public const string CurrentPropertyName = "current";

    /// <summary><c>fallback</c> 配置项名。</summary>
    public const string FallbackPropertyName = "fallback";

    /// <summary><c>base</c> 配置项名。</summary>
    public const string BasePropertyName = "base";

    /// <summary><c>current</c> 里表示「跟随系统」的取值。</summary>
    public const string AutoValue = "auto";

    /// <summary>默认基准语言：中文（简体）。</summary>
    public const string DefaultBaseLanguage = "zh_CN";

    /// <summary>默认回退顺序：中文基准。</summary>
    public static readonly IReadOnlyList<string> DefaultFallbackOrder = [DefaultBaseLanguage];

    /// <summary>默认配置：跟随系统语言，中文兜底。</summary>
    public static LanguageConfig Default { get; } = new(null, DefaultFallbackOrder, DefaultBaseLanguage);

    /// <summary>
    /// 读取 <c>language</c> 节点。节点缺失或类型不对时返回 <see cref="Default"/>。
    /// </summary>
    public static LanguageConfig Read(JsonNode? config)
    {
        if (config?[SectionName] is not JsonObject section)
        {
            return Default;
        }

        return new LanguageConfig(
            ReadCurrent(section[CurrentPropertyName]),
            ReadFallbackOrder(section[FallbackPropertyName]) ?? DefaultFallbackOrder,
            ReadText(section[BasePropertyName]) ?? DefaultBaseLanguage);
    }

    /// <summary>实际要使用的语言：配置为「跟随系统」时取系统 UI 语言。</summary>
    public string ResolveCurrentLanguage()
    {
        var configured = Current?.Trim();

        return string.IsNullOrEmpty(configured)
            ? CultureInfo.CurrentUICulture.Name
            : configured;
    }

    /// <summary>
    /// 完整的查找顺序：当前语言 → 配置的回退顺序 → 基准语言；重复项只保留最先出现的那个。
    /// </summary>
    public IReadOnlyList<string> ResolveOrder()
    {
        var order = new List<string>();

        Add(ResolveCurrentLanguage());
        foreach (var language in FallbackOrder)
        {
            Add(language);
        }

        Add(BaseLanguage);

        return order;

        void Add(string? language)
        {
            var trimmed = language?.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                order.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            order.Add(trimmed);
        }
    }

    private static string? ReadCurrent(JsonNode? node)
    {
        var text = ReadText(node);
        if (text is null)
        {
            return null;
        }

        return string.Equals(text, AutoValue, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(text, "system", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }

    private static IReadOnlyList<string>? ReadFallbackOrder(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var single):
                return Split(single);

            case JsonArray array:
            {
                var languages = new List<string>();
                foreach (var item in array)
                {
                    if (item is JsonValue entry && entry.TryGetValue<string>(out var text))
                    {
                        languages.AddRange(Split(text));
                    }
                }

                return languages;
            }

            default:
                return null;
        }

        static IReadOnlyList<string> Split(string? text)
        {
            var trimmed = text?.Trim();

            return string.IsNullOrEmpty(trimmed) ? [] : [trimmed];
        }
    }

    private static string? ReadText(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}