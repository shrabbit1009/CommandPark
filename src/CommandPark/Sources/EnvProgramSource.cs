using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommandPark.Utils;

namespace CommandPark.Sources;

/// <summary>
/// 在环境变量 Path（以及 Windows 的 System32 / Windows 目录）里找可执行程序。
/// <para>
/// 只查目录本身、不递归子目录——这与系统查找 PATH 的语义一致。
/// 递归既慢（C:\Windows 下的树非常大），又容易因为无权限目录抛
/// <see cref="UnauthorizedAccessException"/> 而中断整个搜索（表现为"cmd 搜得到、后面的 explorer 搜不到"）。
/// </para>
/// </summary>
public class EnvProgramSource : IHintSource, IConfigReader
{
    private static readonly EnumerationOptions SearchOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
    };

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<ProgramMatch>();

        foreach (var directory in GetSearchDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var exeFile in EnumerateExecutables(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileNameWithoutExtension(exeFile);

                // 同名程序只取第一个（PATH 顺序优先，和系统查找行为一致）
                if (!seen.Add(name))
                {
                    continue;
                }

                if (!name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(new ProgramMatch(
                    exeFile,
                    name,
                    ComputeSimilarityHelper.ComputeSimilarity(query, name)));
            }
        }

        // PATH 的目录顺序只用于解决同名程序，不应该决定结果排序。
        // 这里先收齐再按相关度排序，保证输入 cmd 时 cmd 永远排在 jcmd 前面。
        foreach (var match in matches
                     .OrderByDescending(m => m.Score)
                     .ThenBy(m => m.Name.Length)
                     .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new Hint(
                "executable:env",
                $"系统程序 {match.Name}",
                "位于环境变量的程序",
                match.Score,
                ct => OnRun(match.Path, ct))
            {
                Icon = new ExecutableHintIcon(match.Path),
            };
        }
    }

    /// <summary>PATH 里的目录 + Windows 上的 System32 / Windows，去重后保持原顺序。</summary>
    private static List<string> GetSearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var directories = path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .ToList();

        if (OperatingSystem.IsWindows())
        {
            directories.Add(@"C:\Windows\System32");
            directories.Add(@"C:\Windows");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return directories.Where(seen.Add).ToList();
    }

    /// <summary>枚举目录下的 exe；出错（权限/目录消失）时返回空，绝不让整个搜索挂掉。</summary>
    private static string[] EnumerateExecutables(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.exe", SearchOptions).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private Task OnRun(string path, CancellationToken cancellationToken)
    {
        Process.Start(path);
        return Task.CompletedTask;
    }

    private sealed record ProgramMatch(string Path, string Name, float Score);

    public bool ReadConfig(JsonNode json)
    {
        // 空的 envProgram 节点即表示启用该源；不提供节点则禁用。
        return json["sources"] is JsonObject sources &&
               sources["envProgram"] is JsonObject;
    }
}
