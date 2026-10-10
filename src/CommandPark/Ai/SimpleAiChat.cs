using System.Text;
using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

/// <summary>
/// 对 <see cref="IChatClient"/> 的封装，使用更简单：
/// 自动维护系统提示词与对话历史，并在发送时把 <see cref="Tools"/> 交给模型。
/// </summary>
/// <param name="client">底层聊天客户端，实例需可被并发复用。</param>
public class SimpleAiChat(IChatClient client) : ISimpleAiChat
{
    private readonly object _messagesGate = new();
    private readonly List<ChatMessage> _messages = [];
    private readonly SemaphoreSlim _sendGate = new(initialCount: 1, maxCount: 1);

    /// <summary>AI 调用的工具。</summary>
    public List<AITool> Tools { get; } = [];

    /// <summary>系统提示词，将在首次发送时作为系统消息加入。</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>可选的请求选项；为 null 时使用默认值。</summary>
    public ChatOptions? Options { get; set; }

    /// <summary>当前会话消息（含系统消息，若已加入）。</summary>
    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (_messagesGate)
            {
                return _messages.ToArray();
            }
        }
    }

    /// <summary>清空对话历史；下一次发送会重新加入系统提示词。</summary>
    public void Clear()
    {
        lock (_messagesGate)
        {
            _messages.Clear();
        }
    }

    /// <summary>
    /// 导出对话内容字符串。
    /// </summary>
    /// <returns>格式为首行系统提示词，此后每行一个 <c>角色 : 内容</c>。</returns>
    public string ExportToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine(SystemPrompt);
        sb.AppendLine();

        ChatMessage[] messages;
        lock (_messagesGate)
        {
            messages = _messages.ToArray();
        }

        foreach (var msg in messages.Where(msg => msg.Role != ChatRole.System))
        {
            sb.Append(msg.Role);
            sb.Append(" : ");
            sb.Append(msg.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 发送一条消息，并等待回复。同一实例上的并发发送会被串行化，避免对话历史错乱。
    /// </summary>
    /// <param name="text">消息内容，作为用户发送。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>异步返回助手的回答。如果 <see cref="Tools"/> 有内容，则可能同时包含工具调用。</returns>
    public async Task<string> Send(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ChatMessage[] request;
            lock (_messagesGate)
            {
                if (_messages.Count == 0 && !string.IsNullOrWhiteSpace(SystemPrompt))
                {
                    _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                }

                _messages.Add(new ChatMessage(ChatRole.User, text));
                request = _messages.ToArray();
            }

            var options = Options?.Clone() ?? new ChatOptions();
            if (Tools.Count > 0)
            {
                options.Tools = [.. Tools];
            }

            var response = await client
                .GetResponseAsync(request, options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            lock (_messagesGate)
            {
                _messages.AddRange(response.Messages);
            }

            return response.Text ?? string.Empty;
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
