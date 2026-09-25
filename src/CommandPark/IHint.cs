namespace CommandPark;

public interface IHint
{
    string Id { get; set; }
    string Name { get; set; }
    string Description { get; set; }

    /// <summary>条目图标，可为 null（由界面决定用什么默认图标）。</summary>
    IHintIcon? Icon { get; set; }

    Task Run(CancellationToken cancellationToken = default);
    float Similarity { get; set; }
}