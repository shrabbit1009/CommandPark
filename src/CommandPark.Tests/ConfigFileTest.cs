using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(ConfigFile))]
public class ConfigFileTest
{
    [Fact]
    public void FindPath_PrefersCommandparkJson()
    {
        var directory = CreateTempDirectory();
        var configJson = Path.Combine(directory, "config.json");
        var commandParkJson = Path.Combine(directory, ConfigFile.DefaultFileName);

        File.WriteAllText(configJson, "{}");
        Assert.Equal(configJson, ConfigFile.FindPath(directory));

        File.WriteAllText(commandParkJson, "{}");
        Assert.Equal(commandParkJson, ConfigFile.FindPath(directory));
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsNull()
    {
        Assert.Null(ConfigFile.Load(CreateTempDirectory()));
    }

    [Fact]
    public void Load_ParsesJsonRoot()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(directory, ConfigFile.DefaultFileName),
            """{"sources":{"installedApplication":{"prefixMode":true}}}""");

        var root = ConfigFile.Load(directory);

        Assert.NotNull(root);
        Assert.True(root["sources"]?["installedApplication"]?["prefixMode"]?.GetValue<bool>());
    }

    [Fact]
    public void Load_WithInvalidJson_ThrowsJsonException()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, ConfigFile.DefaultFileName), "{ not json");

        Assert.ThrowsAny<JsonException>(() => ConfigFile.Load(directory));
    }

    [Fact]
    public void FindPathForDirectories_PrefersPortableConfigAndIgnoresUserConfig()
    {
        var appDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var portable = Path.Combine(appDirectory, ConfigFile.DefaultFileName);
        File.WriteAllText(portable, "{}");
        File.WriteAllText(Path.Combine(userDirectory, ConfigFile.DefaultFileName), "{}");

        Assert.Equal(portable, ConfigFile.FindPathForDirectories(appDirectory, userDirectory));
    }

    [Fact]
    public void FindPathForDirectories_UsesUserConfigWhenPortableConfigIsMissing()
    {
        var appDirectory = CreateTempDirectory();
        var userDirectory = CreateTempDirectory();
        var userConfig = Path.Combine(userDirectory, "config.json");
        File.WriteAllText(userConfig, "{}");

        Assert.Equal(userConfig, ConfigFile.FindPathForDirectories(appDirectory, userDirectory));
    }

    [Fact]
    public void UserDirectory_IsUnderRoamingAppData()
    {
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ConfigFile.VendorDirectoryName,
                ConfigFile.AppDirectoryName),
            ConfigFile.UserDirectory);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CommandPark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

[TestSubject(typeof(ConfigurableHintProviderFactory))]
public class ConfigurableHintProviderFactoryTest
{
    [Fact]
    public void Create_WhenConfigFileMissing_RegistersAllSources()
    {
        var directory = CreateTempDirectory();
        var first = new ConfigurableTestSource("first");
        var second = new ConfigurableTestSource("second");

        var provider = ConfigurableHintProviderFactory.Create(directory, [first, second]);

        Assert.Equal([first, second], provider.Sources);
    }

    [Fact]
    public void Create_WhenConfigFileExists_RegistersOnlyEnabledReaders()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(directory, ConfigFile.DefaultFileName),
            """{"sources":{"first":{}}}""");

        var enabled = new ConfigurableTestSource("first");
        var disabled = new ConfigurableTestSource("second");

        var provider = ConfigurableHintProviderFactory.Create(directory, [enabled, disabled]);

        Assert.Equal([enabled], provider.Sources);
    }

    [Fact]
    public void InstalledApplicationSource_ReadConfig_AppliesSettings()
    {
        var source = new InstalledApplicationSource();
        var json = JsonNode.Parse(
            """
            {
              "sources": {
                "installedApplication": {
                  "excludes": ["Uninstall"],
                  "prefixMode": true,
                  "skipNoise": false,
                  "pinyinMode": false,
                  "maxLength": 12
                }
              }
            }
            """)!;

        Assert.True(source.ReadConfig(json));
        Assert.True(source.PrefixMode);
        Assert.False(source.SkipNoise);
        Assert.False(source.PinyinMode);
        Assert.Equal(12, source.MaxNameLength);
        Assert.Contains("Uninstall", source.Excludes);
    }

    [Fact]
    public void InstalledApplicationSource_ReadConfig_WithoutSection_DisablesSource()
    {
        var source = new InstalledApplicationSource();
        var json = JsonNode.Parse("""{"sources":{}}""")!;

        Assert.False(source.ReadConfig(json));
    }


    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CommandPark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class ConfigurableTestSource(string key) : IHintSource, IConfigReader
    {
        public bool IsEnabled { get; private set; }

        public bool ReadConfig(JsonNode json)
        {
            IsEnabled = json["sources"]?[key] is not null;
            return IsEnabled;
        }

        public async IAsyncEnumerable<IHint> SearchAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
