using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace CommandPark.Ui;

/// <summary>
/// 窗口图标。
/// <para>
/// 可执行文件的图标由 <c>CommandPark.Ui.csproj</c> 的 <c>ApplicationIcon</c> 决定：
/// 资源管理器、开始菜单、任务管理器，以及后台程序在托盘里显示的那个图标
/// （<c>SHGetFileInfo</c> 从主程序 exe 取）都用它；这里只管窗口自己的图标。
/// </para>
/// <para>
/// 窗口用的是没有背景板的线条版 Logo：白线放到浅色窗口上会看不见，
/// 所以按当前主题在「浅色→黑线 / 深色→白线」之间切换，主题变了跟着换。
/// </para>
/// </summary>
internal static class AppIcons
{
    private const string LightThemeIconUri = "avares://CommandPark.Ui/Assets/Icons/black-line.ico";
    private const string DarkThemeIconUri = "avares://CommandPark.Ui/Assets/Icons/white-line.ico";

    /// <summary>把窗口图标设为当前主题对应的线条 Logo，并在主题切换时同步更新。</summary>
    public static void FollowThemeVariant(this Window window)
    {
        Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window) =>
        window.Icon = LoadIcon(window.ActualThemeVariant == ThemeVariant.Dark);

    private static WindowIcon LoadIcon(bool darkTheme)
    {
        using var stream = AssetLoader.Open(new Uri(darkTheme ? DarkThemeIconUri : LightThemeIconUri));
        return new WindowIcon(new Bitmap(stream));
    }
}
