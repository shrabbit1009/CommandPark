using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandPark.Strings;

/// <summary>
/// 从单个 JSON 文档读取字符串，路径形如 <c>ui.ai.send</c>。
/// <para>
/// 只有路径真的命中一个字符串时才返回它；节点缺失、或节点不是字符串时，
/// 按 <see cref="ThrowIfNotFound"/> / <see cref="ThrowIfNotString"/> 决定是抛异常还是回退成路径。
/// </para>
/// <para>
/// 解析结果按路径缓存，重复取同一个键不会重复遍历 JSON；缓存使用
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>，多个线程同时取字符串是安全的。
/// </para>
/// </summary>
public class StringCollection : IStringCollection
{
    /// <summary>路径分隔符。</summary>
    public const char PathSeparator = '.';

    /// <summary>读取语言文件时使用的 JSON 解析选项（允许注释与尾随逗号）。</summary>
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly JsonNode? _root;
    private readonly ConcurrentDictionary<string, Resolution> _cache = new(StringComparer.Ordinal);

    /// <param name="root">语言文件的 JSON 根节点；为 null 时表示没有任何字符串。</param>
    public StringCollection(JsonNode? root) : this(root, name: null)
    {
    }

    /// <param name="root">语言文件的 JSON 根节点；为 null 时表示没有任何字符串。</param>
    /// <param name="name">提供者名称（一般是语言标记或文件名），只用于诊断。</param>
    public StringCollection(JsonNode? root, string? name)
    {
        _root = root;
        Name = name ?? string.Empty;
    }

    /// <summary>提供者名称（一般是语言标记或文件名），只用于诊断。</summary>
    public string Name { get; }

    /// <summary>
    /// 找不到时的行为，true 代表找不到时抛出异常；反之则返回索引路径。
    /// </summary>
    public bool ThrowIfNotFound { get; set; }

    /// <summary>
    /// JSON 值不是字符串时的行为，true 代表不是字符串时抛出异常；反之则返回索引路径。
    /// </summary>
    public bool ThrowIfNotString { get; set; }

    /// <summary>从 JSON 文本创建。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="name">提供者名称（一般是语言标记或文件名）。</param>
    public static StringCollection Parse(string json, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new StringCollection(
            JsonNode.Parse(json, nodeOptions: null, documentOptions: DocumentOptions),
            name);
    }

    /// <summary>从语言文件创建，提供者名称取文件名（不含扩展名）。</summary>
    /// <param name="path">语言文件路径。</param>
    public static StringCollection FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new StringCollection(
            JsonNode.Parse(File.ReadAllText(path), nodeOptions: null, documentOptions: DocumentOptions),
            Path.GetFileNameWithoutExtension(path));
    }

    /// <inheritdoc />
    public bool TryGetString(string path, out string? value)
    {
        var resolution = Resolve(path);

        value = resolution.IsString ? resolution.Value : null;
        return resolution.IsString;
    }

    /// <inheritdoc />
    public string GetString(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var resolution = Resolve(path);
        if (resolution.IsString)
        {
            return resolution.Value!;
        }

        if (resolution.Kind == JsonValueKind.Undefined)
        {
            return ThrowIfNotFound
                ? throw new StringPathNotFoundException($"找不到当前路径中的字符串'{path}'。")
                : path;
        }

        return ThrowIfNotString
            ? throw new StringPathNotStringException($"'{path}'不是一个字符串，而是{resolution.Kind}。")
            : path;
    }

    /// <inheritdoc />
    public bool Contains(string path) => TryGetString(path, out _);

    /// <inheritdoc />
    public string GetFormat(string path, params object?[] args) =>
        string.Format(GetString(path), args);

    /// <summary>解析一条路径，结果（含未命中）会被缓存。</summary>
    private Resolution Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _cache.GetOrAdd(path, static (key, root) => Walk(root, key), _root);
    }

    private static Resolution Walk(JsonNode? root, string path)
    {
        var node = root;

        foreach (var segment in path.Split(PathSeparator))
        {
            node = Step(node, segment);
            if (node is null)
            {
                return Resolution.Missing;
            }
        }

        return Inspect(node);
    }

    /// <summary>沿一级路径前进：对象按名字取，数组按数字下标取。</summary>
    private static JsonNode? Step(JsonNode? node, string segment) => node switch
    {
        JsonObject obj => obj[segment],

        JsonArray array when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) =>
            index >= 0 && index < array.Count ? array[index] : null,

        _ => null,
    };

    private static Resolution Inspect(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return node is null
                ? Resolution.Missing
                : new Resolution(node.GetValueKind(), null);
        }

        var kind = value.GetValueKind();

        return kind == JsonValueKind.String
            ? new Resolution(kind, value.GetValue<string>())
            : new Resolution(kind, null);
    }

    /// <summary>一条路径的解析结果：<see cref="JsonValueKind.Undefined"/> 表示没找到。</summary>
    private readonly record struct Resolution(JsonValueKind Kind, string? Value)
    {
        public static Resolution Missing => new(JsonValueKind.Undefined, null);

        public bool IsString => Kind == JsonValueKind.String;
    }
}

/// <summary>路径在字符串提供者里不存在。</summary>
public class StringPathNotFoundException : KeyNotFoundException
{
    public StringPathNotFoundException()
    {
    }

    public StringPathNotFoundException(string message) : base(message)
    {
    }

    public StringPathNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>路径存在，但对应的 JSON 值不是字符串。</summary>
public class StringPathNotStringException : Exception
{
    public StringPathNotStringException()
    {
    }

    public StringPathNotStringException(string message) : base(message)
    {
    }

    public StringPathNotStringException(string message, Exception inner) : base(message, inner)
    {
    }
}