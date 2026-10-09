using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CommandPark.Utils.Clicks;
using JetBrains.Annotations;
using Xunit.Abstractions;

namespace CommandPark.Tests.Utils.Clicks;

[TestSubject(typeof(HashToTimesReader))]
public class HashToTimesReaderTest
{
    private static readonly SHA256Store Hello = GetSha256("Hello");
    private static readonly SHA256Store World = GetSha256("World");

    private readonly ITestOutputHelper _testOutputHelper;

    public HashToTimesReaderTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task FullTest()
    {
        var htt = new HashToTimes
        {
            [Hello] = 1,
            [World] = 2,
        };

        var stream = new MemoryStream();
        await HashToTimesReader.WriteAsync(htt, stream);
        stream.Position = 0;
        var parsed = await HashToTimesReader.ReadAsync(stream);

        Assert.Equal(htt[Hello], parsed[Hello]);
        Assert.Equal(htt[World], parsed[World]);
    }

    [Fact]
    public async Task StressTest()
    {
        // 只压 htt 自身的读写：摘要提前算好，避免把 SHA256 的成本算进基准。
        var timer = new Stopwatch();
        for (var i = 0; i < 10000; i++)
        {
            if (i == 1)
            {
                timer.Start();
            }

            await FullTest();
        }

        timer.Stop();

        var ms      = timer.ElapsedMilliseconds;
        var nsPerOp = timer.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency) / 9999;
        _testOutputHelper.WriteLine($"总用时 {ms}ms, 平均每次 {nsPerOp:f2} 纳秒");
    }

    [Fact]
    public async Task Read_EmptyStream_ReturnsEmpty()
    {
        var parsed = await HashToTimesReader.ReadAsync(new MemoryStream());

        Assert.Equal(0, parsed.Count);
    }

    [Fact]
    public async Task Write_EmptyStore_WritesNothing()
    {
        var stream = new MemoryStream();

        await HashToTimesReader.WriteAsync(new HashToTimes(), stream);

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task RoundTrip_PreservesInsertionOrder()
    {
        var htt = new HashToTimes();
        htt.Add(Hello);
        htt.Add(World);
        var extra = GetSha256("Extra");
        htt.Add(extra);

        var stream = new MemoryStream();
        await HashToTimesReader.WriteAsync(htt, stream);
        stream.Position = 0;
        var parsed = await HashToTimesReader.ReadAsync(stream);

        Assert.Equal(new[] { Hello, World, extra }, parsed.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public async Task Read_IgnoresPartialTrailingBlock()
    {
        var stream = new MemoryStream();
        await HashToTimesReader.WriteAsync(new HashToTimes { [Hello] = 1 }, stream);
        stream.Write(new byte[10]);   // 尾部半条记录：忽略
        stream.Position = 0;

        var parsed = await HashToTimesReader.ReadAsync(stream);

        Assert.Equal(1, parsed.Count);
        Assert.Equal(1, parsed[Hello]);
    }

    [Fact]
    public async Task Read_EnforcesMaxEntries_KeepingNewest()
    {
        var htt = new HashToTimes();
        for (var i = 0; i < 5; i++)
        {
            htt.Add(GetSha256($"item-{i}"));
        }

        var stream = new MemoryStream();
        await HashToTimesReader.WriteAsync(htt, stream);
        stream.Position = 0;

        var parsed = await HashToTimesReader.ReadAsync(stream, maxEntries: 3);

        Assert.Equal(3, parsed.Count);
        Assert.False(parsed.ContainsKey(GetSha256("item-0")));
        Assert.False(parsed.ContainsKey(GetSha256("item-1")));
        Assert.True(parsed.ContainsKey(GetSha256("item-4")));
    }

    [Fact]
    public async Task Read_NonSeekableStream_Works()
    {
        // 旧实现依赖 Length / Position，只能读可寻址流；新实现应当支持任意流。
        var payload = new MemoryStream();
        await HashToTimesReader.WriteAsync(new HashToTimes { [Hello] = 7, [World] = 8 }, payload);
        payload.Position = 0;

        var parsed = await HashToTimesReader.ReadAsync(new NonSeekableStream(payload));

        Assert.Equal(7, parsed[Hello]);
        Assert.Equal(8, parsed[World]);
    }

    [Fact]
    public async Task Write_DoesNotLeakPlaintext()
    {
        // 隐私性：文件里只有摘要与整数，不包含任何 Hint 明文。
        const string secret = "super-secret-hint";

        var stream = new MemoryStream();
        await HashToTimesReader.WriteAsync(new HashToTimes { [GetSha256(secret)] = 1 }, stream);

        Assert.DoesNotContain(
            Encoding.UTF8.GetBytes(secret),
            stream.ToArray());
    }

    private static SHA256Store GetSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new SHA256Store(SHA256.HashData(bytes));
    }

    /// <summary>把可寻址流包装成不可寻址流，用于验证顺序读取。</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
