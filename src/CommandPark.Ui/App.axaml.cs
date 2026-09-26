using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CommandPark.Ui.ViewModels;
using CommandPark.Ui.Views;

namespace CommandPark.Ui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyConfiguredTheme();
    }

    private void ApplyConfiguredTheme()
    {
        ThemeMode themeMode;

        try
        {
            themeMode = ThemeConfigReader.Read(ConfigFile.Load());
        }
        catch (Exception)
        {
            // 配置损坏不能影响程序启动；搜索时仍会正常提示配置错误。
            themeMode = ThemeMode.Default;
        }

        RequestedThemeVariant = themeMode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark  => ThemeVariant.Dark,
            _               => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                // DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
