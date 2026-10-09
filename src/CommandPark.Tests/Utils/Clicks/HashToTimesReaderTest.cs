using System.Security.Cryptography;
using System.Text;
using CommandPark.Utils.Clicks;
using JetBrains.Annotations;

namespace CommandPark.Tests.Utils.Clicks;

[TestSubject(typeof(HashToTimesReader))]
public class HashToTimesReaderTest
{

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

    private SHA256Store GetSha256(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hashed = SHA256.HashData(bytes);
        return new SHA256Store(hashed);
    }
}