using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(BingHelper))]
public class BingHelperTest
{
    [Fact]
    public void BuildSearchUrl_EscapesKeyword()
    {
        var helper = new BingHelper();

        var url = helper.BuildSearchUrl("hello world 中文");

        Assert.Equal("https://bing.com/search?q=hello%20world%20%E4%B8%AD%E6%96%87", url);
    }
}