namespace CommandPark;

public class Hint : IHint
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

    public Hint(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    public HintRun OnRun { get; set; } = _ => Task.CompletedTask;

    public Task Run(CancellationToken cancellationToken) => OnRun(cancellationToken);
    public float Similarity { get; set; }
}