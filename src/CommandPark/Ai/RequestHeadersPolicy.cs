using System.ClientModel.Primitives;

namespace CommandPark.Ai;

/// <summary>
/// 给每个出站请求强制写入固定请求头（例如 OpenCode Go 需要的 UA 与 x-opencode-session）。
/// <para>
/// 放在 <see cref="PipelinePosition.BeforeTransport"/>，因此会覆盖 SDK 默认写入的同名请求头。
/// </para>
/// </summary>
/// <param name="headers">要写入的请求头。</param>
internal sealed class RequestHeadersPolicy(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
{
    /// <inheritdoc />
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    /// <inheritdoc />
    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Apply(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Apply(PipelineMessage message)
    {
        foreach (var (name, value) in headers)
        {
            message.Request.Headers.Set(name, value);
        }
    }
}
