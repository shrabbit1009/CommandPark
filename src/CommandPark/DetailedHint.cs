namespace CommandPark;

/// <summary>默认的详情 Hint 实现。</summary>
public record DetailedHint : Hint, IDetailedHint
{
    public DetailedHint(
        string id,
        string name,
        string description,
        string details,
        HintRun onRun) : base(id, name, description, onRun)
    {
        Details = details;
    }

    public DetailedHint(
        string id,
        string name,
        string description,
        float similarity,
        HintRun onRun,
        string details) : base(id, name, description, similarity, onRun)
    {
        Details = details;
    }

    public string Details { get; set; }
}