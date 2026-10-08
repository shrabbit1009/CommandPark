using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

namespace CommandPark.Ui;

/// <summary>
/// 主程序启动阶段对后台程序的引导。
/// <para>
/// 由主程序负责“首次拉起后台程序”：
/// <list type="number">
/// <item>读取 <c>commandpark.json</c> 的 <c>hotkey</c>；为 <see langword="null"/> / 缺失时不启用后台。</item>
/// <item>通过程序目录下的 <c>PID</c> 判断后台程序是否已经在运行（进程是否还活着、是否是后台程序）。</item>
/// <item>首次启动（后台未运行）时不弹输入窗口，只拉起后台程序，然后让调用方直接退出。</item>
/// </list>
/// </para>
/// </summary>
internal static class BackgroundLauncher
{
    /// <summary>后台程序的文件名；构建时会复制到主程序旁边。</summary>
    public const string BackgroundProgramFileName = "CommandPark.Background.Cpp.exe";

    /// <summary>后台程序保存自身 PID 的文件名。</summary>
    public const string PidFileName = "PID";

    /// <summary>默认全局快捷键；必须与 CommandPark.Background.Cpp 的 Constants::DefaultHotKey 保持一致。</summary>
    public const string DefaultHotKey = "Ctrl+Alt+D";

    /// <summary>
    /// 在 Avalonia 初始化之前调用。
    /// 返回 <see langword="true"/> 表示“首次启动，只拉起了后台程序”，
    /// 调用方应当直接退出，不要显示输入窗口。
    /// </summary>
    public static bool StartBackgroundIfNeeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // 快捷键为 null / 未配置：不拉起后台程序，双击直接运行。
        if (string.IsNullOrWhiteSpace(ReadConfiguredHotkey()))
        {
            return false;
        }

        var backgroundProgramPath = Path.Combine(AppContext.BaseDirectory, BackgroundProgramFileName);
        if (!File.Exists(backgroundProgramPath))
        {
            // 没带后台程序（例如没有构建 C++ 项目）：照常显示主窗口。
            return false;
        }

        var pidPath = Path.Combine(AppContext.BaseDirectory, PidFileName);
        if (IsBackgroundProcessRunning(pidPath, backgroundProgramPath))
        {
            // 后台已经在运行：这次启动就是“双击运行主程序 / 热键唤起主窗口”。
            return false;
        }

        // 首次启动：只拉起后台程序，不弹输入窗口。
        return TryStartBackgroundProgram(backgroundProgramPath);
    }

    /// <summary>
    /// 读取配置里的快捷键。
    /// <list type="bullet">
    /// <item>没有配置文件 / 没写 <c>hotkey</c>：返回默认值 <see cref="DefaultHotKey"/>。</item>
    /// <item>显式写 <c>null</c> 或空字符串：返回 <see langword="null"/>，表示关闭后台热键。</item>
    /// <item>字符串：返回配置的快捷键。</item>
    /// </list>
    /// </summary>
    private static string? ReadConfiguredHotkey()
    {
        try
        {
            if (ConfigFile.Load() is not JsonObject root || !root.ContainsKey("hotkey"))
            {
                // 没写 hotkey（或没有配置文件）：使用默认快捷键。
                return DefaultHotKey;
            }

            var node = root["hotkey"];
            if (node is null)
            {
                // 显式写 null：关闭后台热键。
                return null;
            }

            var text = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            // 配置损坏不能影响启动：退回默认快捷键。
            return DefaultHotKey;
        }
    }

    /// <summary>判断 PID 文件里的进程是否还在，并且确实是本后台程序。</summary>
    private static bool IsBackgroundProcessRunning(string pidPath, string expectedImagePath)
    {
        var pid = TryReadPid(pidPath);
        if (pid is null)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            if (process.HasExited)
            {
                return false;
            }

            var imagePath = TryGetProcessImagePath(process);
            if (imagePath is not null)
            {
                return string.Equals(
                    Path.GetFullPath(imagePath),
                    Path.GetFullPath(expectedImagePath),
                    StringComparison.OrdinalIgnoreCase);
            }

            // 拿不到完整路径时退回进程名比较。
            var expectedName = Path.GetFileNameWithoutExtension(expectedImagePath);
            return string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 进程不存在 / 没权限访问：都当作后台没在运行。
            return false;
        }
    }

    private static string? TryGetProcessImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadPid(string pidPath)
    {
        try
        {
            if (!File.Exists(pidPath))
            {
                return null;
            }

            var text = File.ReadAllText(pidPath).Trim();
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
                ? pid
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把后台程序拉起来，参数 1 是主程序的完整路径。</summary>
    private static bool TryStartBackgroundProgram(string backgroundProgramPath)
    {
        try
        {
            var mainProgramPath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "CommandPark.Ui.exe");

            var startInfo = new ProcessStartInfo(backgroundProgramPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add(mainProgramPath);

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            // 拉不起后台程序时不要拦住主程序，正常显示窗口。
            return false;
        }
    }
}
