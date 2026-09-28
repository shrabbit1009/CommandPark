namespace CommandPark;

/// <summary>默认的详情 Hint 实现。</summary>
public record DetailedHint : Hint, IDetailedHint
{
    public DetailedHint(
        string id,
        string name,
        string description,
        string details,
        HintRun onRun,
        string? actionName = null) : base(id, name, description, onRun)
    {
        Details = details;
        ActionName = actionName;
    }

    public DetailedHint(
        string id,
        string name,
        string description,
        float similarity,
        HintRun onRun,
        string details,
        string? actionName = null) : base(id, name, description, similarity, onRun)
    {
        Details = details;
        ActionName = actionName;
    }

    public string Details { get; set; }
    public string? ActionName { get; set; }
}