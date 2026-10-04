using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommandPark.Utils;
using EverythingSearchClient;

namespace CommandPark.Sources;

public class EverythingSource : IHintWithPrefix, IConfigReader
{
    private static SearchClient? _everything;
    
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = ["et:", "#"];

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
        // 剔除前缀（# 或 et:）；PrefixMode=false 时无前缀也搜索，带前缀时结果加权。
        var hasPrefix = HintPrefix.TryStrip(query, out var rawQuery, Prefixes);

        // 检测前缀模式：无前缀则不运行搜索
        if (PrefixMode && !hasPrefix)
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
                
                Result results;
                try
                {
                    results = everything.Search(rawQuery, i + len, i, SearchClient.BehaviorWhenBusy.Continue);
                }
                catch (ResultsNotReceivedException)
                {
                    // Everything 在"没有匹配结果"（或翻页超过结果范围）时会抛这个异常，而不是返回 0 条，
                    // 这里按"没有更多结果"收尾，避免用户搜不到东西时弹出错误。
                    Console.WriteLine("EverythingNoResults");
                    yield break;
                }

                if (results.TotalItems == 0)
                {
                    yield break;
                }

                foreach (var item in results.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var name = item.Name;
                    var path = Path.Join(item.Path, name);
                    // 相似度按剥离前缀后的查询计算，并按是否使用前缀加权
                    var sim = ComputeSimilarityHelper.ComputeSimilarity(rawQuery, name);
                    if (hasPrefix)
                    {
                        sim += 1.0f;
                    }

                    // 根据类型设定文本。
                    
                    var hint = $"打开 {name}";

                    if (item.Flags.HasFlag(Result.ItemFlags.Drive))
                        hint = $"打开驱动器 {name}";

                    if (item.Flags.HasFlag(Result.ItemFlags.Folder))
                        hint = $"打开文件夹 {name}";
                    
                    yield return new EverythingHint("everything:searchResult", hint, path, sim, ct => OnRun(path, ct))
                    {
                        Payload = item,
                        Icon    = CreateIcon(item, path),
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

    /// <summary>
    /// 按结果类型挑图标：
    /// 程序 → 自带图标（取最大那张）；文件夹/驱动器 → 系统图标；
    /// 其它文件 → 优先预览缩略图（界面层判断能否取到，取不到自动回落文件图标）。
    /// </summary>
    private static IHintIcon CreateIcon(Result.Item item, string path)
    {
        if (item.Flags.HasFlag(Result.ItemFlags.Folder) || item.Flags.HasFlag(Result.ItemFlags.Drive))
        {
            return new FileIconHintIcon(path);
        }

        return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            ? new ExecutableHintIcon(path)
            : new ThumbnailHintIcon(path);
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

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources ||
            sources["everything"] is not JsonObject section)
        {
            return false;
        }

        PrefixMode = section["prefixMode"]?.GetValue<bool>() ?? PrefixMode;

        if (section["batchSize"]?.GetValue<int>() is { } batchSize)
        {
            BatchSize = (uint)Math.Clamp(batchSize, 1, 1000);
        }

        return true;
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
