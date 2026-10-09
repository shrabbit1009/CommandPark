using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CommandPark.Utils.Clicks;

/// <summary>
/// 32 字节 SHA256 摘要的定长值类型封装。
/// <para>
/// 直接用四个 <see cref="ulong"/> 承载摘要，作为字典键时既不需要 <c>byte[]</c> 分配，
/// 也能得到稳定的相等/哈希语义。写入时通过 <see cref="WriteBytes"/> 直接落到目标缓冲区，
/// 避免每条记录再分配一个数组。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SHA256Store : IEquatable<SHA256Store>
{
    /// <summary>摘要长度（字节）。</summary>
    public const int Size = 32;

    private readonly ulong _a, _b, _c, _d;  // 32 字节拆成 4 个 ulong

    /// <summary>从至少 32 字节的缓冲区读取摘要。</summary>
    public SHA256Store(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new ArgumentException($"SHA256 摘要至少需要 {Size} 字节。", nameof(bytes));
        }

        _a = BinaryPrimitives.ReadUInt64LittleEndian(bytes[..8]);
        _b = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..16]);
        _c = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..24]);
        _d = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..32]);
    }

    /// <summary>把摘要写入至少 32 字节的目标缓冲区，不产生任何分配。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"目标缓冲区至少需要 {Size} 字节。", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination[..8],   _a);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..16],  _b);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..24], _c);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..32], _d);
    }

    /// <summary>转为 32 字节数组（需要分配，供不关心分配的场景使用）。</summary>
    public byte[] GetBytes()
    {
        var result = new byte[Size];
        WriteBytes(result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(SHA256Store other) =>
        _a == other._a && _b == other._b && _c == other._c && _d == other._d;

    public override bool Equals(object? obj) => obj is SHA256Store other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

    public static bool operator ==(SHA256Store left, SHA256Store right) => left.Equals(right);

    public static bool operator !=(SHA256Store left, SHA256Store right) => !left.Equals(right);

    public override string ToString() => Convert.ToHexString(GetBytes());
}
