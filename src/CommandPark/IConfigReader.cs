using System.Text.Json.Nodes;

namespace CommandPark;

public interface IConfigReader
{
    /// <summary>
    /// 根据配置文件调整配置
    /// </summary>
    /// <param name="json">JSON</param>
    /// <returns>返回 true 代表该源会被运行；false 则不运行。</returns>
    public bool ReadConfig(JsonNode json);
}