using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(EnvProgramSource))]
public class EnvProgramSourceTest
{
    [Fact]
    public async Task SearchAsync_ExactProgramName_RanksFirst()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hints = await new EnvProgramSource().SearchAsync("cmd", cts.Token).ToListAsync();

        Assert.NotEmpty(hints);
        Assert.Equal("系统程序 cmd", hints[0].Name);
        Assert.Equal(1f, hints[0].Similarity);

        var jcmd = hints.FindIndex(hint => hint.Name == "系统程序 jcmd");
        if (jcmd >= 0)
        {
            Assert.True(jcmd > 0, "jcmd 不能排在完全匹配的 cmd 前面");
        }
    }
}
