using CommandPark.Sources;

namespace CommandPark;

/// <summary>
/// 按程序目录或用户 Roaming AppData 中的 JSON 配置创建默认 Hint Provider。
/// <para>
/// 未提供配置文件时沿用现有默认行为，注册全部内置源。
/// 配置文件存在时，每个实现 <see cref="IConfigReader"/> 的源必须先返回 true 才会被注册。
/// </para>
/// </summary>
public static class ConfigurableHintProviderFactory
{
    /// <summary>使用实际生效的配置文件创建默认 Provider。</summary>
    /// <param name="configDirectory">显式配置目录；为空时按程序目录 → 用户配置目录的顺序查找。</param>
    /// <param name="sources">可注入的源列表；为空时使用内置源。</param>
    /// <param name="weightProvider">可选的附加排序权重源（例如点击次数）；会挂到返回的 Provider 上。</param>
    public static HintProvider Create(
        string? configDirectory = null,
        IEnumerable<IHintSource>? sources = null,
        IHintWeightProvider? weightProvider = null)
    {
        var configPath = ConfigFile.FindPath(configDirectory);
        var config = configPath is null ? null : ConfigFile.Load(configDirectory);

        // customExec 等源里的相对路径应相对于真正使用的配置文件，而不是固定相对于程序目录。
        var effectiveConfigDirectory = configPath is not null
            ? Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory
            : configDirectory ?? AppContext.BaseDirectory;

        var provider = new HintProvider
        {
            WeightProvider = weightProvider,
        };

        foreach (var source in sources ?? CreateDefaultSources(effectiveConfigDirectory))
        {
            if (config is not null && source is IConfigReader reader && !reader.ReadConfig(config))
            {
                continue;
            }

            provider.Sources.Add(source);
        }

        return provider;
    }

    private static IEnumerable<IHintSource> CreateDefaultSources(string? configDirectory)
    {
        yield return new EnvProgramSource();
        yield return new EverythingSource();
        yield return new InstalledApplicationSource();
        yield return new CustomExecSource(configDirectory);
        yield return new RunSource();
        yield return new WelcomeSource();
        yield return new CalcSource();
        yield return new BingSearchSource();
    }
}
