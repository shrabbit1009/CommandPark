using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommandPark.Strings;

namespace CommandPark.Sources;

/// <summary>
/// 运行输入的命令。支持 <c>@</c>、<c>run:</c> 和 <c>r:</c> 前缀；未带前缀时作为低优先级兜底。
/// </summary>
public class RunSource : IHintWithPrefix, IConfigReader
{
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = ["@", "run:", "r:"];

    /// <summary>
    /// 前缀模式：true 时只有带前缀才运行；false 时任意输入都作为兜底候选，带前缀时提高权重。
    /// </summary>
    public bool PrefixMode { get; set; }

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hasPrefix = HintPrefix.TryStrip(query, out var commandLine, Prefixes);

        if (PrefixMode && !hasPrefix)
        {
            yield break;
        }

        commandLine = commandLine.Trim();
        if (commandLine.Length == 0)
        {
            yield break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();
        yield return new Hint(
            "run",
            Localization.Format("sources.run.name", commandLine),
            Localization.Get("sources.run.description"),
            hasPrefix ? 1.0f : 0.0f,
            ct => OnRun(commandLine, ct));
    }

    private Task OnRun(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new RunException(Localization.Get("sources.run.missingProgram"));
        }

        var (fileName, arguments) = SplitCommandLine(input);
        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    /// <summary>把“可执行文件 + 原始参数”拆开，保留参数中的引号和空格。</summary>
    private static (string FileName, string Arguments) SplitCommandLine(string commandLine)
    {
        var trimmed = commandLine.Trim();

        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var quotedFileName = trimmed[1..closingQuote];
                var quotedArguments = trimmed[(closingQuote + 1)..].Trim();
                return (quotedFileName, quotedArguments);
            }
        }

        var separator = trimmed.IndexOf(' ');
        return separator < 0
            ? (trimmed, string.Empty)
            : (trimmed[..separator], trimmed[(separator + 1)..].Trim());
    }

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources ||
            sources["run"] is not JsonObject section)
        {
            return false;
        }

        PrefixMode = section["prefixMode"]?.GetValue<bool>() ?? PrefixMode;
        return true;
    }

    public class RunException : Exception
    {
        public RunException()
        {
        }

        public RunException(string message) : base(message)
        {
        }

        public RunException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
