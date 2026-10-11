using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using CommandPark.Ai;
using CommandPark.Strings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandPark.Ui.ViewModels;

/// <summary>
/// 简单 AI 聊天窗口的视图模型。
/// <para>
/// 会话保存在内存里，窗口关闭即随视图模型一起丢弃，不做任何持久化。
/// 导出 / 复制 / 保存由视图层读取 <see cref="BuildExport"/> 的结果完成。
/// </para>
/// </summary>
public partial class AiChatWindowViewModel : ViewModelBase
{
    private readonly ISimpleAiChat? _chat;

    /// <param name="chat">AI 会话；为 null 表示尚未配置 AI，界面只提示不可用。</param>
    /// <param name="modifierName">快速选择修饰键显示名（Alt / Ctrl）。</param>
    public AiChatWindowViewModel(ISimpleAiChat? chat, string modifierName = "Alt")
    {
        _chat = chat;
        ModifierName = modifierName;

        if (chat is null)
        {
            ErrorMessage = Localization.Get("ai.config.notConfigured");
        }

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));
    }

    /// <summary>当前对话消息。</summary>
    public ObservableCollection<AiChatMessageViewModel> Messages { get; } = [];

    /// <summary>快速选择修饰键显示名。</summary>
    public string ModifierName { get; }

    /// <summary>发送按钮文案。</summary>
    public string SendLabel => Localization.Format("chat.button.send", ModifierName);

    /// <summary>复制按钮文案。</summary>
    public string CopyLabel => Localization.Format("chat.button.copy", ModifierName);

    /// <summary>导出按钮文案。</summary>
    public string ExportLabel => Localization.Format("chat.button.export", ModifierName);

    /// <summary>关闭按钮文案。</summary>
    public string CloseLabel => Localization.Get("chat.button.close");

    /// <summary>是否已有对话可复制 / 导出。</summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>是否可以发送。</summary>
    public bool CanSend =>
        _chat is not null && !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    /// <summary>输入框内容。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial string InputText { get; set; } = string.Empty;

    /// <summary>是否正在等待 AI 回复。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial bool IsBusy { get; set; }

    /// <summary>错误提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>是否有错误提示。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>发送当前输入并等待回复。</summary>
    public async Task SendAsync()
    {
        if (_chat is null || IsBusy)
        {
            return;
        }

        var text = InputText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        ErrorMessage = null;
        InputText = string.Empty;
        Messages.Add(new AiChatMessageViewModel(isUser: true, text));
        IsBusy = true;

        try
        {
            var reply = await _chat.Send(text);
            Messages.Add(new AiChatMessageViewModel(isUser: false, reply));
        }
        catch (OperationCanceledException)
        {
            // 主动取消不算错误。
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把当前对话导出为 Markdown 文本。</summary>
    public string BuildExport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + Localization.Get("chat.export.heading"));
        sb.AppendLine();
        sb.Append(Localization.Get("chat.export.time")).AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();

        foreach (var message in Messages)
        {
            sb.AppendLine("## " + (message.IsUser ? Localization.Get("chat.role.user") : Localization.Get("chat.role.assistant")));
            sb.AppendLine(message.Text);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
