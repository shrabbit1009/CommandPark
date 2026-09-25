using Lucide.Avalonia;

namespace CommandPark.Ui;

public class LucideHintIcon(LucideIconKind kind) : IHintIcon
{
    public LucideIconKind IconKind { get; } = kind;
    public override string ToString() => IconKind.ToString();
}