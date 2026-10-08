using Avalonia;
using System;
using System.Linq;
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
        // --no-display：自启动模式，首次拉起后台程序后不弹“已在后台启动”提示。
        var noDisplay = args.Any(static arg =>
            string.Equals(arg, "--no-display", StringComparison.OrdinalIgnoreCase));

        // 主程序负责“首次拉起后台程序”：
        // 配置了热键且后台未运行时，这次启动只拉起后台程序，并按需提示，然后直接退出。
        if (BackgroundLauncher.StartBackgroundIfNeeded(noDisplay))
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
