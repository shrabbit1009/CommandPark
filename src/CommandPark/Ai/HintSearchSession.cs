using System.Text;
using CommandPark.Strings;
using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

/// <summary>
/// 一次 AI 筛选 Hint 的搜索会话。
/// <para>
/// 向模型暴露「搜索 / 读取」工具，并把模型见过的所有 Hint 按短哈希记录下来，
/// 最终把模型输出的短哈希数组解析回真实 <see cref="IHint"/>。
/// </para>
/// </summary>
public sealed class HintSearchSession
{
    /// <summary>调用 <c>search_hints</c> 时单次最多返回的条数。</summary>
    public const int MaxSearchResults = 100;

    /// <summary>向模型展示单个字段时使用的最大字符数，避免上下文被长描述撑爆。</summary>
    private const int FieldDisplayLimit = 200;

    private readonly IHintProvider _provider;
    private readonly int _hashLength;
    private readonly object _gate = new();
    private readonly Dictionary<string, IHint> _known = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<AITool>? _tools;

    /// <param name="provider">用于搜索 Hint 的提供者。</param>
    /// <param name="hashLength">短哈希长度，默认 <see cref="HintHash.DefaultLength"/>。</param>
    public HintSearchSession(IHintProvider provider, int hashLength = HintHash.DefaultLength)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _hashLength = Math.Clamp(hashLength, HintHash.MinLength, HintHash.MaxLength);
    }

    /// <summary>当前会话累计记录的 Hint 条数。</summary>
    public int KnownCount
    {
        get
        {
            lock (_gate)
            {
                return _known.Count;
            }
        }
    }

    /// <summary>提供给模型的工具：搜索 Hint、读取 Hint 详情。</summary>
    public IReadOnlyList<AITool> Tools => _tools ??=
    [
        AIFunctionFactory.Create(
            (Func<string, int, CancellationToken, Task<string>>)SearchHintsAsync,
            "search_hints",
            Localization.Get("ai.tools.searchHints")),

        AIFunctionFactory.Create(
            (Func<string, string>)GetHint,
            "get_hint",
            Localization.Get("ai.tools.getHint")),
    ];

    /// <summary>搜索 Hint 并把结果格式化成模型可读的文本，同时记录短哈希用于最终解析。</summary>
    /// <param name="query">搜索关键字。</param>
    /// <param name="maxResults">本次最多返回的条数（1~<see cref="MaxSearchResults"/>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string> SearchHintsAsync(
        string query,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        query = query ?? string.Empty;
        maxResults = Math.Clamp(maxResults, 1, MaxSearchResults);

        var collected = await SearchAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
        if (collected.Count == 0)
        {
            return Localization.Format("ai.session.noMatch", query);
        }

        var sb = new StringBuilder();
        sb.AppendLine(Localization.Format("ai.session.candidates", collected.Count));
        foreach (var hint in collected)
        {
            sb.Append(HintHash.Short(hint, _hashLength))
              .Append(" | ")
              .Append(Truncate(hint.Name, FieldDisplayLimit))
              .Append(" | ")
              .AppendLine(Truncate(hint.Description, FieldDisplayLimit));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>按短哈希读取一个 Hint 的完整信息。</summary>
    /// <param name="hash">search_hints 返回过的短哈希。</param>
    public string GetHint(string hash)
    {
        if (!TryResolve(hash, out var hint))
        {
            return Localization.Format("ai.session.unknownHash", hash);
        }

        var sb = new StringBuilder();
        sb.AppendLine(Localization.Format("ai.session.hash", HintHash.Short(hint, _hashLength)));
        sb.AppendLine(Localization.Format("ai.session.id", Truncate(hint.Id, FieldDisplayLimit)));
        sb.AppendLine(Localization.Format("ai.session.name", Truncate(hint.Name, FieldDisplayLimit)));
        sb.AppendLine(Localization.Format("ai.session.description", Truncate(hint.Description, FieldDisplayLimit)));

        if (hint is IDetailedHint detailed && !string.IsNullOrWhiteSpace(detailed.Details))
        {
            sb.AppendLine(Localization.Format("ai.session.details", Truncate(detailed.Details, FieldDisplayLimit * 2)));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>把模型给出的短哈希数组解析回 Hint，保持模型给定的顺序并去重。</summary>
    /// <param name="hashes">短哈希（大小写不敏感，允许前后空白）。</param>
    public IReadOnlyList<IHint> Resolve(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        var resolved = new List<IHint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            foreach (var raw in hashes)
            {
                var key = Normalize(raw);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                if (_known.TryGetValue(key, out var hint))
                {
                    resolved.Add(hint);
                }
            }
        }

        return resolved;
    }

    /// <summary>是否记录过某个短哈希。</summary>
    public bool Contains(string hash)
    {
        lock (_gate)
        {
            return _known.ContainsKey(Normalize(hash));
        }
    }

    private async Task<List<IHint>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var results = new List<IHint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await _provider.SearchAsync(
            query,
            batch =>
            {
                foreach (var hint in batch)
                {
                    var key = HintHash.Short(hint, _hashLength);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(hint);

                    lock (_gate)
                    {
                        _known[key] = hint;
                    }
                }
            },
            TimeSpan.FromMilliseconds(50),
            cancellationToken).ConfigureAwait(false);

        if (results.Count > maxResults)
        {
            results.RemoveRange(maxResults, results.Count - maxResults);
        }

        return results;
    }

    private bool TryResolve(string? hash, out IHint hint)
    {
        lock (_gate)
        {
            return _known.TryGetValue(Normalize(hash), out hint!);
        }
    }

    /// <summary>规整模型可能带上的引号、反引号或 <c>#</c> 前缀。</summary>
    private static string Normalize(string? hash)
    {
        var text = hash?.Trim() ?? string.Empty;
        text = text.Trim('"', '\'', '`', ' ', '\t');

        return text.StartsWith('#') ? text[1..].Trim() : text;
    }

    private static string Truncate(string? text, int limit)
    {
        text ??= string.Empty;
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= limit ? text : text[..limit] + "…";
    }
}
