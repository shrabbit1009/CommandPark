using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommandPark.Utils;
using CommandPark.Utils.SourceHelpers;

namespace CommandPark.Sources;

/// <summary>
/// Bing 网络搜索源。仅在输入 <c>b:</c> 或 <c>bs:</c> 前缀时运行，同时提供 Bing 搜索建议。
/// </summary>
public class BingSearchSource : IHintSource, IHintWithPrefix, IConfigReader
{
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = ["bs:", "b:"];

    private readonly ISearchEngineHelper _helper;

    public BingSearchSource() : this(new BingHelper())
    {
    }

    public BingSearchSource(ISearchEngineHelper helper)
    {
        _helper = helper;
    }

    /// <summary>前缀模式：true 时只有带前缀才运行。</summary>
    public bool PrefixMode { get; set; } = true;

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hasPrefix = HintPrefix.TryStrip(query, out var rawQuery, Prefixes);
        if (PrefixMode && !hasPrefix)
        {
            yield break;
        }

        var keyword = rawQuery.Trim();
        if (keyword.Length == 0)
        {
            yield break;
        }

        var boost = hasPrefix ? 1.0f : 0.0f;
        var directUrl = _helper.BuildSearchUrl(keyword);

        yield return new Hint(
            "bing:search",
            $"Bing 搜索：{keyword}",
            "在 Bing 中执行精确搜索。",
            1.0f + boost,
            ct => OpenUrlAsync(directUrl, ct))
        {
            Icon = new LucideIdHintIcon("Search"),
        };

        string[] suggestions;
        try
        {
            suggestions = await _helper.GetSuggestedItems(keyword, cancellationToken) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 建议接口失败不应影响直接搜索；直接搜索 Hint 已经产出。
            suggestions = [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            keyword,
        };

        foreach (var suggestion in suggestions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = suggestion.Trim();
            if (text.Length == 0 || !seen.Add(text))
            {
                continue;
            }

            var similarity = ComputeSimilarityHelper.ComputeSimilarity(keyword, text);
            yield return new Hint(
                "bing:suggestion",
                text,
                "Bing 搜索建议。",
                Math.Max(0.5f, similarity) + boost,
                ct => OpenUrlAsync(_helper.BuildSearchUrl(text), ct))
            {
                Icon = new LucideIdHintIcon("Search"),
            };
        }
    }

    private static Task OpenUrlAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            Arguments = $"\"{url}\"",
            UseShellExecute = true,
        };
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources ||
            sources["bing"] is not JsonObject section)
        {
            return false;
        }

        PrefixMode = section["prefixMode"]?.GetValue<bool>() ?? PrefixMode;
        return true;
    }
}