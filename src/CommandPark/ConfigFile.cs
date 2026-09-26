using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandPark;

/// <summary>
/// 读取程序目录中的 JSON 配置。
/// <para>
/// 默认文件名为 <c>commandpark.json</c>，为了兼容常见命名也接受 <c>config.json</c>；
/// 文件不存在时返回 <see langword="null"/>，由调用方决定是否使用默认配置。
/// </para>
/// </summary>
public static class ConfigFile
{
    public const string DefaultFileName = "commandpark.json";

    private static readonly string[] CandidateFileNames = [DefaultFileName, "config.json"];

    /// <summary>查找程序目录中实际存在的配置文件。</summary>
    /// <param name="baseDirectory">程序目录；为空时使用 <see cref="AppContext.BaseDirectory"/>。</param>
    public static string? FindPath(string? baseDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        foreach (var fileName in CandidateFileNames)
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 读取并解析配置文件。文件不存在时返回 <see langword="null"/>。
    /// JSON 格式错误或读取失败时会抛出异常，由调用方决定如何提示。
    /// </summary>
    public static JsonNode? Load(string? baseDirectory = null)
    {
        var path = FindPath(baseDirectory);
        if (path is null)
        {
            return null;
        }

        var json = File.ReadAllText(path);
        var root = JsonNode.Parse(
            json,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

        if (root is not JsonObject)
        {
            throw new JsonException("配置文件根节点必须是 JSON 对象。");
        }

        return root;
    }
}
