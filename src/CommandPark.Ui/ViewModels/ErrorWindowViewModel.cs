using System;
using CommandPark.Strings;
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
        Title   = Localization.Get("error.title");
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
                Localization.Format("error.elevation.required", name),

            // 1223 = 用户在 UAC 弹窗里点了“否”
            Win32Exception { NativeErrorCode: 1223 } =>
                Localization.Format("error.elevation.canceled", name),

            // 5 = ERROR_ACCESS_DENIED
            Win32Exception { NativeErrorCode: 5 } =>
                Localization.Format("error.permission.denied", name),

            UnauthorizedAccessException =>
                Localization.Format("error.permission.unauthorized", name),

            FileNotFoundException or DirectoryNotFoundException =>
                Localization.Format("error.notFound", name),

            Win32Exception win32 =>
                Localization.Format("error.launchFailed", name, win32.Message, win32.NativeErrorCode),

            _ => Localization.Format("error.unknown", name, exception.Message),
        };
    }

    private static string BuildDetails(IHint hint, Exception exception) =>
        $"Hint    : {hint.Name} ({hint.Id})" + Environment.NewLine +
        $"Command : {hint.Description}" + Environment.NewLine +
        Environment.NewLine +
        exception;
}