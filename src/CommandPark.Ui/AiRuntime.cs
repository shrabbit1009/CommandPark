using System;
using CommandPark.Ai;
using Microsoft.Extensions.AI;

namespace CommandPark.Ui;

/// <summary>
/// AI 配置与客户端的进程内入口。
/// <para>
/// 配置读取与客户端创建都是懒加载：只有真正进入 AI 模式或打开聊天窗口时才读盘 / 建连；
/// 客户端在整个进程内复用。
/// </para>
/// </summary>
internal static class AiRuntime
{
    private static readonly object Gate = new();

    private static bool _loaded;
    private static AiConfig? _config;
    private static IChatClient? _client;

    /// <summary>读取到的 AI 配置；配置文件中没有 <c>ai</c> 节点时为 null。</summary>
    public static AiConfig? Config
    {
        get
        {
            EnsureLoaded();
            return _config;
        }
    }

    /// <summary>AI 的三项必填配置是否都已填写。</summary>
    public static bool IsConfigured
    {
        get
        {
            EnsureLoaded();
            return _config is { IsConfigured: true };
        }
    }

    /// <summary>取共享的聊天客户端；配置不完整时返回 null。</summary>
    public static IChatClient? TryGetClient()
    {
        EnsureLoaded();
        if (_config is not { IsConfigured: true })
        {
            return null;
        }

        lock (Gate)
        {
            return _client ??= AiChatClientFactory.Create(_config);
        }
    }

    /// <summary>创建一个独立会话（不保留历史）；配置不完整时返回 null。</summary>
    public static ISimpleAiChat? TryCreateChat(string systemPrompt)
    {
        var client = TryGetClient();
        return client is null
            ? null
            : new SimpleAiChat(client) { SystemPrompt = systemPrompt };
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            try
            {
                _config = AiConfig.Read(ConfigFile.Load());
            }
            catch (Exception)
            {
                // 配置损坏不能影响程序启动；此时视为没有配置 AI。
                _config = null;
            }

            _loaded = true;
        }
    }
}
