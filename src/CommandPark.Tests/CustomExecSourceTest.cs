using System.Diagnostics;
using System.Text.Json.Nodes;
using CommandPark.Sources;
using JetBrains.Annotations;

namespace CommandPark.Tests;

[TestSubject(typeof(CustomExecSource))]
public class CustomExecSourceTest : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CommandPark.Tests",
        Guid.NewGuid().ToString("N"));

    public CustomExecSourceTest()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_WithoutPrefix_FindsCommandAndResolvesIcon()
    {
        CreateCommand(
            """
            {
              "name": "Deploy",
              "description": "Deploy the project",
              "icon": "deploy.png",
              "execFile": "deploy.exe",
              "args": ["--fast"],
              "env": {"MODE": "test"}
            }
            """);

        var source = new CustomExecSource(_root) { Paths = ["commands"] };
        var hint = Assert.Single(await source.SearchAsync("deploy").ToListAsync());

        Assert.Equal("customExec:command", hint.Id);
        Assert.Equal("Deploy", hint.Name);
        Assert.Equal("Deploy the project", hint.Description);
        Assert.Equal(1f, hint.Similarity);

        var icon = Assert.IsType<LocalImageHintIcon>(hint.Icon);
        Assert.Equal(Path.Combine(_root, "commands", "deploy.png"), icon.Path);
    }

    [Theory]
    [InlineData(">deploy")]
    [InlineData("cus:deploy")]
    [InlineData("cpc:deploy")]
    public async Task SearchAsync_WithPrefix_StripsPrefixAndBoostsResult(string query)
    {
        CreateCommand("""{"name":"Deploy","execFile":"deploy.exe"}""");
        var source = new CustomExecSource(_root) { Paths = ["commands"] };

        var plain = Assert.Single(await source.SearchAsync("deploy").ToListAsync());
        var prefixed = Assert.Single(await source.SearchAsync(query).ToListAsync());

        Assert.Equal("Deploy", prefixed.Name);
        Assert.Equal(plain.Similarity + 1f, prefixed.Similarity);
    }

    [Fact]
    public async Task SearchAsync_WithPrefixOnly_ListsEveryCommand()
    {
        CreateCommand("""{"name":"First","execFile":"first.exe"}""", fileName: "first.cpc");
        CreateCommand("""{"name":"Second","execFile":"second.exe"}""", fileName: "second.cpc");
        var source = new CustomExecSource(_root) { Paths = ["commands"] };

        var hints = await source.SearchAsync(">").ToListAsync();

        Assert.Equal(["First", "Second"], hints.Select(hint => hint.Name));
    }

    [Theory]
    [InlineData("cus deploy")]
    [InlineData("custom:deploy")]
    public async Task SearchAsync_RejectsPrefixesWithoutColon(string query)
    {
        CreateCommand("""{"name":"Deploy","execFile":"deploy.exe"}""");
        var source = new CustomExecSource(_root)
        {
            Paths = ["commands"],
            PrefixMode = true,
        };

        Assert.Empty(await source.SearchAsync(query).ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_WithPrefixModeAndNoPrefix_ReturnsNothing()
    {
        CreateCommand("""{"name":"Deploy","execFile":"deploy.exe"}""");
        var source = new CustomExecSource(_root)
        {
            Paths = ["commands"],
            PrefixMode = true,
        };

        Assert.Empty(await source.SearchAsync("deploy").ToListAsync());
        Assert.Single(await source.SearchAsync(">deploy").ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_MatchesDescriptionAndExecutableName()
    {
        CreateCommand(
            """
            {
              "name": "Release",
              "description": "Publish the production build",
              "execFile": "pack-tool.exe"
            }
            """,
            fileName: "release-tool.cpc");
        var source = new CustomExecSource(_root) { Paths = ["commands"] };

        Assert.Single(await source.SearchAsync("production").ToListAsync());
        Assert.Single(await source.SearchAsync("pack-tool.exe").ToListAsync());
        Assert.Single(await source.SearchAsync("release-tool").ToListAsync());
        Assert.Empty(await source.SearchAsync("unrelated").ToListAsync());
    }

    [Fact]
    public async Task SearchAsync_IgnoresMalformedFilesAndMissingDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "commands"));
        File.WriteAllText(Path.Combine(_root, "commands", "broken.cpc"), "{ not json");
        CreateCommand("""{"name":"Valid","execFile":"valid.exe"}""", fileName: "valid.cpc");

        var source = new CustomExecSource(_root)
        {
            Paths = ["missing", "commands"],
        };

        var hint = Assert.Single(await source.SearchAsync("valid").ToListAsync());

        Assert.Equal("Valid", hint.Name);
    }

    [Fact]
    public async Task SearchAsync_SupportsCommentsTrailingCommasAndCaseInsensitiveProperties()
    {
        CreateCommand(
            """
            {
              // .cpc 允许注释与尾随逗号，方便手工编辑。
              "Name": "Commented",
              "ExecFile": "tool.exe",
            }
            """);

        var source = new CustomExecSource(_root) { Paths = ["commands"] };

        var hint = Assert.Single(await source.SearchAsync("commented").ToListAsync());

        Assert.Equal("Commented", hint.Name);
    }

    [Fact]
    public async Task SearchAsync_WithoutNameOrDescription_UsesLocalizedFallbacks()
    {
        CreateCommand("""{"execFile":"my-tool.exe"}""");
        var source = new CustomExecSource(_root) { Paths = ["commands"] };

        var hint = Assert.Single(await source.SearchAsync("my-tool").ToListAsync());

        Assert.Equal("自定义命令 my-tool", hint.Name);
        Assert.Equal("自定义命令", hint.Description);
    }

    [Fact]
    public async Task Run_StartsResolvedExecutableWithArgumentsEnvironmentAndWorkingDirectory()
    {
        CreateCommand(
            """
            {
              "name": "Deploy",
              "execFile": "bin/deploy.exe",
              "args": ["--fast", "value with space"],
              "env": {"MODE": "test"}
            }
            """);

        ProcessStartInfo? captured = null;
        var source = new CustomExecSource(_root)
        {
            Paths = ["commands"],
            ProcessStarter = info => captured = info,
        };

        var hint = Assert.Single(await source.SearchAsync("deploy").ToListAsync());
        await hint.Run();

        Assert.NotNull(captured);
        Assert.False(captured.UseShellExecute);
        Assert.Equal(Path.Combine(_root, "commands", "bin", "deploy.exe"), captured.FileName);
        Assert.Equal(Path.Combine(_root, "commands"), captured.WorkingDirectory);
        Assert.Equal(["--fast", "value with space"], captured.ArgumentList.ToArray());
        Assert.Equal("test", captured.Environment["MODE"]);
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_ThrowsInsteadOfSwallowingCancellation()
    {
        CreateCommand("""{"name":"Deploy","execFile":"deploy.exe"}""");
        var source = new CustomExecSource(_root) { Paths = ["commands"] };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.SearchAsync("deploy", cts.Token).ToListAsync());
    }

    [Fact]
    public void CreateStartInfo_ForBatchFile_UsesCommandInterpreter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var script = Path.Combine(_root, "commands", "deploy.cmd");

        var startInfo = CustomExecSource.CreateStartInfo(
            script,
            ["first", "value with space"],
            new Dictionary<string, string>(),
            Path.GetDirectoryName(script)!);

        Assert.Equal(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", startInfo.FileName);
        Assert.Equal(["/d", "/c", script, "first", "value with space"], startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void ReadConfig_AppliesPathsAndPrefixMode()
    {
        var source = new CustomExecSource(_root);
        var json = JsonNode.Parse(
            """
            {
              "sources": {
                "customExec": {
                  "paths": ["commands", "commands", "other"],
                  "prefixMode": true
                }
              }
            }
            """)!;

        Assert.True(source.ReadConfig(json));
        Assert.Equal(["commands", "other"], source.Paths);
        Assert.True(source.PrefixMode);
    }

    [Fact]
    public void ReadConfig_AcceptsLegacyCustomSection()
    {
        var source = new CustomExecSource(_root);
        var json = JsonNode.Parse("""{"sources":{"custom":{"paths":["legacy"]}}}""")!;

        Assert.True(source.ReadConfig(json));
        Assert.Equal(["legacy"], source.Paths);
    }

    [Fact]
    public void ReadConfig_WithoutSection_ReturnsFalse()
    {
        var source = new CustomExecSource(_root);

        Assert.False(source.ReadConfig(JsonNode.Parse("""{"sources":{}}""")!));
    }

    [Fact]
    public void Factory_RegistersCustomExecSourceWithConfigDirectory()
    {
        File.WriteAllText(
            Path.Combine(_root, ConfigFile.DefaultFileName),
            """{"sources":{"customExec":{"paths":["commands"]}}}""");

        var provider = ConfigurableHintProviderFactory.Create(_root);

        var source = Assert.Single(provider.Sources.OfType<CustomExecSource>());
        Assert.Equal(["commands"], source.Paths);
    }

    private string CreateCommand(
        string json,
        string fileName = "command.cpc",
        string directoryName = "commands")
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, json);
        return path;
    }
}