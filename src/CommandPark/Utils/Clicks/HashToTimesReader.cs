using System.Buffers;
using System.Buffers.Binary;

namespace CommandPark.Utils.Clicks;

/// <summary>
/// <see cref="HashToTimes"/> 的二进制读写（htt 格式）。
/// <para>
/// 每条记录固定 <see cref="BlockSize"/> 字节：32 字节 SHA256 摘要 + 4 字节小端 int 计数。
/// 读写都在一次性租用的大缓冲区上分块进行，只有摘要与整数，既没有逐条分配，
/// 也不是 JSON / XML 这类会把明文写进文件的标记语言。
/// </para>
/// </summary>
public static class HashToTimesReader
{
    /// <summary>单条记录长度：32 字节摘要 + 4 字节计数。</summary>
    public const int BlockSize = 36;

    /// <summary>一次租用的缓冲区大小；按块装载/落盘，避免逐条读写。</summary>
    private const int BufferSize = 4096;

    /// <summary>
    /// 从流中读取全部记录，构造 <see cref="HashToTimes"/>。
    /// </summary>
    /// <param name="reader">数据流；可以是不可寻址的流，读到末尾自然结束。</param>
    /// <param name="maxEntries">最多保留的条目数，超出时按文件顺序淘汰最旧的记录。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <remarks>不足一条记录的尾部字节会被忽略（与旧格式的容错行为一致）。</remarks>
    public static async Task<HashToTimes> ReadAsync(
        Stream reader,
        int maxEntries = HashToTimes.DefaultMaxEntries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var htt = new HashToTimes(maxEntries);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var carry = 0;  // 上一次没凑满一条记录的残留字节，始终放在缓冲区开头
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 把残留放在开头，直接从 carry 之后续读；读满缓冲区分块解析。
                var read = await reader
                    .ReadAsync(buffer.AsMemory(carry, buffer.Length - carry), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var available = carry + read;
                var offset = 0;
                var span = buffer.AsSpan(0, available);
                while (available - offset >= BlockSize)
                {
                    var block = span.Slice(offset, BlockSize);
                    var key = new SHA256Store(block[..32]);
                    var value = BinaryPrimitives.ReadInt32LittleEndian(block[32..]);
                    htt.Set(key, value);
                    offset += BlockSize;
                }

                carry = available - offset;
                if (carry > 0 && offset > 0)
                {
                    // 把没解析完的尾部搬到缓冲区开头，等下一次续读。
                    span.Slice(offset, carry).CopyTo(buffer);
                }
            }

            return htt;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 把 <see cref="HashToTimes"/> 按插入顺序写入流。
    /// </summary>
    /// <param name="htt">要写入的实例。</param>
    /// <param name="writer">目标流。</param>
    /// <param name="cancellationToken">取消。</param>
    /// <remarks>空实例不会写出任何字节；调用方需自行确保流的写入位置符合预期。</remarks>
    public static async Task WriteAsync(
        HashToTimes htt,
        Stream writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(htt);
        ArgumentNullException.ThrowIfNull(writer);

        if (htt.Count == 0)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var filled = 0;
        try
        {
            foreach (var (key, value) in htt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (buffer.Length - filled < BlockSize)
                {
                    await writer
                        .WriteAsync(buffer.AsMemory(0, filled), cancellationToken)
                        .ConfigureAwait(false);
                    filled = 0;
                }

                var block = buffer.AsSpan(filled, BlockSize);
                key.WriteBytes(block[..32]);
                BinaryPrimitives.WriteInt32LittleEndian(block[32..], value);
                filled += BlockSize;
            }

            if (filled > 0)
            {
                await writer
                    .WriteAsync(buffer.AsMemory(0, filled), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
