using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CommandPark.Ai;

/// <summary>
/// 按 <see cref="AiConfig"/> 创建带自动工具调用能力的 OpenAI 兼容聊天客户端。
/// </summary>
public static class AiChatClientFactory
{
    /// <summary>聊天补全路径后缀；用户直接填完整端点时会被剥掉。</summary>
    private const string ChatCompletionsSuffix = "/chat/completions";

    /// <summary>
    /// 创建聊天客户端。返回的实例可安全复用于多个会话与并发请求。
    /// </summary>
    /// <param name="config">AI 配置，必须已经填写齐全。</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">配置不完整或端点不是合法的绝对 URI。</exception>
    public static IChatClient Create(AiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.IsConfigured)
        {
            throw new InvalidOperationException(
                $"AI 配置不完整，缺少：{string.Join('、', config.MissingFields)}。");
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = ParseEndpoint(config.Endpoint),
        };

        // OpenCode Go 等提供商要求在请求头上带固定字段（UA、会话 id）。
        var headers = config.BuildRequestHeaders();
        if (headers.Count > 0)
        {
            options.AddPolicy(new RequestHeadersPolicy(headers), PipelinePosition.BeforeTransport);
        }

        var openAiClient = config.SendApiKey
            ? new OpenAIClient(new ApiKeyCredential(config.ApiKey), options)
            : CreateAnonymousClient(options);

        var rawClient = openAiClient.GetChatClient(config.Model).AsIChatClient();

        return new ChatClientBuilder(rawClient)
            .UseFunctionInvocation()
            .Build();
    }

    /// <summary>免费网关不接受默认认证头，这里走“无认证策略”。</summary>
    private static OpenAIClient CreateAnonymousClient(OpenAIClientOptions options)
    {
#pragma warning disable OPENAI001 // OpenAI SDK 目前把无认证构造函数标记为实验 API。
        return new OpenAIClient(new NoAuthenticationPolicy(), options);
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// 把配置里的端点规整成 OpenAI SDK 需要的基础地址：
    /// 允许用户直接填写 <c>.../chat/completions</c>，这里会把该后缀去掉。
    /// </summary>
    private static Uri ParseEndpoint(string endpoint)
    {
        var text = endpoint.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"AI 端点不是合法的 http(s) 地址：{endpoint}");
        }

        var normalized = uri.GetLeftPart(UriPartial.Path);
        if (normalized.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^ChatCompletionsSuffix.Length];
        }

        normalized = normalized.TrimEnd('/');
        return new Uri(normalized, UriKind.Absolute);
    }
}
