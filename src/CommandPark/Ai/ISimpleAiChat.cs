using Microsoft.Extensions.AI;

namespace CommandPark.Ai;

public interface ISimpleAiChat
{
    public List<AITool> Tools { get; }
    public string SystemPrompt { get; set; }
    public string ExportToString();
    public Task<string> Send(string text, CancellationToken cancellationToken = default);
}