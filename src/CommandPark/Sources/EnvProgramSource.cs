using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CommandPark.Sources;

/// <summary>
/// 在环境变量 Path（以及 Windows 的 System32 / Windows 目录）里找可执行程序。
/// <para>
/// 只查目录本身、不递归子目录——这与系统查找 PATH 的语义一致。
/// 递归既慢（C:\Windows 下的树非常大），又容易因为无权限目录抛
/// <see cref="UnauthorizedAccessException"/> 而中断整个搜索（表现为"cmd 搜得到、后面的 explorer 搜不到"）。
/// </para>
/// </summary>
public class EnvProgramSource : IHintSource
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                yield return new Hint(
                    "executable:env",
                    $"系统程序 {name}",
                    "位于环境变量的程序",
                    ComputeSimilarityHelper.ComputeSimilarity(query, name),
                    ct => OnRun(exeFile, ct))
                {
                    Icon = new ExecutableHintIcon(exeFile),
                };
            }
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
}