using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandPark.Strings;

/// <summary>
/// 从语言目录里的 JSON 文件构建 <see cref="MultiStringCollection"/>。
/// <para>
/// 语言文件默认放在程序目录下的 <c>lang</c> 目录里，文件名就是语言标记（例如 <c>zh_CN.json</c>、
/// <c>en_US.json</c>）。构建出的链按优先级从高到低排列：
/// <b>当前语言 → 配置的回退顺序 → 基准语言（默认中文 <c>zh_CN</c>）</b>。
/// </para>
/// <para>
/// 顺序来自 <see cref="LanguageConfig"/>，也就是 <c>commandpark.json</c> 的 <c>language</c> 节点，
/// 可以按需改成任意语言顺序。缺失或损坏的语言文件会被跳过，链本身仍然可用
/// （最差的情况只是把没翻译的键显示成路径）。
/// </para>
/// </summary>
public sealed class StringCollectionBuilder
{
    /// <summary>语言目录名。</summary>
    public const string LanguageDirectoryName = "lang";

    /// <summary>语言文件扩展名。</summary>
    public const string FileExtension = ".json";

    private readonly List<string> _fallbackOrder = [];

    private Dictionary<string, string>? _fileIndex;

    /// <param name="baseDirectory">程序目录；为空时使用 <see cref="AppContext.BaseDirectory"/>。</param>
    public StringCollectionBuilder(string? baseDirectory = null)
    {
        BaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        LanguageDirectory = Path.Combine(BaseDirectory, LanguageDirectoryName);
    }

    /// <summary>程序目录。</summary>
    public string BaseDirectory { get; }

    /// <summary>语言目录；默认是程序目录下的 <c>lang</c>。</summary>
    public string LanguageDirectory { get; set; }

    /// <summary>要使用的语言；null 表示跟随系统 UI 语言。</summary>
    public string? Language { get; set; }

    /// <summary>基准语言，始终排在查找顺序最后；默认中文。</summary>
    public string BaseLanguage { get; set; } = LanguageConfig.DefaultBaseLanguage;

    /// <summary>命中的提供者是否在找不到路径时抛异常（默认 false，回退成路径）。</summary>
    public bool ThrowIfNotFound { get; set; }

    /// <summary>命中的提供者是否在值不是字符串时抛异常（默认 false，回退成路径）。</summary>
    public bool ThrowIfNotString { get; set; }

    /// <summary>当前配置的回退顺序（不含基准语言）。</summary>
    public IReadOnlyList<string> FallbackOrder => _fallbackOrder;

    /// <summary>创建一个默认指向程序目录的构建器。</summary>
    /// <param name="baseDirectory">程序目录；为空时使用 <see cref="AppContext.BaseDirectory"/>。</param>
    public static StringCollectionBuilder Create(string? baseDirectory = null) => new(baseDirectory);

    /// <summary>使用指定语言；传 null 表示跟随系统 UI 语言。</summary>
    public StringCollectionBuilder WithLanguage(string? language)
    {
        Language = language;
        return this;
    }

    /// <summary>替换回退顺序（按给定顺序查找）。</summary>
    public StringCollectionBuilder WithFallback(params string?[] languages)
    {
        _fallbackOrder.Clear();

        return AddFallback(languages);
    }

    /// <summary>在回退顺序末尾追加。</summary>
    public StringCollectionBuilder AddFallback(params string?[] languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        foreach (var language in languages)
        {
            if (!string.IsNullOrWhiteSpace(language))
            {
                _fallbackOrder.Add(language.Trim());
            }
        }

        return this;
    }

    /// <summary>设置基准语言（查找顺序的最后一位）。</summary>
    public StringCollectionBuilder WithBaseLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            BaseLanguage = language.Trim();
        }

        return this;
    }

    /// <summary>设置语言目录。</summary>
    public StringCollectionBuilder WithDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LanguageDirectory = directory;
        }

        _fileIndex = null;
        return this;
    }

    /// <summary>按 <c>commandpark.json</c> 的 <c>language</c> 节点配置语言、回退顺序与基准语言。</summary>
    public StringCollectionBuilder ReadConfig(JsonNode? config)
    {
        var language = LanguageConfig.Read(config);

        Language = language.Current;
        BaseLanguage = language.BaseLanguage;

        return WithFallback([.. language.FallbackOrder]);
    }

    /// <summary>语言目录里可用的语言（按文件名排序）。</summary>
    public IReadOnlyList<string> AvailableLanguages =>
    [
        .. FileIndex()
            .Values
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

    /// <summary>
    /// 把语言标记解析成语言文件路径。会依次尝试：完全匹配 → 去掉书写系统/子标记的匹配 →
    /// 只给语言部分时取该语言排序最靠前的变体（例如 <c>en</c> → <c>en_US.json</c>）。
    /// </summary>
    public bool TryResolveLanguageFile(string? language, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var index = FileIndex();
        foreach (var candidate in LanguageCandidates(language.Trim()))
        {
            if (index.TryGetValue(NormalizeKey(candidate), out var exact))
            {
                path = exact;
                return true;
            }
        }

        // 只给语言部分（en）时，取该语言下排序最靠前的变体（en_US）。
        var prefix = NormalizeKey(LanguagePart(language)) + "_";
        var match = index.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (match is null)
        {
            return false;
        }

        path = index[match];
        return true;
    }

    /// <summary>
    /// 构建最终的字符串链：当前语言 → 回退顺序 → 基准语言（去重，跳过缺失的文件）。
    /// </summary>
    public MultiStringCollection Build()
    {
        var chain = new MultiStringCollection();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in BuildOrder())
        {
            if (!TryResolveLanguageFile(language, out var path) || !used.Add(path))
            {
                continue;
            }

            if (Load(path) is { } collection)
            {
                chain.Add(collection);
            }
        }

        return chain;
    }

    /// <summary>解析顺序：当前语言 → 回退顺序 → 基准语言。</summary>
    public IReadOnlyList<string> BuildOrder()
    {
        var order = new List<string>();

        Add(Language ?? CultureInfo.CurrentUICulture.Name);
        foreach (var language in _fallbackOrder)
        {
            Add(language);
        }

        Add(BaseLanguage);

        return order;

        void Add(string? language)
        {
            var trimmed = language?.Trim();
            if (string.IsNullOrEmpty(trimmed) || order.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            order.Add(trimmed);
        }
    }

    private StringCollection? Load(string path)
    {
        try
        {
            var collection = StringCollection.FromFile(path);
            collection.ThrowIfNotFound = ThrowIfNotFound;
            collection.ThrowIfNotString = ThrowIfNotString;

            return collection;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 语言文件损坏不应该拦住启动：跳过它，继续用回退语言。
            return null;
        }
    }

    /// <summary>语言目录索引：规范化后的语言标记 → 文件路径。</summary>
    private Dictionary<string, string> FileIndex()
    {
        if (_fileIndex is not null)
        {
            return _fileIndex;
        }

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(LanguageDirectory))
        {
            foreach (var path in Directory
                         .EnumerateFiles(LanguageDirectory, "*" + FileExtension)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                index.TryAdd(NormalizeKey(Path.GetFileNameWithoutExtension(path)), path);
            }
        }

        return _fileIndex = index;
    }

    /// <summary>语言标记的候选形式：<c>zh-Hans-CN</c> → <c>zh-CN</c> → <c>zh</c>。</summary>
    private static IEnumerable<string> LanguageCandidates(string language)
    {
        yield return language;

        var parts = language.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 2)
        {
            yield return $"{parts[0]}-{parts[^1]}";
        }

        if (parts.Length > 1)
        {
            yield return parts[0];
        }
    }

    private static string LanguagePart(string language) =>
        language.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries) is [var first, ..]
            ? first
            : language;

    private static string NormalizeKey(string language) => language.Replace('-', '_').ToLowerInvariant();
}