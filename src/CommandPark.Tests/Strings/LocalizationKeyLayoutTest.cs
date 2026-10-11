using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommandPark.Strings;
using JetBrains.Annotations;

namespace CommandPark.Tests.Strings;

/// <summary>
/// 翻译键布局的看门狗：规则写在 <c>docs/i18n.md</c>，这里把规则变成会失败的断言。
/// <para>
/// 只扫描核心库与界面工程；测试工程不参与，因为它会故意使用一些不存在的键。
/// </para>
/// </summary>
[TestSubject(typeof(Localization))]
public class LocalizationKeyLayoutTest
{
    /// <summary>键路径的最大层级：再深就该拆到别的分组里去。</summary>
    private const int MaxDepth = 4;

    /// <summary>每一段最多几个单词：3 个词就该拆成两级（userStatusOffline -&gt; user.status.offline）。</summary>
    private const int MaxWordsPerSegment = 2;

    /// <summary>单个分组最多放几个键：再多就该继续分组，避免一层平铺太多。</summary>
    private const int MaxKeysPerGroup = 8;

    /// <summary>顶层是领域列表，允许比普通分组略多一点。</summary>
    private const int MaxTopLevelGroups = 12;

    private static readonly Regex KeyLiteral =
        new("\"([a-z][A-Za-z0-9]*(?:\\.[A-Za-z0-9]+)+)\"", RegexOptions.Compiled);

    private static readonly Regex XamlLoc =
        new(@"\{loc:Loc\s+([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)\}", RegexOptions.Compiled);

    private static readonly Regex WordBoundary = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    // ------------------------------------------------------------------
    // 引用与定义必须一一对应
    // ------------------------------------------------------------------

    [Fact]
    public void EveryReferencedKeyIsDefined()
    {
        var defined = DefinedKeys();
        var missing = ReferencedKeys().Where(key => !defined.Contains(key)).Order().ToList();

        Assert.True(
            missing.Count == 0,
            $"代码里引用了不存在的翻译键（拼错了，或忘了同步 lang\\zh_CN.json）：{string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryDefinedKeyIsReferenced()
    {
        var referenced = ReferencedKeys();
        var unused = DefinedKeys().Where(key => !referenced.Contains(key)).Order().ToList();

        Assert.True(
            unused.Count == 0,
            $"lang\\zh_CN.json 里有没人用的翻译键（改文案时应顺手删掉旧键）：{string.Join(", ", unused)}");
    }

    // ------------------------------------------------------------------
    // 布局规则
    // ------------------------------------------------------------------

    [Fact]
    public void Keys_AreNotTooDeep()
    {
        var tooDeep = DefinedKeys()
            .Where(key => key.Split('.').Length > MaxDepth)
            .Order()
            .ToList();

        Assert.True(tooDeep.Count == 0, $"键的层级超过 {MaxDepth} 层：{string.Join(", ", tooDeep)}");
    }

    [Fact]
    public void Keys_UseAtMostTwoWordsPerSegment()
    {
        var tooWordy = DefinedKeys()
            .SelectMany(key => key.Split('.').Select(segment => (Key: key, Segment: segment)))
            .Where(pair => WordBoundary.Split(pair.Segment).Length > MaxWordsPerSegment)
            .Select(pair => $"{pair.Key}（{pair.Segment}）")
            .Order()
            .ToList();

        Assert.True(tooWordy.Count == 0, $"键的某一段超过 {MaxWordsPerSegment} 个单词：{string.Join(", ", tooWordy)}");
    }

    [Fact]
    public void Groups_AreNotFlattened()
    {
        var tooWide = GroupSizes()
            .Where(group => group.ChildCount > (group.Path.Length == 0 ? MaxTopLevelGroups : MaxKeysPerGroup))
            .Select(group => $"{group.Path}（{group.ChildCount} 个键）")
            .Order()
            .ToList();

        Assert.True(tooWide.Count == 0, $"分组里平铺了太多键，应该继续分组：{string.Join(", ", tooWide)}");
    }

    // ------------------------------------------------------------------
    // 数据来源
    // ------------------------------------------------------------------

    /// <summary>zh_CN.json 里的全部叶子键。</summary>
    private static HashSet<string> DefinedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        void Walk(JsonNode? node, string prefix)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj)
                    {
                        Walk(child, prefix.Length == 0 ? key : $"{prefix}.{key}");
                    }

                    break;

                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    keys.Add(prefix);
                    break;
            }
        }

        Walk(JsonNode.Parse(File.ReadAllText(BaseLanguageFile)), string.Empty);
        return keys;
    }

    /// <summary>每个分组（JSON 对象）路径 -&gt; 它直接包含的键数。</summary>
    private static IEnumerable<(string Path, int ChildCount)> GroupSizes()
    {
        var sizes = new List<(string Path, int ChildCount)>();

        void Walk(JsonNode? node, string path)
        {
            if (node is not JsonObject obj)
            {
                return;
            }

            sizes.Add((path, obj.Count));
            foreach (var (key, child) in obj)
            {
                Walk(child, path.Length == 0 ? key : $"{path}.{key}");
            }
        }

        Walk(JsonNode.Parse(File.ReadAllText(BaseLanguageFile)), string.Empty);
        return sizes;
    }

    /// <summary>代码里实际用到的键：C# 的 Localization.Get/Format/TryGet 与 XAML 的 {loc:Loc 键}。</summary>
    private static HashSet<string> ReferencedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            if (file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in XamlLoc.Matches(text))
                {
                    keys.Add(match.Groups[1].Value);
                }

                continue;
            }

            foreach (var arguments in LocalizationCallArguments(text))
            {
                foreach (Match match in KeyLiteral.Matches(arguments))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }
        }

        return keys;
    }

    /// <summary>依次取出每个 <c>Localization.XXX(...)</c> 的实参文本（靠括号配对定位）。</summary>
    private static IEnumerable<string> LocalizationCallArguments(string source)
    {
        const string marker = "Localization.";
        var index = 0;

        while ((index = source.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            var open = source.IndexOf('(', index);
            if (open < 0)
            {
                yield break;
            }

            var depth = 0;
            var end = open;
            for (; end < source.Length; end++)
            {
                if (source[end] == '(')
                {
                    depth++;
                }
                else if (source[end] == ')' && --depth == 0)
                {
                    break;
                }
            }

            yield return source[(open + 1)..Math.Min(end, source.Length)];
            index = end + 1;
        }
    }

    /// <summary>核心库与界面工程里的 .cs / .axaml（跳过 bin、obj）。</summary>
    private static IEnumerable<string> SourceFiles()
    {
        foreach (var project in new[] { "CommandPark", "CommandPark.Ui" })
        {
            var directory = Path.Combine(RepositoryRoot, "src", project);
            var separator = Path.DirectorySeparatorChar;

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{separator}bin{separator}") || file.Contains($"{separator}obj{separator}"))
                {
                    continue;
                }

                if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static string BaseLanguageFile =>
        Path.Combine(RepositoryRoot, "src", "CommandPark", "lang", "zh_CN.json");

    private static string RepositoryRoot
    {
        get
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
}