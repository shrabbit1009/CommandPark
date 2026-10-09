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
            [GetSha256("Hello")] = 1,
            [GetSha256("World")] = 2
        };

        var s = new MemoryStream();
        await HashToTimesReader.WriteAsync(htt, s);
        s.Position = 0;
        var parsed = await HashToTimesReader.ReadAsync(s);
        
        Assert.Equal(htt[GetSha256("Hello")], parsed[GetSha256("Hello")]);
        Assert.Equal(htt[GetSha256("World")], parsed[GetSha256("World")]);
    }

    [Fact]
    public async Task StressTest()
    {
        var timer = new Stopwatch();
        for (var i = 0; i < 10000; i++)
        {
            if (i == 1)
                timer.Start();
            await FullTest();
        }

        timer.Stop();

        var ms      = timer.ElapsedMilliseconds;
        var nsPerOp = timer.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency) / 9999;
        _testOutputHelper.WriteLine($"总用时 {ms}ms, 平均每次 {nsPerOp:f2} 纳秒");
    }

    private SHA256Store GetSha256(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hashed = SHA256.HashData(bytes);
        return new SHA256Store(hashed);
    }
}