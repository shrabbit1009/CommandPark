using System;
using System.ComponentModel;
using System.IO;

namespace CommandPark.Ui.ViewModels;

/// <summary>
/// 错误提示窗口的视图模型：一句人话说明 + 可选的完整堆栈跟踪。
/// </summary>
public sealed class ErrorWindowViewModel
{
    public ErrorWindowViewModel(HintRunFailedEventArgs failure) : this(failure.Hint, failure.Exception)
    {
    }

    public ErrorWindowViewModel(IHint hint, Exception exception)
    {
        Title   = "命令执行失败";
        Message = Describe(hint, exception);
        Details = BuildDetails(hint, exception);
    }

    /// <summary>窗口标题。</summary>
    public string Title { get; }

    /// <summary>给人看的说明（常见错误直接说清原因）。</summary>
    public string Message { get; }

    /// <summary>完整异常信息（含堆栈），默认折叠，按需展开。</summary>
    public string Details { get; }

    /// <summary>是否有详情可展示。</summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    private static string Describe(IHint hint, Exception exception)
    {
        var name = string.IsNullOrWhiteSpace(hint.Name) ? hint.Id : hint.Name;

        return exception switch
        {
            // 740 = ERROR_ELEVATION_REQUIRED：目标程序要求管理员权限
            Win32Exception { NativeErrorCode: 740 } =>
                $"「{name}」需要管理员权限，而 CommandPark 当前不是以管理员身份运行的。\n" +
                "请用管理员身份重新启动 CommandPark，或改选不需要提权的命令。",

            // 1223 = 用户在 UAC 弹窗里点了“否”
            Win32Exception { NativeErrorCode: 1223 } =>
                $"「{name}」的权限提升请求被取消了，命令没有执行。",

            // 5 = ERROR_ACCESS_DENIED
            Win32Exception { NativeErrorCode: 5 } =>
                $"「{name}」被系统拒绝访问（访问被拒绝），通常需要更高的权限。",

            UnauthorizedAccessException =>
                $"没有权限执行「{name}」。",

            FileNotFoundException or DirectoryNotFoundException =>
                $"找不到「{name}」对应的文件，可能已被移动或删除。",

            Win32Exception win32 =>
                $"启动「{name}」失败：{win32.Message}（Win32 错误码 {win32.NativeErrorCode}）",

            _ => $"执行「{name}」时出错：{exception.Message}",
        };
    }

    private static string BuildDetails(IHint hint, Exception exception) =>
        $"Hint    : {hint.Name} ({hint.Id})" + Environment.NewLine +
        $"Command : {hint.Description}" + Environment.NewLine +
        Environment.NewLine +
        exception;
}