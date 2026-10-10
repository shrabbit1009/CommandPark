using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

/// <summary>对 <see cref="IChatClient"/> 的轻量封装，负责维护对话历史、系统提示词与工具。</summary>
public interface ISimpleAiChat
{
    /// <summary>发送请求时携带的工具。</summary>
    List<AITool> Tools { get; }

    /// <summary>系统提示词；在会话第一次发送时作为系统消息加入。</summary>
    string SystemPrompt { get; set; }

    /// <summary>可选的请求选项（温度、最大输出 token 等）；为 null 时使用默认值。</summary>
    ChatOptions? Options { get; set; }

    /// <summary>当前会话消息（含系统消息，若已加入）。</summary>
    IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>清空对话历史；下一次发送会重新加入系统提示词。</summary>
    void Clear();

    /// <summary>导出对话内容字符串。</summary>
    string ExportToString();

    /// <summary>发送一条用户消息并等待回复。</summary>
    /// <param name="text">作为用户发送的消息内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>助手回复的纯文本；<see cref="Tools"/> 非空时可能同时包含工具调用。</returns>
    Task<string> Send(string text, CancellationToken cancellationToken = default);
}
