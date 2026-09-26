using System.Text.Json.Nodes;
using CommandPark.Sources;

namespace CommandPark.Tests;

public class ConfigurationSchemaTest
{
    [Fact]
    public void EnvProgramSource_ReadConfig_EmptyObjectEnablesSource()
    {
        var source = new EnvProgramSource();

        Assert.True(source.ReadConfig(JsonNode.Parse("""{"sources":{"envProgram":{}}}""")!));
        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }

    [Fact]
    public void EverythingSource_ReadConfig_AppliesAndClampsSettings()
    {
        var source = new EverythingSource();

        Assert.True(source.ReadConfig(
            JsonNode.Parse("""{"sources":{"everything":{"prefixMode":false,"batchSize":0}}}""")!));
        Assert.False(source.PrefixMode);
        Assert.Equal(1u, source.BatchSize);

        Assert.True(source.ReadConfig(
            JsonNode.Parse("""{"sources":{"everything":{"batchSize":5000}}}""")!));
        Assert.Equal(1000u, source.BatchSize);
    }

    [Fact]
    public void DefaultConfig_EveryPropertyExistsInSchema()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configPath = Path.Combine(repositoryRoot, "src", "CommandPark.Ui", "commandpark.json");
        var schemaPath = Path.Combine(repositoryRoot, "src", "CommandPark.Ui", "commandpark.schema.json");

        var config = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(configPath)));
        var schema = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(schemaPath)));
        var schemaTopProperties = Assert.IsType<JsonObject>(schema["properties"]);
        var sources = Assert.IsType<JsonObject>(config["sources"]);
        var schemaSources = Assert.IsType<JsonObject>(schema["properties"]?["sources"]?["properties"]);
        var definitions = Assert.IsType<JsonObject>(schema["$defs"]);

        foreach (var (propertyName, _) in config)
        {
            Assert.True(
                schemaTopProperties.ContainsKey(propertyName),
                $"Schema 顶层缺少配置项：{propertyName}");
        }

        foreach (var (sourceName, sourceNode) in sources)
        {
            Assert.True(schemaSources.ContainsKey(sourceName), $"Schema 缺少源：{sourceName}");

            var sourceConfig = Assert.IsType<JsonObject>(sourceNode);
            var definition = Assert.IsType<JsonObject>(definitions[sourceName]);
            var schemaProperties = definition["properties"] as JsonObject ?? new JsonObject();
            var additionalPropertiesAllowed = definition["additionalProperties"]?.GetValue<bool>() ?? true;

            Assert.False(additionalPropertiesAllowed, $"Schema 的 {sourceName} 应禁止未知属性");

            foreach (var (propertyName, _) in sourceConfig)
            {
                Assert.True(
                    schemaProperties.ContainsKey(propertyName),
                    $"Schema 的 {sourceName} 缺少配置项：{propertyName}");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CommandPark.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 CommandPark 仓库根目录。");
    }
}