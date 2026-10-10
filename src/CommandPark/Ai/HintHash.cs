using CommandPark.Utils.Clicks;

namespace CommandPark.Ai;

/// <summary>
/// Hint 的哈希摘要工具：把「id + 名称 + 描述」做 SHA256 后转成十六进制，并截短成模型易读的短哈希。
/// <para>
/// 短哈希只用于 AI 与本地之间的“引用编号”，不携带任何额外信息，也无法反推 Hint 内容。
/// 摘要复用 <see cref="HintClickStore.HashOf(IHint)"/>，与点击权重的摘要保持一致。
/// </para>
/// </summary>
public static class HintHash
{
    /// <summary>默认短哈希长度（截取 SHA256 十六进制的前 8 个字符）。</summary>
    public const int DefaultLength = 8;

    /// <summary>允许的最短长度。</summary>
    public const int MinLength = 4;

    /// <summary>允许的最长长度（完整 SHA256 十六进制长度）。</summary>
    public const int MaxLength = SHA256Store.Size * 2;

    /// <summary>计算 Hint 的完整 SHA256 摘要。</summary>
    public static SHA256Store DigestOf(IHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        return HintClickStore.HashOf(hint);
    }

    /// <summary>计算 Hint 的短哈希（默认取 8 个十六进制字符，小写）。</summary>
    public static string Short(IHint hint, int length = DefaultLength) =>
        Short(DigestOf(hint), length);

    /// <summary>把 32 字节摘要截短成十六进制短哈希（小写）。</summary>
    public static string Short(SHA256Store digest, int length = DefaultLength)
    {
        var clamped = Math.Clamp(length, MinLength, MaxLength);
        Span<byte> bytes = stackalloc byte[SHA256Store.Size];
        digest.WriteBytes(bytes);

        Span<char> chars = stackalloc char[SHA256Store.Size * 2];
        var written = 0;
        foreach (var b in bytes)
        {
            written += WriteHex(b, chars[written..]);
        }

        return new string(chars[..clamped]);
    }

    private static int WriteHex(byte value, Span<char> destination)
    {
        const string digits = "0123456789abcdef";
        destination[0] = digits[value >> 4];
        destination[1] = digits[value & 0xF];
        return 2;
    }
}
