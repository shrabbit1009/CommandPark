namespace CommandPark;

/// <summary>
/// 简单 LNK (10.0) 解析程序
/// </summary>
public class LnkFileParser
{
    public static Task<LnkFile> ParseAsync(string filepath, CancellationToken cancellationToken = default)
    {
        var data = File.ReadAllBytesAsync(filepath, cancellationToken);
        
    }

    private static void ValidHeader(byte[] data)
    {
        byte[] target = [
            0x4c, 0x00, 0x00, 0x00,
            0x01, 0x14, 0x02, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xc0, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x46
        ];

        if (!data.AsSpan()[..target.Length].SequenceEqual(target)) 
            throw new LnkFileParseException("Invalid header");
    }
    
    [Flags]
    private enum LinkFlags
    {
        KeepLocalIDListForUNCTarget = 1 << 0,
        PreferEnvironmentPath = 1 << 1,
        UnaliasOnSave = 1 << 2,
        AllowLinkToLink =  1 << 3,
        DisableKnownFolderAlias  = 1 << 4,
        DisableKnownFolderTracking = 1 << 5,
        Special = 1 << 6,
        DisableLinkPathTracking =  1 << 7,
        EnableTargetMetadata =  1 << 8,
        ForceNoLinkTrack =  1 << 9,
    }
}

public class LnkFile
{
    public string TargetPath { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public class LnkFileParseException : Exception
{
    public LnkFileParseException()
    {
    }

    public LnkFileParseException(string message) : base(message)
    {
    }

    public LnkFileParseException(string message, Exception inner) : base(message, inner)
    {
    }
}