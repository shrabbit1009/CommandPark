using System.Diagnostics;
using System.Runtime.CompilerServices;
using EverythingSearchClient;

namespace CommandPark.Sources;

public class EverythingSource : IHintSource
{
    private static SearchClient? _everything;
    
    /// <summary>
    /// 每一批结果的数量。
    /// </summary>
    public uint BatchSize { get; set; } = 4;
    
    /// <summary>
    /// 前缀模式，false 时不加任何前缀都触发 Everything 搜索；true 时，只有在搜索前缀为 '#' 或者 'et:' 时，才触发 Everything 搜索。
    /// </summary>
    public bool PrefixMode { get; set; } = true;

    public async IAsyncEnumerable<IHint> SearchAsync(string query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 前缀模式剔除前缀后搜索，非前缀模式保留不动。
        var rawQuery = PrefixMode ? query.TrimStart('#')
                            .TrimStart("et:")
                            .ToString() : query;

        // 检测前缀模式：无前缀则不运行搜索
        if (PrefixMode && !(query.StartsWith('#') || query.StartsWith("et:")))
        {
            Console.WriteLine("EverythingBypass");
            yield break;
        }
        
        Console.WriteLine("Everything");
        
        if (OperatingSystem.IsWindows())
        {
            if (!SearchClient.IsEverythingAvailable())
            {
                Console.WriteLine("EverythingNotAvailable");
                yield return new Hint("everything:everythingNotAvailableError", "Everything 主程序未工作", "需要安装或将 Everything 处于后台运行。");
                yield break;
            }

            if (SearchClient.IsEverythingBusy())
            {
                Console.WriteLine("EverythingBusy");
                yield return new Hint("everything:everythingNotAvailableError",
                                      "Everything 正在工作中",
                                      "Everything 搜索暂时不可用。");
                yield break;
            }
            
            // 每次只搜 BatchSize 个结果
            var len        = BatchSize;
            var everything = _everything ??= new SearchClient();

            for (var i = 0u; ; i += len)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                Console.WriteLine($"Everything {i}..{i+len}");
                
                var results    = everything
                   .Search(rawQuery,
                           i + len, 
                           i, 
                           SearchClient.BehaviorWhenBusy.Continue);

                if (results.TotalItems == 0)
                {
                    yield break;
                }

                foreach (var item in results.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var name = item.Name;
                    var path = Path.Join(item.Path, name);
                    var sim = ComputeSimilarityHelper.ComputeSimilarity(query, name);

                    // 根据类型设定文本。
                    
                    var hint = $"打开\"{name}\"";

                    if (item.Flags.HasFlag(Result.ItemFlags.Drive))
                        hint = $"打开驱动器\"{name}\"";

                    if (item.Flags.HasFlag(Result.ItemFlags.Folder))
                        hint = $"打开文件夹\"{name}\"";
                    
                    yield return new EverythingHint("everything:searchResult", hint, path, sim, ct => OnRun(path, ct))
                    {
                        Payload = item
                    };
                    await Task.Yield();
                }
            }
            
        }
        else
        {
            yield return new Hint("everything:platformNotSupportedError", "Everything 搜索未启用", "该搜索源只在 Windows 上工作。");
        }
    }

    private static Task OnRun(string filename, CancellationToken cancellationToken)
    {
        if (Path.GetExtension(filename) != ".exe")
        {
            Process.Start("explorer.exe", filename);
            return Task.CompletedTask;
        }
        
        Process.Start(filename);
        return Task.CompletedTask;
    }
}

public record EverythingHint : Hint
{
    
    public EverythingHint
        (string id, string name, string description, float similarity, HintRun onRun) : this(id, name, description)
    {
        OnRun      = onRun;
        Similarity = similarity;
    }

    public EverythingHint(string id, string name, string description)
    {
        Id          = id;
        Name        = name;
        Description = description;
    }
    
    public Result.Item Payload { get; init; } = null!;
}