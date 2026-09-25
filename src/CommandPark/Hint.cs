namespace CommandPark;

public record Hint : IHint
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    public Hint() : this("unknown", "unknown", "unknown")
    {
        
    }

    public Hint(string id, string name, string description, HintRun onRun) : this(id, name, description)
    {
        OnRun = onRun;
    }

    public Hint(string id, string name, string description, float similarity, HintRun onRun) : this(id, name, description)
    {
        OnRun      = onRun;
        Similarity = similarity;
    }

    public Hint(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    /// <inheritdoc />
    public IHintIcon? Icon { get; set; }

    public HintRun OnRun { get; set; } = _ => Task.CompletedTask;

    public Task Run(CancellationToken cancellationToken) => OnRun(cancellationToken);
    public float Similarity { get; set; }
}