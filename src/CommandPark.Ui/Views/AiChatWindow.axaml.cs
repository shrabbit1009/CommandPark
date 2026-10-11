using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommandPark.Ai;
using CommandPark.Strings;
using CommandPark.Ui.ViewModels;

namespace CommandPark.Ui.Views;

/// <summary>
/// 简单 AI 聊天窗口：直接输入提示词与 AI 对话，支持复制 / 导出，关闭后不保留。
/// 所有操作都可以用键盘完成：Enter 发送、Esc 关闭、Ctrl+E 导出、Ctrl+Shift+C 复制，
/// 以及「相对修饰键 + 1~4」的快速选择（相对修饰键与主界面 AI 快捷键一致）。
/// </summary>
public partial class AiChatWindow : Window
{
    private readonly KeyModifiers _shortcutModifier;
    private readonly TextBox? _inputBox;

    public AiChatWindow()
    {
        InitializeComponent();

        // 与主界面一致：使用与 quickSelect.modifier「相对」的另一个修饰键，
        // 避免与快速选择冲突，也避开 Alt+F1 与 NVIDIA 截图冲突。
        QuickSelectModifier relativeModifier;
        try
        {
            relativeModifier = QuickSelectConfig.GetRelativeModifier(
                QuickSelectConfig.ReadModifier(ConfigFile.Load()));
        }
        catch (Exception)
        {
            relativeModifier = QuickSelectConfig.GetRelativeModifier(QuickSelectConfig.DefaultModifier);
        }

        _shortcutModifier = relativeModifier == QuickSelectModifier.Ctrl
            ? KeyModifiers.Control
            : KeyModifiers.Alt;

        var modifierName = relativeModifier == QuickSelectModifier.Ctrl ? "Ctrl" : "Alt";

        ISimpleAiChat? chat = null;
        string? creationError = null;
        try
        {
            chat = AiRuntime.TryCreateChat(AiPrompts.Chat);
        }
        catch (Exception exception)
        {
            // 端点不合法等创建失败：仍然打开窗口，但把原因显示出来。
            creationError = exception.Message;
        }

        var viewModel = new AiChatWindowViewModel(chat, modifierName);
        if (creationError is not null)
        {
            viewModel.ErrorMessage = creationError;
        }

        viewModel.Messages.CollectionChanged += (_, _) => ScrollToEnd();
        DataContext = viewModel;

        _inputBox = this.FindControl<TextBox>("InputBox");

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => _inputBox?.Focus();
    }

    private AiChatWindowViewModel? ViewModel => DataContext as AiChatWindowViewModel;

    /// <summary>窗口级快捷键：Esc / Ctrl+E / Ctrl+Shift+C / PageUp / PageDown / 修饰键 + 1~4。</summary>
    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
            return;
        }

        // Enter 发送、Shift+Enter 换行。
        // TextBox 的 AcceptsReturn 会在气泡阶段把 Enter 当成换行并标记已处理，
        // 因此必须在隧道阶段抢先处理；只在输入框获得焦点时生效，焦点在按钮上时保留按钮的 Enter 激活。
        if (e.Key == Key.Enter &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            _inputBox is { IsFocused: true })
        {
            e.Handled = true;
            await SendAsync();
            return;
        }

        if (e.Key is Key.PageUp or Key.PageDown)
        {
            e.Handled = true;
            ScrollPage(e.Key == Key.PageUp ? -1 : 1);
            return;
        }

        if (e.Key == Key.C &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            await CopyAsync();
            return;
        }

        if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await ExportAsync();
            return;
        }

        if (e.KeyModifiers == _shortcutModifier && e.Key is >= Key.D1 and <= Key.D4)
        {
            e.Handled = true;
            switch ((int)e.Key - (int)Key.D1)
            {
                case 0:
                    await SendAsync();
                    break;
                case 1:
                    await CopyAsync();
                    break;
                case 2:
                    await ExportAsync();
                    break;
                case 3:
                    Close();
                    break;
            }
        }
    }


    private async Task SendAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await viewModel.SendAsync();
        ScrollToEnd();
    }

    private async Task CopyAsync()
    {
        if (ViewModel is not { } viewModel || viewModel.Messages.Count == 0)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(viewModel.BuildExport());
        }
    }

    private async Task ExportAsync()
    {
        if (ViewModel is not { } viewModel || viewModel.Messages.Count == 0)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localization.Get("ui.ai.exportTitle"),
            SuggestedFileName = $"commandpark-ai-chat-{DateTime.Now:yyyyMMdd-HHmmss}.md",
            DefaultExtension = "md",
            FileTypeChoices =
            [
                new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
                new FilePickerFileType(Localization.Get("ui.ai.fileTypeText")) { Patterns = ["*.txt"] },
            ],
        });

        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(viewModel.BuildExport());
        await writer.FlushAsync();
    }

    private void ScrollToEnd() =>
        Dispatcher.UIThread.Post(
            () => this.FindControl<ScrollViewer>("MessagesScroll")?.ScrollToEnd(),
            DispatcherPriority.Background);

    private void ScrollPage(int direction)
    {
        if (this.FindControl<ScrollViewer>("MessagesScroll") is not { } scroll)
        {
            return;
        }

        var page = Math.Max(60, scroll.Viewport.Height - 40) * direction;
        var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var target = Math.Clamp(scroll.Offset.Y + page, 0, maximum);
        scroll.Offset = new Vector(scroll.Offset.X, target);
    }

    private void Send_OnClick(object? sender, RoutedEventArgs e) => _ = SendAsync();

    private void Copy_OnClick(object? sender, RoutedEventArgs e) => _ = CopyAsync();

    private void Export_OnClick(object? sender, RoutedEventArgs e) => _ = ExportAsync();

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
