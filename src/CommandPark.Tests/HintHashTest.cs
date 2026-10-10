using CommandPark.Ai;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(HintHash))]
public class HintHashTest
{
    private static Hint CreateHint(string id = "run:notepad", string name = "记事本", string description = "打开记事本") =>
        new(id, name, description);

    [Fact]
    public void Short_DefaultsToEightLowercaseHexChars()
    {
        var hash = HintHash.Short(CreateHint());

        Assert.Equal(HintHash.DefaultLength, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void Short_IsStableForSameHint()
    {
        var hint = CreateHint();

        Assert.Equal(HintHash.Short(hint), HintHash.Short(hint));
    }

    [Fact]
    public void Short_DiffersWhenFieldBoundaryMoves()
    {
        // 字段分隔符保证 ("ab","c") 与 ("a","bc") 不会算出同一个短哈希。
        var first = new Hint("ab", "c", "d");
        var second = new Hint("a", "bc", "d");

        Assert.NotEqual(HintHash.Short(first), HintHash.Short(second));
    }

    [Fact]
    public void Short_RespectsRequestedLength()
    {
        var hint = CreateHint();

        Assert.Equal(4, HintHash.Short(hint, 4).Length);
        Assert.Equal(32, HintHash.Short(hint, 32).Length);
    }

    [Fact]
    public void Short_ClampsOutOfRangeLength()
    {
        var hint = CreateHint();

        Assert.Equal(HintHash.MinLength, HintHash.Short(hint, -10).Length);
        Assert.Equal(HintHash.MaxLength, HintHash.Short(hint, 9999).Length);
    }
}
