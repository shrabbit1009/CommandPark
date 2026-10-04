using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AngouriMath;
using AngouriMath.Extensions;
using CommandPark.Utils.SourceHelpers;
using RestSharp;

namespace CommandPark.Sources;

/// <summary>
/// LaTeX / AngouriMath 计算源。计算放到后台线程执行，支持超时与调用方取消。
/// </summary>
public class CalcSource : IHintSource, IHintWithPrefix, IConfigReader
{
    /// <summary>本源支持的前缀。</summary>
    public static readonly string[] Prefixes = ["~", "c:", "calc:"];

    /// <summary>前缀模式：true 时只有带前缀才运行。</summary>
    public bool PrefixMode { get; set; } = true;

    /// <summary>单次计算超时时间，单位秒，配置范围为 1..30。</summary>
    public int TimeoutSeconds { get; set; } = 2;

    public async IAsyncEnumerable<IHint> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hasPrefix = HintPrefix.TryStrip(query, out var rawQuery, Prefixes);
        if (PrefixMode && !hasPrefix)
        {
            yield break;
        }

        var expression = rawQuery.Trim();
        if (expression.Length == 0)
        {
            yield break;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 30)));

        string?       result          = null;
        Exception?    error           = null;
        var           timedOut        = false;
        Task<string>? calculationTask = null;

        try
        {
            calculationTask = Task.Run(
                () => Evaluate(expression, timeoutCts.Token),
                timeoutCts.Token);

            // WaitAsync 明确给 UI/Provider 一个截止时间；AngouriMath 的本地令牌负责中止实际计算。
            result = await calculationTask.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Observe(calculationTask);
                throw;
            }

            timedOut = true;
            Observe(calculationTask);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        if (timedOut)
        {
            yield return new Hint(
                "lcalc:timeout",
                "计算超时",
                $"表达式计算超过 {Math.Clamp(TimeoutSeconds, 1, 30)} 秒，已取消。",
                1,
                _ => Task.CompletedTask)
            {
                Icon = new LucideIdHintIcon("TimerOff"),
            };

            if (CreateDesmosHint(expression, query, hasPrefix) is { } timeoutDesmosHint)
            {
                yield return timeoutDesmosHint;
            }
            yield break;
        }

        if (error is not null)
        {
            yield return new Hint(
                "lcalc:error",
                "表达式不正确",
                error.Message,
                1,
                _ => Task.CompletedTask)
            {
                Icon = new LucideIdHintIcon("CircleAlert"),
            };

            if (CreateDesmosHint(expression, query, hasPrefix) is { } errorDesmosHint)
            {
                yield return errorDesmosHint;
            }
            yield break;
        }

        var details =
            $"输入: {expression}{Environment.NewLine}" +
            $"结果: {result}";

        yield return new DetailedHint(
            "calc:result",
            $"= {result}",
            "高级计算器，按 Enter 查看详情",
            1,
            ct => OpenDesmosAsync(expression, ct),
            details,
            "在 Desmos 中打开 (Alt+D)")
        {
            Icon = new LucideIdHintIcon("Calculator"),
        };

        if (CreateDesmosHint(expression, query, hasPrefix) is { } desmosHint)
        {
            yield return desmosHint;
        }

    }

    private static Hint? CreateDesmosHint(string expression, string query, bool hasPrefix)
    {
        // 仅在明确的 c:/calc: 前缀下额外提供独立的 Desmos 跳转项。
        if (!hasPrefix || !HasExactCalcPrefix(query))
        {
            return null;
        }

        return new Hint(
            "calc:desmos",
            "在 Desmos 中打开",
            $"将 {expression} 转换为 LaTeX 并打开图表。",
            0.95f,
            ct => OpenDesmosAsync(expression, ct))
        {
            Icon = new LucideIdHintIcon("ExternalLink"),
        };
    }

    private static bool HasExactCalcPrefix(string query)
    {
        var trimmed = query.Trim();
        return trimmed.StartsWith("c:", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("calc:", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task OpenDesmosAsync(string expression, CancellationToken cancellationToken)
    {
        var latex = expression.Latexize();
        var url = await DesmosHelper.GenerateDesmosSnapshotUrl(latex, cancellationToken);
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(url);
        Process.Start(startInfo);
    }

    private static string Evaluate(string expression, CancellationToken cancellationToken)
    {
        try
        {
            // AngouriMath 使用线程本地取消令牌，必须在实际计算线程中设置。
            MathS.Multithreading.SetLocalCancellationToken(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var parsed = Entity.Parse(expression, null);
            cancellationToken.ThrowIfCancellationRequested();

            return parsed.EvalNumerical().ToString();
        }
        finally
        {
            MathS.Multithreading.SetLocalCancellationToken(CancellationToken.None);
        }
    }

    private static void Observe(Task? task)
    {
        if (task is null)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public bool ReadConfig(JsonNode json)
    {
        if (json["sources"] is not JsonObject sources ||
            sources["latexCalc"] is not JsonObject section)
        {
            return false;
        }

        PrefixMode = section["prefixMode"]?.GetValue<bool>() ?? PrefixMode;
        TimeoutSeconds = Math.Clamp(
            section["timeoutSeconds"]?.GetValue<int>() ?? TimeoutSeconds,
            1,
            30);

        return true;
    }
}
