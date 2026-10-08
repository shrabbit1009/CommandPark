using Avalonia;
using System;
using System.Text;

namespace CommandPark.Ui;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 主程序负责“首次拉起后台程序”：
        // 配置了热键且后台未运行时，这次启动只拉起后台程序，不弹输入窗口，直接退出。
        if (BackgroundLauncher.StartBackgroundIfNeeded())
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

        return AppBuilder.Configure<App>()
                         .UsePlatformDetect()
#if DEBUG
                         .WithDeveloperTools()
#endif
                         .WithInterFont()
                         .LogToTrace();
    }
}
