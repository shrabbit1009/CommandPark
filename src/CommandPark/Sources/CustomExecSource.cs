using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CommandPark.Strings;
using CommandPark.Utils;
using NPinyin;

namespace CommandPark.Sources;

/// <summary>
/// 自定义命令源：扫描配置目录中的 <c>.cpc</c> 文件，并把它们转换成可执行的 Hint。
/// <para>
/// <c>.cpc</c> 是 JSON 文件，路径相对于该文件所在目录解析；命令通过 <c>&gt;</c>、
/// <c>cus:</c> 或 <c>cpc:</c> 前缀触发，也可以按配置直接参与普通搜索。
/// </para>
/// </summary>
public class CustomExecSource : IHintSource, IHintWithPrefix, IConfigReader
{
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = [">", "cus:", "cpc:"];

    private const float MinimumMatchScore = 0.30f;

    private const string ConfigSectionName = "customExec";
    private const string LegacyConfigSectionName = "custom";

    private static readonly EnumerationOptions SearchOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = FileAttributes.None,
    };

    private readonly string _baseDirectory;

    /// <summary>测试用：真正启动进程前先经过这里，默认直接调用 <see cref="Process.Start(ProcessStartInfo)"/>。</summary>
    internal Action<ProcessStartInfo> ProcessStarter { get; set; } = static info => Process.Start(info);

    public CustomExecSource() : this(AppContext.BaseDirectory)
    {
    }

    /// <param name="baseDirectory">配置文件中相对路径的基准目录；为空时使用程序目录。</param>
    public CustomExecSource(string? baseDirectory)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);
    }

    /// <summary>包含 <c>.cpc</c> 文件的目录。相对路径以 <see cref="_baseDirectory"/> 为基准。</summary>
    public string[] Paths { get; set; } = [];

    /// <summary>为 <see langword="true"/> 时只有带前缀才运行；否则带前缀只提升权重。</summary>
    public bool PrefixMode { get; set; }

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hasPrefix = HintPrefix.TryStrip(query, out var rawQuery, Prefixes);
        if (PrefixMode && !hasPrefix)
        {
            yield break;
        }

        var keyword = rawQuery.Trim();

        // 普通模式下空查询不扫描所有自定义命令；单独输入前缀时则列出全部命令。
        if (keyword.Length == 0 && !hasPrefix)
        {
            yield break;
        }

        var commands = await LoadCommandsAsync(cancellationToken).ConfigureAwait(false);
        var matches = new List<(CustomExecCommand Command, float Score)>();

        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var score = keyword.Length == 0 ? 1.0f : CalculateScore(command, keyword);
            if (score >= MinimumMatchScore)
            {
                matches.Add((command, score));
            }
        }

        var boost = hasPrefix ? 1.0f : 0.0f;

        foreach (var (command, score) in matches
                     .OrderByDescending(match => match.Score)
                     .ThenBy(match => match.Command.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(match => match.Command.ConfigPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new Hint(
                "customExec:command",
                command.Name,
                command.Description,
                score + boost,
                ct => OnRun(command, ct))
            {
                Icon = command.Icon,
            };
        }
    }

    private static float CalculateScore(CustomExecCommand command, string keyword)
    {
        var normalizedQuery = Normalize(keyword);
        if (normalizedQuery.Length == 0)
        {
            return 0f;
        }

        var directScore = ComputeSimilarityHelper.ComputeSimilarity(normalizedQuery, command.NormalizedName);
        var pinyinScore = ComputeSimilarityHelper.ComputeSimilarity(normalizedQuery, command.NormalizedPinyin);
        var initialsScore = ComputeSimilarityHelper.ComputeSimilarity(normalizedQuery, command.Initials);

        return Math.Max(directScore, Math.Max(pinyinScore, initialsScore));
    }

    private Task OnRun(CustomExecCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = CreateStartInfo(
            command.ExecFile,
            command.Args,
            command.Env,
            command.WorkingDirectory);

        ProcessStarter(startInfo);
        return Task.CompletedTask;
    }

    /// <summary>构造启动信息；拆成单独方法以便单元测试验证参数、环境和脚本分支。</summary>
    internal static ProcessStartInfo CreateStartInfo(
        string execFile,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        string workingDirectory)
    {
        var (fileName, preparedArgs) = PrepareExecutable(execFile, args);
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        foreach (var arg in preparedArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in env)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            startInfo.Environment[key] = Environment.ExpandEnvironmentVariables(value ?? string.Empty);
        }

        return startInfo;
    }

    /// <summary>
    /// 让批处理与 PowerShell 脚本也能直接执行。普通程序保持原路径，参数统一通过
    /// <see cref="ProcessStartInfo.ArgumentList"/> 传递，避免手工拼接引号。
    /// </summary>
    private static (string FileName, IReadOnlyList<string> Args) PrepareExecutable(
        string execFile,
        IReadOnlyList<string> args)
    {
        var extension = Path.GetExtension(execFile);

        if (OperatingSystem.IsWindows() && extension.ToLowerInvariant() is ".cmd" or ".bat")
        {
            var shellArgs = new List<string>(args.Count + 3)
            {
                "/d", "/c", execFile,
            };
            shellArgs.AddRange(args);
            return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", shellArgs);
        }

        if (OperatingSystem.IsWindows() && extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var shellArgs = new List<string>(args.Count + 5)
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", execFile,
            };
            shellArgs.AddRange(args);
            return ("powershell.exe", shellArgs);
        }

        return (execFile, args);
    }

    private async Task<List<CustomExecCommand>> LoadCommandsAsync(CancellationToken cancellationToken)
    {
        var commands = new List<CustomExecCommand>();

        foreach (var configPath in EnumerateConfigFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var command = await LoadCommandAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (command is not null)
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private static async Task<CustomExecCommand?> LoadCommandAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        CustomExecDto? dto;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
            dto = JsonSerializer.Deserialize(json, CustomExecJsonContext.Default.CustomExecDto);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableFileError(exception))
        {
            // 单个损坏或不可读的配置文件不能影响其它自定义命令。
            return null;
        }

        if (dto is null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        var execFile = ResolvePath(dto.ExecFile, directory);
        if (execFile is null)
        {
            return null;
        }

        var iconPath = ResolvePath(dto.Icon, directory);
        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? Localization.Get("sources.customExec.fallback.name")
            : dto.Name.Trim();
        var description = string.IsNullOrWhiteSpace(dto.Description)
            ? Localization.Get("sources.customExec.fallback.description")
            : dto.Description.Trim();
        var normalizedName = Normalize(name);

        return new CustomExecCommand(
            configPath,
            execFile,
            name,
            description,
            normalizedName,
            Normalize(Pinyin.GetPinyin(name)),
            BuildInitials(name),
            CleanArguments(dto.Args),
            CleanEnvironment(dto.Env),
            CreateIcon(dto.LucideIcon, iconPath, execFile));
    }

    private static IReadOnlyList<string> CleanArguments(string[]? args) =>
        args is null
            ? []
            : args.Where(arg => arg is not null).ToArray();

    private static IReadOnlyDictionary<string, string> CleanEnvironment(Dictionary<string, string>? env)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var result = new Dictionary<string, string>(comparer);
        if (env is null)
        {
            return result;
        }

        foreach (var (key, value) in env)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value ?? string.Empty;
            }
        }

        return result;
    }

    private static IHintIcon? CreateIcon(string? lucideIcon, string? iconPath, string execFile)
    {
        if (!string.IsNullOrWhiteSpace(lucideIcon))
        {
            return new LucideIdHintIcon(lucideIcon.Trim());
        }

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            return new LocalImageHintIcon(iconPath);
        }

        return IsExecutableIcon(Path.GetExtension(execFile))
            ? new ExecutableHintIcon(execFile)
            : new FileIconHintIcon(execFile);
    }

    private static string BuildInitials(string name)
    {
        var initials = new StringBuilder();
        var atWordStart = true;
        var previousWasLower = false;

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                atWordStart = true;
                previousWasLower = false;
                continue;
            }

            if (IsCjk(c))
            {
                var syllable = Pinyin.GetPinyin(c);
                if (syllable.Length > 0)
                {
                    initials.Append(char.ToLowerInvariant(syllable[0]));
                }

                atWordStart = true;
                previousWasLower = false;
                continue;
            }

            if (atWordStart || (char.IsUpper(c) && previousWasLower))
            {
                initials.Append(char.ToLowerInvariant(c));
            }

            previousWasLower = char.IsLower(c);
            atWordStart = false;
        }

        return initials.ToString();
    }

    private static bool IsCjk(char c) => c is (>= '\u4e00' and <= '\u9fff') or (>= '\u3400' and <= '\u4dbf');

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.' or '(' or ')' or '[' or ']' or ',' or '，' or '、' or '·')
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static bool IsExecutableIcon(string extension) => extension.ToLowerInvariant() is
        ".exe" or ".dll" or ".scr" or ".cpl" or ".ocx" or ".sys";

    private IEnumerable<string> EnumerateConfigFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredPath in Paths)
        {
            var directory = ResolvePath(configuredPath, _baseDirectory);
            if (directory is null || !Directory.Exists(directory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.cpc", SearchOptions)
                                   .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                   .ToArray();
            }
            catch (Exception exception) when (IsRecoverableFileError(exception))
            {
                // 目录可能刚好被删除、没有权限，或中途断开了网络驱动器。
                continue;
            }

            foreach (var file in files)
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(file);
                }
                catch (Exception exception) when (IsRecoverableFileError(exception))
                {
                    continue;
                }

                if (seen.Add(fullPath))
                {
                    yield return fullPath;
                }
            }
        }
    }

    /// <summary>把相对路径解析到基准目录；空路径或非法路径返回 <see langword="null"/>。</summary>
    private static string? ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            return Path.GetFullPath(
                Path.IsPathRooted(expanded)
                    ? expanded
                    : Path.Combine(baseDirectory, expanded));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsRecoverableFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or
            NotSupportedException or ArgumentException;

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources)
        {
            return false;
        }

        // customExec 是正式名称；兼容早期草稿里的 custom。
        var section = sources[ConfigSectionName] as JsonObject ??
                      sources[LegacyConfigSectionName] as JsonObject;
        if (section is null)
        {
            return false;
        }

        if (section["paths"] is JsonArray pathArray)
        {
            Paths = pathArray
                    .Select(node => node?.GetValue<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        PrefixMode = section["prefixMode"]?.GetValue<bool>() ?? PrefixMode;
        return true;
    }

    private sealed record CustomExecCommand(
        string ConfigPath,
        string ExecFile,
        string Name,
        string Description,
        string NormalizedName,
        string NormalizedPinyin,
        string Initials,
        IReadOnlyList<string> Args,
        IReadOnlyDictionary<string, string> Env,
        IHintIcon? Icon)
    {
        public string WorkingDirectory => Path.GetDirectoryName(ConfigPath) ?? AppContext.BaseDirectory;
    }

    // 生成器支持 Native AOT，避免运行时反射序列化。
    public sealed class CustomExecDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;

        [JsonPropertyName("lucideIcon")]
        public string LucideIcon { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("execFile")]
        public string ExecFile { get; set; } = string.Empty;

        [JsonPropertyName("args")]
        public string[] Args { get; set; } = [];

        [JsonPropertyName("env")]
        public Dictionary<string, string> Env { get; set; } = [];
    }
}

[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CustomExecSource.CustomExecDto))]
internal partial class CustomExecJsonContext : JsonSerializerContext;