using System.Reflection;
using System.Text.Json.Nodes;

namespace CommandPark.Ai;

/// <summary>AI 提供商预设。</summary>
public enum AiProvider
{
    /// <summary>通用 OpenAI 兼容端点：自行填写端点、密钥与模型。</summary>
    Custom,

    /// <summary>KiloCode 免费网关：无需密钥即可使用，默认模型 <c>kilo-auto/free</c>。</summary>
    KiloFree,

    /// <summary>DeepSeek 官方 API：默认模型 <c>deepseek-flash</c>，也可改用 <c>deepseek-pro</c>。</summary>
    DeepSeek,

    /// <summary>OpenCode Go 网关：需要密钥与模型，并自动附带 UA 与 <c>x-opencode-session</c> 请求头。</summary>
    OpenCodeGo,
}

/// <summary>
/// AI 处理模式与 AI 聊天模式使用的配置，位于 <c>commandpark.json</c> 顶层的 <c>ai</c> 节点。
/// <para>
/// 可以直接填写 <c>provider</c> 预设（会自动补齐端点、模型与必要的请求头），
/// 也可以用 <c>endpoint</c> / <c>model</c> / <c>apiKey</c> 覆盖预设值：
/// </para>
/// <code>
/// {
///   "ai": {
///     "provider": "kilo-free",
///     "maxResults": 5,
///     "temperature": 0.2
///   }
/// }
/// </code>
/// <para>支持的内置预设：</para>
/// <list type="bullet">
///   <item><description><c>kilo-free</c>：端点 <c>https://api.kilo.ai/api/gateway</c>，模型 <c>kilo-auto/free</c>，无需密钥。</description></item>
///   <item><description><c>deepseek</c>：端点 <c>https://api.deepseek.com/v1</c>，默认模型 <c>deepseek-flash</c>（可改 <c>deepseek-pro</c>），需要密钥。</description></item>
///   <item><description><c>opencode-go</c>：端点 <c>https://opencode.ai/zen/go/v1</c>，需要密钥与模型，自动附带 UA 与 <c>x-opencode-session</c>。</description></item>
/// </list>
/// </summary>
public sealed class AiConfig
{
    /// <summary>配置节点名。</summary>
    public const string SectionName = "ai";

    /// <summary>提供商预设配置项名。</summary>
    public const string ProviderPropertyName = "provider";

    /// <summary>API 端点配置项名。</summary>
    public const string EndpointPropertyName = "endpoint";

    /// <summary>API 密钥配置项名。</summary>
    public const string ApiKeyPropertyName = "apiKey";

    /// <summary>模型 ID 配置项名。</summary>
    public const string ModelPropertyName = "model";

    /// <summary>OpenCode Go 会话配置项名。</summary>
    public const string SessionPropertyName = "session";

    /// <summary>自定义请求头配置项名。</summary>
    public const string HeadersPropertyName = "headers";

    /// <summary>AI 最终呈现的结果条数配置项名。</summary>
    public const string MaxResultsPropertyName = "maxResults";

    /// <summary>采样温度配置项名。</summary>
    public const string TemperaturePropertyName = "temperature";

    /// <summary>KiloCode 免费网关预设名。</summary>
    public const string KiloFreeProviderName = "kilo-free";

    /// <summary>DeepSeek 预设名。</summary>
    public const string DeepSeekProviderName = "deepseek";

    /// <summary>OpenCode Go 预设名。</summary>
    public const string OpenCodeGoProviderName = "opencode-go";

    /// <summary>KiloCode 免费网关端点。</summary>
    public const string KiloFreeEndpoint = "https://api.kilo.ai/api/gateway";

    /// <summary>KiloCode 免费网关默认模型。</summary>
    public const string KiloFreeModel = "kilo-auto/free";

    /// <summary>DeepSeek 端点。</summary>
    public const string DeepSeekEndpoint = "https://api.deepseek.com/v1";

    /// <summary>DeepSeek 默认模型。</summary>
    public const string DeepSeekDefaultModel = "deepseek-flash";

    /// <summary>DeepSeek 可选模型。</summary>
    public const string DeepSeekProModel = "deepseek-pro";

    /// <summary>OpenCode Go 端点。</summary>
    public const string OpenCodeGoEndpoint = "https://opencode.ai/zen/go/v1";

    /// <summary>默认最终呈现的结果条数。</summary>
    public const int DefaultMaxResults = 5;

    /// <summary>最终呈现的结果条数上限。</summary>
    public const int MaxResultsLimit = 20;

    /// <summary>默认采样温度（偏低，让筛选更稳定）。</summary>
    public const float DefaultTemperature = 0.2f;

    /// <summary>解析出来的提供商预设。</summary>
    public AiProvider Provider { get; init; } = AiProvider.Custom;

    /// <summary>配置里填写的提供商名称原文（可能为空）。</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>实际使用的 API 端点（预设默认值或配置覆盖）。</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>API 密钥（KiloCode 免费网关可为空）。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>实际使用的模型 ID（预设默认值或配置覆盖）。</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>OpenCode Go 的 <c>x-opencode-session</c> 值；留空时自动生成。</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>
    /// 用户自定义的请求头。手动模式与 provider 模式都会附带；
    /// 与 provider 定义的请求头同名时，以 provider 为准。
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>AI 最终呈现给用户的结果条数（1~<see cref="MaxResultsLimit"/>）。</summary>
    public int MaxResults { get; init; } = DefaultMaxResults;

    /// <summary>采样温度（0~2）。</summary>
    public float Temperature { get; init; } = DefaultTemperature;

    /// <summary>该预设是否必须提供 API 密钥。</summary>
    public bool RequiresApiKey => Provider != AiProvider.KiloFree;

    /// <summary>
    /// 是否应当发送 API 密钥。KiloCode 免费网关留空、或填写占位值 <c>free</c> 时不发送认证头。
    /// </summary>
    public bool SendApiKey =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.Equals(ApiKey.Trim(), "free", StringComparison.OrdinalIgnoreCase);

    /// <summary>必填配置是否都已满足。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(Model) &&
        (!RequiresApiKey || !string.IsNullOrWhiteSpace(ApiKey));

    /// <summary>尚未填写的必填配置项名，便于提示用户。</summary>
    public IReadOnlyList<string> MissingFields
    {
        get
        {
            var missing = new List<string>(3);
            if (string.IsNullOrWhiteSpace(Endpoint)) missing.Add(EndpointPropertyName);
            if (RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey)) missing.Add(ApiKeyPropertyName);
            if (string.IsNullOrWhiteSpace(Model)) missing.Add(ModelPropertyName);
            return missing;
        }
    }

    /// <summary>
    /// 读取 <c>ai</c> 配置节。配置节不存在（或不是对象）时返回 <see langword="null"/>；
    /// 存在但字段缺失时返回一个 <see cref="IsConfigured"/> 为 <see langword="false"/> 的实例。
    /// </summary>
    public static AiConfig? Read(JsonNode? config)
    {
        if (config?[SectionName] is not JsonObject section)
        {
            return null;
        }

        var providerName = ReadProviderName(section);
        var provider = ParseProvider(providerName);

        var endpoint = ReadString(section, EndpointPropertyName);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultEndpoint(provider);
        }

        var model = ReadString(section, ModelPropertyName);
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel(provider);
        }

        return new AiConfig
        {
            Provider     = provider,
            ProviderName = providerName,
            Endpoint     = endpoint,
            ApiKey       = ReadString(section, ApiKeyPropertyName),
            Model        = model,
            Session      = ReadString(section, SessionPropertyName),
            Headers      = ReadHeaders(section),
            MaxResults   = ReadInt(section, MaxResultsPropertyName, DefaultMaxResults, 1, MaxResultsLimit),
            Temperature  = ReadFloat(section, TemperaturePropertyName, DefaultTemperature, 0f, 2f),
        };
    }

    /// <summary>
    /// 计算该提供商需要额外附带的请求头（目前只有 OpenCode Go）。
    /// <para>
    /// <c>User-Agent</c> 固定为 <c>command-park/{程序集版本}</c>；
    /// <c>x-opencode-session</c> 使用配置值，缺省时自动生成一个会话 id。
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildRequestHeaders()
    {
        // 先放入用户自定义请求头，再用 provider 的请求头覆盖：
        // 这样 provider 声明的请求头（如 OpenCode Go 的 UA / session）始终优先。
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Headers)
        {
            headers[name] = value;
        }

        if (Provider == AiProvider.OpenCodeGo)
        {
            headers["User-Agent"] = BuildUserAgent();
            headers["x-opencode-session"] = ResolveSession();
        }

        return headers;
    }

    /// <summary>解析 OpenCode Go 会话 id：优先使用配置值，否则生成一个。</summary>
    public string ResolveSession() =>
        string.IsNullOrWhiteSpace(Session) ? Guid.NewGuid().ToString("N") : Session.Trim();

    /// <summary>构造 <c>command-park/{程序集版本}</c> 形式的 User-Agent。</summary>
    public static string BuildUserAgent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        return $"command-park/{version}";
    }

    private static string DefaultEndpoint(AiProvider provider) => provider switch
    {
        AiProvider.KiloFree   => KiloFreeEndpoint,
        AiProvider.DeepSeek   => DeepSeekEndpoint,
        AiProvider.OpenCodeGo => OpenCodeGoEndpoint,
        _                     => string.Empty,
    };

    private static string DefaultModel(AiProvider provider) => provider switch
    {
        AiProvider.KiloFree => KiloFreeModel,
        AiProvider.DeepSeek => DeepSeekDefaultModel,
        _                   => string.Empty,
    };

    private static AiProvider ParseProvider(string name) => name.Trim().ToLowerInvariant() switch
    {
        "kilo-free" or "kilofree" or "kilo" or "kilocode" or "kilo-code" => AiProvider.KiloFree,
        "deepseek" or "deep-seek"                                        => AiProvider.DeepSeek,
        "opencode-go" or "opencodego" or "opencode"                      => AiProvider.OpenCodeGo,
        _                                                                => AiProvider.Custom,
    };

    /// <summary>
    /// 读取 <c>provider</c>，既支持 <c>"provider": "deepseek"</c>，也支持
    /// <c>"provider": { "name": "deepseek" }</c>。
    /// </summary>
    private static string ReadProviderName(JsonObject section)
    {
        var node = section[ProviderPropertyName];

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text?.Trim() ?? string.Empty;
        }

        if (node is JsonObject obj &&
            obj["name"] is JsonValue nameValue &&
            nameValue.TryGetValue<string>(out var name))
        {
            return name?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>读取自定义请求头对象；非字符串值会被忽略。</summary>
    private static IReadOnlyDictionary<string, string> ReadHeaders(JsonObject section)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (section[HeadersPropertyName] is not JsonObject obj)
        {
            return headers;
        }

        foreach (var (rawName, node) in obj)
        {
            var name = rawName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (node is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                headers[name] = text;
            }
        }

        return headers;
    }

    private static string ReadString(JsonObject section, string name) =>
        section[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text?.Trim() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonObject section, string name, int fallback, int min, int max)
    {
        if (section[name] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return Math.Clamp(integer, min, max);
        }

        if (value.TryGetValue<double>(out var number) && !double.IsNaN(number))
        {
            return Math.Clamp((int)Math.Round(number), min, max);
        }

        return fallback;
    }

    private static float ReadFloat(JsonObject section, string name, float fallback, float min, float max)
    {
        if (section[name] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<float>(out var single))
        {
            return Math.Clamp(single, min, max);
        }

        if (value.TryGetValue<double>(out var number) && !double.IsNaN(number))
        {
            return Math.Clamp((float)number, min, max);
        }

        return fallback;
    }
}
