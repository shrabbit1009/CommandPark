using System.Text.Json.Nodes;

namespace CommandPark;

/// <summary>主题模式。</summary>
public enum ThemeMode
{
    /// <summary>跟随系统；对应 JSON 中的 auto、null 或未填写。</summary>
    Default,
    Light,
    Dark,
}

/// <summary>读取顶层 theme 配置。</summary>
public static class ThemeConfigReader
{
    public const string PropertyName = "theme";

    /// <summary>
    /// 配置缺失、为 null、auto 或无法识别时返回 <see cref="ThemeMode.Default"/>。
    /// </summary>
    public static ThemeMode Read(JsonNode? json)
    {
        if (json?[PropertyName] is not JsonValue value ||
            !value.TryGetValue<string>(out var configuredTheme) ||
            string.IsNullOrWhiteSpace(configuredTheme))
        {
            return ThemeMode.Default;
        }

        return configuredTheme.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "dark"  => ThemeMode.Dark,
            _       => ThemeMode.Default,
        };
    }
}