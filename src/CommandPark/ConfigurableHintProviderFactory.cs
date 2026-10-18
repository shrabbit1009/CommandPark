using CommandPark.Sources;

namespace CommandPark;

/// <summary>
/// 按程序目录中的 JSON 配置创建默认 Hint Provider。
/// <para>
/// 未提供配置文件时沿用现有默认行为，注册全部内置源。
/// 配置文件存在时，每个实现 <see cref="IConfigReader"/> 的源必须先返回 true 才会被注册。
/// </para>
/// </summary>
public static class ConfigurableHintProviderFactory
{
    /// <summary>使用程序目录中的配置创建默认 Provider。</summary>
    /// <param name="configDirectory">配置目录；为空时使用 <see cref="AppContext.BaseDirectory"/>。</param>
    /// <param name="sources">可注入的源列表；为空时使用内置源。</param>
    /// <param name="weightProvider">可选的附加排序权重源（例如点击次数）；会挂到返回的 Provider 上。</param>
    public static HintProvider Create(
        string? configDirectory = null,
        IEnumerable<IHintSource>? sources = null,
        IHintWeightProvider? weightProvider = null)
    {
        var config = ConfigFile.Load(configDirectory);
        var provider = new HintProvider
        {
            WeightProvider = weightProvider,
        };

        foreach (var source in sources ?? CreateDefaultSources(configDirectory))
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
