namespace CommandPark.Strings;

public class StringItem(StringItem parent, string current, Func<string, StringItem> indexCallback)
{
    public StringItem Parent { get; } = parent;
    public string Current { get; } = current;
    public StringItem this[string sub] => indexCallback(sub);
    public string GetFormat(params IEnumerable<object> p) => string.Format(Current, p);
}