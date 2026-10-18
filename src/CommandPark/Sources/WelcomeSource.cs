using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommandPark.Strings;

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
                Localization.Get("sources.welcome.changeConfig.name"),
                Localization.Get("sources.welcome.changeConfig.description"),
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
                Localization.Get("sources.welcome.repo.name"),
                Localization.Get("sources.welcome.repo.description"),
                ct =>
                {
                    Process.Start("explorer.exe", "https://github.com/shrabbit1009/CommandPark");
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
                Localization.Get("sources.welcome.official.name"),
                Localization.Get("sources.welcome.official.description"),
                ct =>
                {
                    Process.Start("explorer.exe", "https://home.shrabbit.com/products/commandpark");
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
                Localization.Get("sources.welcome.backgroundUnavailable"),
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
        ShowRepo = section["showRepo"]?.GetValue<bool>() ?? true;
        ShowOfficial = section["showOfficial"]?.GetValue<bool>() ?? true;

        return true;
    }
}