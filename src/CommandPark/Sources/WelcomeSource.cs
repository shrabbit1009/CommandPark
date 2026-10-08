using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace CommandPark.Sources;

/// <summary>
/// 首屏欢迎页。仅在空查询时显示；用户开始输入后自动隐藏。
/// </summary>
public class WelcomeSource : IHintSource, IConfigReader, IHomeDisplayable
{
    public bool ShowEditConfig { get; set; } = true;
    public bool ShowRepo { get; set; } = true;
    public bool ShowOfficial { get; set; } = true;

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 首屏源只在空查询时运行。
        if (!string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        if (ShowEditConfig)
        {
            yield return new Hint(
                "welcome:changeConfigFile",
                "修改 CommandPark 配置文件",
                "使用默认文本编辑器打开配置文件。",
                ct =>
                {
                    Process.Start("explorer.exe", Path.Join(AppDomain.CurrentDomain.BaseDirectory, "commandpark.json"));
                    return Task.CompletedTask;
                })
            {
                Icon = new LucideIdHintIcon("Cog"),
            };
        }

        if (ShowRepo)
        {
            yield return new Hint(
                "welcome:repo",
                "让 CommandPark 更好！",
                "[默认 Web 浏览器] 进入本项目仓库，参与贡献或者提出你遇到的问题。",
                ct =>
                {
                    Process.Start("explorer.exe", "https://shrabbit.com:4000/gitlab/shrabbit/commandpark");
                    return Task.CompletedTask;
                })
            {
                Icon = new LucideIdHintIcon("ThumbsUp"),
            };
        }

        if (ShowOfficial)
        {
            yield return new Hint(
                "welcome:official",
                "shRabbit 官方网站",
                "[默认 Web 浏览器] 访问并了解最新内容。",
                ct =>
                {
                    Process.Start("explorer.exe", "https://shrabbit.com:4000/gitlab/shrabbit/commandpark");
                    return Task.CompletedTask;
                })
            {
                Icon = new LucideIdHintIcon("Globe"),
            };
        }

        yield return new Hint("welcome:version",
                              $"CommandPark {Assembly.GetAssembly(typeof(WelcomeSource))?.GetName().Version?.ToString(3)}",
                              "shRabbit",
                              _ => Task.CompletedTask)
        {
            Icon = new LucideIdHintIcon("GitCommitHorizontal"),
        };

        // 后台热键不可用（后台程序缺失 / 未配置快捷键 / 启动失败等）时，在最后给一条提示。
        var backgroundUnavailableReason = BackgroundStatus.UnavailableReason;
        if (!string.IsNullOrWhiteSpace(backgroundUnavailableReason))
        {
            yield return new Hint(
                "welcome:backgroundUnavailable",
                "全局热键不可用",
                backgroundUnavailableReason,
                _ => Task.CompletedTask)
            {
                Icon = new LucideIdHintIcon("KeyboardOff"),
            };
        }

        await Task.CompletedTask;
    }

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources ||
            sources["welcome"] is not JsonObject section)
        {
            return false;
        }

        ShowEditConfig = section["showEditConfig"]?.GetValue<bool>() ?? true;
        ShowRepo       = section["showRepo"]?.GetValue<bool>() ?? true;
        ShowOfficial   = section["showOfficial"]?.GetValue<bool>() ?? true;

        return true;
    }
}