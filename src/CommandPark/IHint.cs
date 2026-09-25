namespace CommandPark;

public interface IHint
{
    string Id { get; set; }
    string Name { get; set; }
    string Description { get; set; }
    Task Run(CancellationToken cancellationToken);
    float Similarity { get; set; }
}