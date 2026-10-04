using CommandPark.Utils;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(ComputeSimilarityHelper))]
public class ComputeSimilarityHelperTest
{
    [Fact]
    public void ComputeSimilarity_ExactMatch_ReturnsMaximumScore()
    {
        Assert.Equal(1f, ComputeSimilarityHelper.ComputeSimilarity("cmd", "cmd"));
        Assert.Equal(1f, ComputeSimilarityHelper.ComputeSimilarity("CMD", "cmd"));
    }

    [Fact]
    public void ComputeSimilarity_ExactMatch_OutranksLongerNameContainingQuery()
    {
        var exact = ComputeSimilarityHelper.ComputeSimilarity("cmd", "cmd");
        var embedded = ComputeSimilarityHelper.ComputeSimilarity("cmd", "jcmd");

        Assert.Equal(1f, exact);
        Assert.True(exact > embedded);
    }

    [Fact]
    public void ComputeSimilarity_PrefixMatch_OutranksEmbeddedMatch()
    {
        var cmdExe = ComputeSimilarityHelper.ComputeSimilarity("cmd", "cmd.exe");
        var jcmdExe = ComputeSimilarityHelper.ComputeSimilarity("cmd", "jcmd.exe");

        Assert.True(cmdExe > jcmdExe);
    }

    [Fact]
    public void ComputeSimilarity_MatchQuality_IsStrictlyLayered()
    {
        var exact = ComputeSimilarityHelper.ComputeSimilarity("code", "code");
        var prefix = ComputeSimilarityHelper.ComputeSimilarity("code", "code.exe");
        var contains = ComputeSimilarityHelper.ComputeSimilarity("code", "my-code-editor");
        var fuzzy = ComputeSimilarityHelper.ComputeSimilarity("code", "c_o_d_e_editor");

        Assert.True(exact > prefix);
        Assert.True(prefix > contains);
        Assert.True(contains > fuzzy);
    }

    [Fact]
    public void ComputeSimilarity_WordBoundaryMatch_OutranksMatchInsideWord()
    {
        var wordBoundary = ComputeSimilarityHelper.ComputeSimilarity("cmd", "my-cmd");
        var insideWord = ComputeSimilarityHelper.ComputeSimilarity("cmd", "mycmd");

        Assert.True(wordBoundary > insideWord);
    }

    [Theory]
    [InlineData("", "cmd")]
    [InlineData("   ", "cmd")]
    [InlineData("cmd", "")]
    [InlineData("cmd", "   ")]
    public void ComputeSimilarity_WithEmptyInput_ReturnsZero(string input, string target)
    {
        Assert.Equal(0f, ComputeSimilarityHelper.ComputeSimilarity(input, target));
    }

    [Fact]
    public void ComputeSimilarity_WithCompletelyDifferentText_ReturnsZero()
    {
        Assert.Equal(0f, ComputeSimilarityHelper.ComputeSimilarity("abc", "xyz"));
    }

    [Fact]
    public void ComputeSimilarity_FuzzyMatch_IsBelowContainsMatch()
    {
        var contains = ComputeSimilarityHelper.ComputeSimilarity("cmd", "xcmd");
        var fuzzy = ComputeSimilarityHelper.ComputeSimilarity("cmd", "c-m-d");

        Assert.InRange(fuzzy, 0f, 1f);
        Assert.True(contains > fuzzy);
    }
}
