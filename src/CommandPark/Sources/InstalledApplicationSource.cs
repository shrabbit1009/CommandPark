using System.Diagnostics;
using NPinyin;

namespace CommandPark.Sources;

public class InstalledApplicationSource : IHintWithPrefix
{
    public List<string> Excludes { get; } = [];

    public bool PrefixMode { get; set; }

    private Task? _scanTask = null;

    /// <summary>
    /// 缓存字典，扫描一次后使用缓存值。LNK 路径对 App 信息。仅当非 null 时可读取。
    /// </summary>
    private static Dictionary<string, App>? _cache;

    /// <summary>
    /// 索引软件的拼音（中文环境）
    /// </summary>
    public bool PinyinMode { get; set; } = true;

    public async IAsyncEnumerable<IHint> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("InstalledApplication");
        
        var map = _cache ?? new Dictionary<string, App>();
        
        var path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs");

        if (_cache is null)
        {
            _scanTask ??= Task.Run(() =>
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        var lnk = Lnk.Lnk.LoadFile(file);
                        
                        var name = string.IsNullOrEmpty(lnk.Name) ? Path.GetFileNameWithoutExtension(file) : lnk.Name;
                        
                        var app = map[file] = new App(name, null, lnk.LocalPath);

                        Console.WriteLine(app);
                    }
                    catch (Exception e)
                    {
                        // 跳过读取错误的
                    }
                }

                Console.WriteLine("InstalledApplicationScanCompleted");

                _cache = map;
            }, CancellationToken.None);
            
            await _scanTask;
        }

        foreach (var app in _cache.Values)
        {
            var name        = $"打开 {app.Name}";
            var description = app.Description ?? "已安装的程序";

            var pinyin = PinyinMode ? Pinyin.GetPinyin(app.Name) : string.Empty ?? string.Empty;
            
            var sim       = ComputeSimilarityHelper.ComputeSimilarity(query, app.Name);
            var simPinyin = ComputeSimilarityHelper.ComputeSimilarity(query, pinyin);
            
            // 搜索词包含程序名称或名称的拼音，并且启用了拼音搜索
            // Console.WriteLine(app.Name.ToLower().Replace(' ', '\0'));
            // Console.WriteLine(pinyin.Replace(' ', '\0'));
            if (app.Name.ToLower().Replace(' ', '\0').Contains(query) || (PinyinMode && pinyin.ToLower().Replace(' ', '\0').Contains(query)))
            {
                Console.WriteLine(app);
                yield return new Hint("installedApplication", name, description, sim, ct => OnRun(app, ct));
            }
        }
    }

    private Task OnRun(App app, CancellationToken cancellationToken)
    {
        Process.Start(app.RunPath);
        return Task.CompletedTask;
    }

    private record App(string Name, string? Description, string RunPath);
}