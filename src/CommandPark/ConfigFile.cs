using System.Text.Json;
using System.Text.Json.Nodes;
using CommandPark.Strings;

namespace CommandPark;

/// <summary>
/// 读取 User Roaming AppData 中的 JSON 配置。
/// <para>
/// 默认文件名为 <c>commandpark.json</c>，为了兼容常见命名也接受 <c>config.json</c>。
/// 如果程序目录本身带有 <c>commandpark.json</c> 或 <c>config.json</c>，则视为便携配置：
/// 程序目录配置优先，并且完全忽略用户配置目录中的文件。
/// </para>
/// </summary>
public static class ConfigFile
{
    public const string DefaultFileName = "commandpark.json";
    public const string VendorDirectoryName = "shRabbit";
    public const string AppDirectoryName = "CommandPark";

    private static readonly string[] CandidateFileNames = [DefaultFileName, "config.json"];

    /// <summary>用户配置目录：<c>%APPDATA%\shRabbit\CommandPark</c>。</summary>
    public static string UserDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrWhiteSpace(appData)
                ? Path.Combine(AppContext.BaseDirectory, VendorDirectoryName, AppDirectoryName)
                : Path.Combine(appData, VendorDirectoryName, AppDirectoryName);
        }
    }

    /// <summary>用户配置文件的默认完整路径。</summary>
    public static string UserConfigPath => Path.Combine(UserDirectory, DefaultFileName);

    /// <summary>
    /// 查找实际使用的配置文件。
    /// <para>指定 <paramref name="baseDirectory"/> 时只查该目录，供测试或便携场景使用；
    /// 不指定时先查程序目录，再查 <see cref="UserDirectory"/>。</para>
    /// </summary>
    public static string? FindPath(string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return FindInDirectory(baseDirectory);
        }

        // 程序目录里的任意配置文件都表示“便携模式”，此时用户配置必须完全忽略。
        return FindInDirectory(AppContext.BaseDirectory) ?? FindInDirectory(UserDirectory);
    }

    /// <summary>测试用：显式指定程序目录与用户目录，保持与运行时相同的优先级。</summary>
    internal static string? FindPathForDirectories(string appDirectory, string? userDirectory) =>
        FindInDirectory(appDirectory) ??
        (string.IsNullOrWhiteSpace(userDirectory) ? null : FindInDirectory(userDirectory));

    private static string? FindInDirectory(string directory)
    {
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
            throw new JsonException(Localization.Get("config.invalidRoot"));
        }

        return root;
    }
}
