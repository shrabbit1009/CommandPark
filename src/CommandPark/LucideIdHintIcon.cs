namespace CommandPark;

public class LucideIdHintIcon(string id) : IHintIcon
{
    public string? Id { get; set; } = id;
}
