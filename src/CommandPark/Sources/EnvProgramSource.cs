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
                    $"打开\"{name}\"",
                    exeFile,
                    ComputeSimilarity(query, name),
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

    /// <summary>
    /// 简单计算相似。权重：50% 序列相似，50% 仅出现字符串。
    /// </summary>
    private float ComputeSimilarity(string input, string target)
    {
        var aResult = LongestCommonSubstring(input, target);
        var bResult = LcsLength(target, input);

        return (float)((float)aResult * .5 + (float)bResult * .5);
    }

    public static int LcsLength(string a, string b)
    {
        // 确保 b 是较短的那个，减少空间
        if (a.Length < b.Length) (a, b) = (b, a);

        int   m    = a.Length, n = b.Length;
        int[] prev = new int[n + 1];
        int[] curr = new int[n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
                curr[j] = a[i - 1] == b[j - 1]
                    ? prev[j - 1] + 1
                    : Math.Max(prev[j], curr[j - 1]);

            (prev, curr) = (curr, prev);
            Array.Clear(curr, 0, n + 1);
        }

        return prev[n];
    }

    public static int LongestCommonSubstring(string a, string b)
    {
        int   n    = a.Length, m = b.Length, maxLen = 0;
        int[] prev = new int[m + 1];

        for (int i = 1; i <= n; i++)
        {
            // 必须从右往左遍历，避免覆盖上一行的值
            for (int j = m; j >= 1; j--)
            {
                prev[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : 0;
                maxLen  = Math.Max(maxLen, prev[j]);
            }

            prev[0] = 0;
        }

        return maxLen;
    }

    private Task OnRun(string path, CancellationToken cancellationToken)
    {
        Process.Start(path);
        return Task.CompletedTask;
    }
}