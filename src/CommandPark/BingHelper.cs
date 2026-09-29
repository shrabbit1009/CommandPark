using System.Text.Json.Nodes;
using RestSharp;

namespace CommandPark;

public class BingHelper : ISearchEngineHelper
{
    public BingHelper()
    {

    }

    public BingHelper(IRestClient client)
    {
        RestClient = client;
    }

    public IRestClient RestClient { get; set; } = new RestClient();

    /// <summary>
    /// 关键字建议的最小 URL
    /// </summary>
    /// <remarks>
    /// {0} = 关键字
    /// </remarks>
    private string SuggestUrl { get; set; } = "https://bing.com/AS/Suggestions?pt=page.serp&qry={0}&csr=1&pths=1&cvid=%00";

    /// <summary>
    /// 搜索 URL
    /// </summary>
    /// <remarks>
    /// {0} = 关键字
    /// </remarks>
    private string SearchUrl { get; set; }  = "https://bing.com/search?q={0}";

    /// <summary>
    /// 获取建议的关键词
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <param name="keyword">关键字</param>
    /// <returns>建议的关键词数组</returns>
    public async Task<string[]> GetSuggestedItems(string keyword, CancellationToken cancellationToken = default)
    {
        var list = new List<string>(12);

        var req = new RestRequest(string.Format(SuggestUrl, keyword));
        var rsp = await RestClient.ExecuteAsync(req, cancellationToken);

        var json = JsonNode.Parse(rsp.Content!)!["s"]!;

        // 只关心 q
        foreach (var item in json.AsArray())
            list.Add(Trim(item!["q"]!.GetValue<string>()));

        return list.ToArray();
    }

    /// <summary>
    /// 构建 Bing 跳转搜索。
    /// </summary>
    /// <param name="keyword"></param>
    /// <returns></returns>
    public string BuildSearchUrl(string keyword)
    {
        return string.Format(SearchUrl, keyword);
    }

    private string Trim(string input) => input.Replace("\ue000",string.Empty)
                                              .Replace("\ue001", string.Empty);
}
