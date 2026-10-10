using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

/// <summary>一次 AI 筛选 Hint 的结果。</summary>
/// <param name="Answer">AI 的说明性文本（已去掉末尾的短哈希数组行）。</param>
/// <param name="Hints">按模型给出的短哈希解析出来的 Hint，顺序与模型一致。</param>
public sealed record AiHintResult(string Answer, IReadOnlyList<IHint> Hints);

/// <summary>
/// AI 处理模式的核心流程：让模型借助工具筛选 Hint，再把最后一行短哈希数组解析回真实 Hint。
/// </summary>
public static class AiHintSearch
{
    /// <summary>兜底搜索时最多读取的候选条数。</summary>
    public const int FallbackSearchResults = 50;

    /// <summary>
    /// 执行一次 AI 筛选。返回 AI 的说明文本与解析出来的 Hint（最多 <see cref="AiConfig.MaxResults"/> 条）。
    /// </summary>
    /// <param name="client">聊天客户端（应带自动工具调用能力）。</param>
    /// <param name="provider">本地 Hint 提供者。</param>
    /// <param name="prompt">用户输入的自然语言。</param>
    /// <param name="config">AI 配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<AiHintResult> RunAsync(
        IChatClient client,
        IHintProvider provider,
        string prompt,
        AiConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(config);

        var session = new HintSearchSession(provider);
        var chat = new SimpleAiChat(client)
        {
            SystemPrompt = AiPrompts.HintFilter(config.MaxResults),
            Options = new ChatOptions { Temperature = config.Temperature },
        };
        chat.Tools.AddRange(session.Tools);

        var text = await chat.Send(prompt, cancellationToken).ConfigureAwait(false);

        var hashes = ParseLastLineHashArray(text);
        var hints = session.Resolve(hashes).Take(config.MaxResults).ToArray();

        // 兜底：模型可能没有调用工具就直接给出了短哈希。用用户提示词补一次搜索，尽量把哈希解析出来。
        if (hints.Length == 0 && hashes.Count > 0)
        {
            try
            {
                await session.SearchHintsAsync(prompt, FallbackSearchResults, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 兜底搜索失败不影响主流程：最多只是解析不到结果。
            }

            hints = session.Resolve(hashes).Take(config.MaxResults).ToArray();
        }

        return new AiHintResult(StripTrailingHashLine(text), hints);
    }

    /// <summary>
    /// 读取 AI 回复中最后一行的单行 JSON 字符串数组。
    /// <para>
    /// 严格遵循约定：只检查最后一个非空行，必须是形如 <c>["a1b2c3d4"]</c> 的纯字符串数组；
    /// 解析失败（含被代码块包裹、使用对象包装的情况）时返回空数组。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ParseLastLineHashArray(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return TryParseJsonStringArray(line) ?? [];
        }

        return [];
    }

    /// <summary>把 AI 回复末尾的短哈希数组行去掉，只保留人可读的说明文本。</summary>
    public static string StripTrailingHashLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var lastIndex = -1;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Trim().Length > 0)
            {
                lastIndex = i;
                break;
            }
        }

        if (lastIndex >= 0 && TryParseJsonStringArray(lines[lastIndex].Trim()) is not null)
        {
            return string.Join('\n', lines[..lastIndex]).Trim();
        }

        return text.Trim();
    }

    private static IReadOnlyList<string>? TryParseJsonStringArray(string line)
    {
        if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    items.Add(value.Trim());
                }
            }

            return items;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
