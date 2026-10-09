using System.Buffers;

namespace CommandPark.Utils.Clicks;

public static class HashToTimesReader
{
    public const int BlockSize = 36;
    
    /// <summary>
    /// 从流中读取结构
    /// </summary>
    /// <param name="reader">流</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>返回构建好的<see cref="HashToTimes"/>实例。</returns>
    public static async Task<HashToTimes> ReadAsync(Stream reader, CancellationToken cancellationToken = default)
    {
        var htt = new HashToTimes();
        var buffer = new byte[BlockSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 读取一块（32字节sha + 4字节次数）
            if (reader.Length < BlockSize || reader.Length - reader.Position < BlockSize)
                return htt;
            // 开始读
            await reader.ReadExactlyAsync(buffer, cancellationToken);
            // 根据上方规则，写值
            var span = buffer.AsSpan();
            htt[new SHA256Store(span[..32])] = BitConverter.ToInt32(span[32..BlockSize]);
        }
    }

    /// <summary>
    /// 写入到流
    /// </summary>
    /// <param name="htt"><see cref="HashToTimes"/>实例</param>
    /// <param name="writer">流</param>
    /// <param name="cancellationToken">此为异步任务</param>
    public static async Task WriteAsync(HashToTimes htt, Stream writer, CancellationToken cancellationToken = default)
    {
        if (htt.Count == 0)
            return;

        var buffer = new byte[BlockSize];
        
        foreach (var kv in htt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 开始写入块
            var hash = kv.Key.GetBytes();
            var times = BitConverter.GetBytes(kv.Value);
            Array.Copy(hash,  0, buffer, 0,  32);
            Array.Copy(times, 0, buffer, 32, 4);
            await writer.WriteAsync(buffer, cancellationToken);
        }
    }
}