using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandPark.Strings;

public interface IStringCollection
{
    public string GetString(string path);
    public string GetFormat(string path, params IEnumerable<object> p);
    public bool TryGetString(string path, out string? value);
}

public class StringCollection(JsonNode node)
{
    /// <summary>
    /// 在找不到时的行为，true 代表找不到时抛出异常；反之则返回索引路径。
    /// </summary>
    public bool ThrowIfNotFound { get; set; } = false;
    
    /// <summary>
    /// 在 JSON 值不是字符串时的行为，true 代表不是字符串时抛出异常；反之则返回索引路径。
    /// </summary>
    public bool ThrowIfNotString { get; set; } = false;

    public bool TryGetString(string path, out string? value)
    {
        value = null;
        var node = JumpPath(path);

        if (node is null || node.GetValueKind() != JsonValueKind.String) return false;
        
        value = node.GetValue<string>();
        return true;
    }

    public string GetString(string path)
    {
        var node = JumpPath(path);
        
        if (node is null)
            return ThrowIfNotFound ? throw new StringPathNotFoundException($"找不到当前路径中的字符串'{path}'。") : path;
        
        if (node.GetValueKind() != JsonValueKind.String)
            return ThrowIfNotString ? throw new StringPathNotStringException($"'{path}'不是一个字符串，而是{node.GetValueKind()}。") : path;
        
        return node.GetValue<string>();
    }

    public string GetFormat(string path, params IEnumerable<object> p) => string.Format(GetString(path), p);

    private JsonNode? JumpPath(string path)
    {
        var current = node;
        
        foreach (var p in path.Split('/'))
        {
            current = node[p];
            
            if (current is null)
                return null;
        }
        
        return current;
    }
}

/// <summary>
/// 组合各 <see cref="IStringCollection"/>
/// </summary>
public class MultiStringCollection : IStringCollection
{
    public List<IStringCollection> Chain { get; set; } = [];
    
    public string GetString(string path)
    {
        foreach (var c in Chain)
        {
            if (c.TryGetString(path, out var value))
                return value!;
        }

        return path;
    }

    public string GetFormat(string path, params IEnumerable<object> p)
    {
        foreach (var c in Chain)
        {
            if (c.TryGetString(path, out var value))
                return value!;
        }

        return path;
    }

    public bool TryGetString(string path, out string? value)
    {
        value = null;
        foreach (var c in Chain)
        {
            if (c.TryGetString(path, out value))
                return true;
        }
        
        return false;
    }
}

public class StringPathNotFoundException : KeyNotFoundException
{
    public StringPathNotFoundException()
    {
    }

    public StringPathNotFoundException(string message) : base(message)
    {
    }

    public StringPathNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}

public class StringPathNotStringException : Exception
{
    public StringPathNotStringException()
    {
    }

    public StringPathNotStringException(string message) : base(message)
    {
    }

    public StringPathNotStringException(string message, Exception inner) : base(message, inner)
    {
    }
}