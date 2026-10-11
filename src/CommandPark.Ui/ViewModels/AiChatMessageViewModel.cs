using CommandPark.Strings;

namespace CommandPark.Ui.ViewModels;

/// <summary>AI 聊天窗口中的一条消息。</summary>
public sealed class AiChatMessageViewModel
{
    public AiChatMessageViewModel(bool isUser, string text)
    {
        IsUser = isUser;
        Text = text ?? string.Empty;
    }

    /// <summary>true 表示用户消息，false 表示 AI 回复。</summary>
    public bool IsUser { get; }

    /// <summary>true 表示 AI 回复。</summary>
    public bool IsAssistant => !IsUser;

    /// <summary>消息角色显示名。</summary>
    public string Role => IsUser ? Localization.Get("ui.ai.you") : Localization.Get("ui.ai.ai");

    /// <summary>消息正文。</summary>
    public string Text { get; }
}
