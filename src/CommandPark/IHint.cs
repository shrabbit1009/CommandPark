namespace CommandPark;

public interface IHint
{
    string Name { get; set; }
    string Description { get; set; }
    Task Run(CancellationToken cancellationToken);
}