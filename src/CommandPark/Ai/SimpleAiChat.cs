using System.Text;
using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

/// <summary>
/// 对<see cref="IChatClient"/>的封装，使用更简单。
/// </summary>
/// <param name="client">客户端实例</param>
public class SimpleAiChat(IChatClient client) : ISimpleAiChat
{
    private readonly List<ChatMessage> _messages = [];
    
    /// <summary>
    /// AI 调用的工具
    /// </summary>
    public List<AITool> Tools { get; } = [];
    
    /// <summary>
    /// 系统提示词，将首次输入并作为系统发送。
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 导出对话内容字符串
    /// </summary>
    /// <returns>格式为首行系统提示词，此后每行一个 角色\t: 内容</returns>
    public string ExportToString()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine(SystemPrompt);
        sb.AppendLine();

        foreach (var msg in _messages
                    .Where(msg => msg.Role != ChatRole.System))
        {
            /*
             * 你是一个有问题的助手。
             * 
             * User : 啊哈哈哈会话
             * Assistant    : 我直接用最直白的话告诉你，这是不对的说法！\r\n\r\n你这种表述是错误的。问题在于：**整个句子没有一个字，但却连成了一句话。**\r\n## 可以这样修改：\r\n```\r\n啊哈哈哈哈活活或或红红火火恍恍惚惚 MJ\r\n```\r\n\r\n需要我为你整理一份详细的"说话指南吗"？
             */
            
            sb.Append(msg.Role);
            sb.Append('\t');
            sb.Append(": ");
            sb.Append(msg.Text);
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// 发送一条消息，并等待回复
    /// </summary>
    /// <param name="text">消息内容，作为用户发送。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <returns>异步返回助手的回答。如果<see cref="Tools"/>有内容，则可能同时包含工具调用。</returns>
    public async Task<string> Send(string text, CancellationToken cancellationToken = default)
    {
        if (_messages.Count == 0)
            _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        
        _messages.Add(new ChatMessage(ChatRole.User, text));

        var options = Tools.Count > 0 ? new ChatOptions
        {
            Tools = Tools,
        } : null;
        
        var resp = await client.GetResponseAsync(_messages, options, cancellationToken: cancellationToken);
        _messages.AddRange(resp.Messages);
        
        return resp.Text;
    }
}