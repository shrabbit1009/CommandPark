using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;

namespace CommandPark.Utils.Clicks;

[StructLayout(LayoutKind.Sequential)]
public readonly struct SHA256Store : IEquatable<SHA256Store>
{
    private readonly ulong _a, _b, _c, _d;  // 32 字节拆成 4 个 ulong

    public SHA256Store(ReadOnlySpan<byte> bytes)
    {
        _a = BinaryPrimitives.ReadUInt64LittleEndian(bytes[..8]);
        _b = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..16]);
        _c = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..24]);
        _d = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..32]);
    }

    public bool Equals(SHA256Store other) =>
        _a == other._a && _b == other._b && _c == other._c && _d == other._d;

    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

    public byte[] GetBytes()
    {
        var result = new byte[32];

        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(0,  8),  _a);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8,  16), _b);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16, 24), _c);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(24, 32), _d);

        return result;
    }
}