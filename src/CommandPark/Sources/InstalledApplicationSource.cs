using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using NPinyin;

namespace CommandPark.Sources;

/// <summary>
/// 已安装程序搜索源：扫描开始菜单里的快捷方式（用户 + 所有用户）。
/// <para>
/// 名称取<b>快捷方式文件名</b>（开始菜单里显示的就是它）——<c>LnkFile.Name</c> 经常是 null，
/// 或者是 <c>@%SystemRoot%\system32\Shell32.dll,-21762</c> 这种资源引用，所以只把它当额外别名。
/// </para>
/// <para>
/// 匹配范围：名称、拼音（连续，<c>yuanshen</c>）、拼音首字母（<c>ys</c>）、目标程序名；
/// 大小写、空格与常见分隔符都不敏感。结果按相关度排序。
/// </para>
/// <para>
/// 前缀模式见 <see cref="PrefixMode"/>：默认不加前缀也搜，<c>ap:</c> / <c>app:</c> 前缀会让本源结果加权。
/// </para>
/// </summary>
public class InstalledApplicationSource : IHintWithPrefix, IConfigReader
{
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = ["$", "app:", "ap:", "ia:"];

    /// <summary>常见"噪音"快捷方式关键词（卸载/说明/网站/更新……）。</summary>
    private static readonly string[] NoiseKeywords =
    [
        "uninstall", "卸载", "readme", "read me", "说明", "帮助", "help", "tutorial", "教程",
        "samples", "sample", "website", "官网", "网站", "常见问题", "faq", "check for updates",
        "检查更新", "更新日志", "changelog", "license", "许可证", "许可协议", "manual", "文档",
        "documentation", "汉化", "许可",
    ];

    private static Task<IReadOnlyList<App>>? _scanTask;

    /// <summary>额外排除的关键字（名字或路径包含即排除，大小写不敏感）。</summary>
    public List<string> Excludes { get; } = [];

    /// <summary>
    /// 前缀匹配模式。false（默认）：不输入前缀也能匹配，输入 <c>ap:</c>/<c>app:</c> 前缀会提升显示权重；
    /// true：只有输入前缀后才触发本源。
    /// </summary>
    public bool PrefixMode { get; set; }

    /// <summary>是否启用拼音匹配（连续拼音与首字母），默认开。</summary>
    public bool PinyinMode { get; set; } = true;

    /// <summary>是否过滤常见的噪音快捷方式（卸载/说明/网站/更新……），默认开。</summary>
    public bool SkipNoise { get; set; } = true;

    /// <summary>名字超过这个长度就当成描述型噪音（例如安装器写进去的整句话），默认 64。</summary>
    public int MaxNameLength { get; set; } = 64;

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 前缀：剥离后搜索；PrefixMode=true 时没前缀直接不跑
        var hasPrefix = HintPrefix.TryStrip(query, out var rawQuery, Prefixes);
        if (PrefixMode && !hasPrefix)
        {
            yield break;
        }

        var normalizedQuery = Normalize(rawQuery);
        if (normalizedQuery.Length == 0)
        {
            yield break;
        }

        var apps = await GetApplicationsAsync();

        var matches = new List<(App App, float Score)>();
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsFilteredOut(app))
            {
                continue;
            }

            if (TryScore(normalizedQuery, app, hasPrefix, out var score))
            {
                matches.Add((app, score));
            }
        }

        // 相关度高的在前；同分时短名字在前
        foreach (var (app, score) in matches
                     .OrderByDescending(m => m.Score)
                     .ThenBy(m => m.App.Name.Length)
                     .ThenBy(m => m.App.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new Hint(
                "installedApplication",
                $"打开 {app.Name}",
                string.IsNullOrWhiteSpace(app.Description) ? app.RunPath : app.Description,
                score,
                ct => OnRun(app, ct))
            {
                Icon = CreateIcon(app),
            };
        }
    }

    /// <summary>
    /// 计算匹配程度。返回 false 表示不匹配。
    /// </summary>
    private bool TryScore(string query, App app, bool prefixBoost, out float score)
    {
        score = 0f;

        // 1) 名称（归一化：忽略大小写、空格与常见分隔符）
        if (app.NormalizedName == query)
        {
            score = 1.00f;
        }
        else if (app.NormalizedName.StartsWith(query, StringComparison.Ordinal))
        {
            score = 0.90f;
        }
        else if (app.NormalizedName.Contains(query, StringComparison.Ordinal))
        {
            score = 0.70f;
        }

        if (PinyinMode && app.NormalizedPinyin.Length > 0)
        {
            // 2) 连续拼音：yuan shen → yuanshen，所以 "yuans"/"yuanshen" 都能命中
            if (app.NormalizedPinyin.StartsWith(query, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.85f);
            }
            else if (app.NormalizedPinyin.Contains(query, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.65f);
            }

            // 3) 拼音首字母：原神 → ys，米哈游启动器 → mhyqdq
            if (app.Initials.Length > 0 && app.Initials.StartsWith(query, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.80f);
            }
        }

        // 4) 别名：目标程序名（原神 → genshinimpact）、快捷方式的 Lnk.Name
        foreach (var alias in app.NormalizedAliases)
        {
            if (alias.StartsWith(query, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.60f);
                break;
            }

            if (alias.Contains(query, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.50f);
                break;
            }
        }

        if (score <= 0f)
        {
            return false;
        }

        // 同分时让名字短的靠前
        score += Math.Max(0f, 0.05f - app.Name.Length * 0.001f);

        // 带了本源前缀 → 权重明显更高（其它源比不过）
        if (prefixBoost)
        {
            score += 1.0f;
        }

        return true;
    }

    /// <summary>是否被用户配置或噪音规则过滤掉。</summary>
    private bool IsFilteredOut(App app)
    {
        if (app.Name.Length > MaxNameLength)
        {
            return true;
        }

        foreach (var term in Excludes)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            if (app.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || app.RunPath.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!SkipNoise)
        {
            return false;
        }

        return NoiseKeywords.Any(keyword => IsNoiseMatch(app.NormalizedName, keyword));
    }

    private static bool IsNoiseMatch(string normalizedName, string keyword)
    {
        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword.Length == 0)
        {
            return false;
        }

        // 英文关键词按"词首"判断（避免 HelpDesk 之类的误伤），中文关键词按包含判断
        return normalizedKeyword.All(char.IsAscii)
            ? normalizedName.StartsWith(normalizedKeyword, StringComparison.Ordinal)
            : normalizedName.Contains(normalizedKeyword, StringComparison.Ordinal);
    }

    private static Task<IReadOnlyList<App>> GetApplicationsAsync()
    {
        var existing = _scanTask;
        if (existing is not null)
        {
            return existing;
        }

        // 扫描只做一次，且不跟随某次搜索的取消（缓存供后续搜索复用）
        var created = Task.Run(ScanApplications, CancellationToken.None);
        return Interlocked.CompareExchange(ref _scanTask, created, null) ?? created;
    }

    private static IReadOnlyList<App> ScanApplications()
    {
        var apps = new List<App>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetStartMenuRoots())
        {
            string[] shortcuts;
            try
            {
                shortcuts = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;   // 目录不存在/无权限：跳过
            }

            foreach (var shortcut in shortcuts)
            {
                try
                {
                    var lnk = Lnk.Lnk.LoadFile(shortcut);

                    var runPath = Expand(lnk.LocalPath);
                    if (runPath.Length == 0 || !File.Exists(runPath))
                    {
                        continue;   // 没有可启动的目标（控制面板项、失效快捷方式等）
                    }

                    var name = Path.GetFileNameWithoutExtension(shortcut);
                    if (string.IsNullOrWhiteSpace(name) || !seenNames.Add(name))
                    {
                        continue;   // 名字为空，或同名快捷方式（用户开始菜单优先）
                    }

                    apps.Add(new App(
                        Name: name,
                        // Lnk.Name 是快捷方式的"备注"，只当描述文本用，不参与匹配（否则备注里的长句子会污染搜索结果）
                        Description: string.IsNullOrWhiteSpace(lnk.Name) ? null : lnk.Name.Trim(),
                        RunPath: runPath,
                        Arguments: lnk.Arguments,
                        WorkingDirectory: Expand(lnk.WorkingDirectory),
                        IconPath: ExpandIconPath(lnk.IconLocation),
                        NormalizedName: Normalize(name),
                        NormalizedPinyin: Normalize(Pinyin.GetPinyin(name)),
                        Initials: BuildInitials(name),
                        NormalizedAliases: BuildAliases(runPath)));
                }
                catch (Exception)
                {
                    // 快捷方式损坏/解析失败：跳过
                }
            }
        }

        return apps;
    }

    /// <summary>用户开始菜单 + 所有用户开始菜单。</summary>
    private static IEnumerable<string> GetStartMenuRoots()
    {
        yield return Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");

        yield return Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");
    }

    /// <summary>别名只放目标程序名（原神 → genshinimpact），备注不进匹配。</summary>
    private static string[] BuildAliases(string runPath)
    {
        var targetName = Path.GetFileNameWithoutExtension(runPath);
        return string.IsNullOrWhiteSpace(targetName) ? [] : [Normalize(targetName)];
    }

    /// <summary>
    /// 由名称生成拼音首字母：原神 → ys，阿里云盘 → alyp，Visual Studio Code → vsc，BaiduNetdisk → bn。
    /// <para>
    /// 注意：必须基于"名称"逐字算，不能拿 <c>Pinyin.GetPinyin</c> 的整串结果算——
    /// NPinyin 对英文会逐字母插入空格（"Visual" → "V i s u a l"），那样只会得到整串而不是首字母。
    /// </para>
    /// </summary>
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

            // 汉字：每个字都算一个词，取该字拼音的首字母
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

            // 英文/数字：词首，或驼峰边界（BaiduNetdisk → bn）
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

    /// <summary>归一化：小写，去掉空格与常见分隔符，让 "visual studio" 与 "visualstudio" 等价。</summary>
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

    private static string Expand(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Environment.ExpandEnvironmentVariables(path).Trim();

    /// <summary>图标位置形如 <c>C:\app\a.exe,0</c> 或 <c>%SystemRoot%\system32\shell32.dll,-123</c>，这里取出文件部分。</summary>
    private static string? ExpandIconPath(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return null;
        }

        var path = iconLocation.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out _))
        {
            path = path[..comma];
        }

        var expanded = Expand(path);
        return File.Exists(expanded) ? expanded : null;
    }

    private static IHintIcon? CreateIcon(App app)
    {
        // 1) 快捷方式指定的是位图图标（png/jpg/…）→ 直接用
        if (IsBitmapIcon(app.IconPath))
        {
            return new LocalImageHintIcon(app.IconPath!);
        }

        // 2) 目标是可执行文件 → 用它自带的图标
        //    （IconLocation 是 .ico 时也走这里：Avalonia 解不了 ico，退回目标图标更稳）
        if (IsExecutableIcon(app.RunPath))
        {
            return new ExecutableHintIcon(app.RunPath);
        }

        // 3) 其它文件 → 缩略图，取不到会回落文件类型图标
        return new ThumbnailHintIcon(app.RunPath);
    }

    private static bool IsBitmapIcon(string? path) => path is not null && Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg";

    private static bool IsExecutableIcon(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".exe" or ".dll" or ".scr" or ".cpl" or ".ocx" or ".sys";

    private static Task OnRun(App app, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = app.RunPath,
            UseShellExecute = true,   // 与双击开始菜单一致
        };

        if (!string.IsNullOrWhiteSpace(app.Arguments))
        {
            startInfo.Arguments = app.Arguments;
        }

        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory) && Directory.Exists(app.WorkingDirectory))
        {
            startInfo.WorkingDirectory = app.WorkingDirectory;
        }

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    /// <summary>缓存里的一个开始菜单条目（匹配所需的字段都预先归一化好）。</summary>
    private sealed record App(
        string Name,
        string? Description,
        string RunPath,
        string? Arguments,
        string? WorkingDirectory,
        string? IconPath,
        string NormalizedName,
        string NormalizedPinyin,
        string Initials,
        string[] NormalizedAliases);

    public bool ReadConfig(JsonNode json)
    {
        // 定位配置节
        var node = json["sources"]?["installedApplication"];

        // 如果没有这个，则代表该源没有启用，直接不运行。
        if (node is null)
            return false;
        
        // 排除项，找不到则默认
        if (node["excludes"]?.GetValue<string[]>() is {} exc)
            Excludes.AddRange(exc);

        // 前缀模式，找不到则默认
        PrefixMode    = node["prefixMode"]?.GetValue<bool>() ?? PrefixMode;
        SkipNoise     = node["skipNoise"]?.GetValue<bool>() ?? SkipNoise;
        PinyinMode    = node["pinyinMode"]?.GetValue<bool>() ?? PinyinMode;
        MaxNameLength = node["maxLength"]?.GetValue<int>() ?? MaxNameLength;
        
        return true;
    }
}