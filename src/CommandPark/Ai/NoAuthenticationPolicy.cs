using System.ClientModel.Primitives;

namespace CommandPark.Ai;

/// <summary>
/// 不添加认证头、直接放行请求的客户端管道策略。
/// <para>KiloCode 免费网关不接受默认的 <c>Authorization</c> 头时需要它。</para>
/// </summary>
internal sealed class NoAuthenticationPolicy : AuthenticationPolicy
{
    /// <inheritdoc />
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
    }

    /// <inheritdoc />
    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        return ProcessNextAsync(message, pipeline, currentIndex);
    }
}
