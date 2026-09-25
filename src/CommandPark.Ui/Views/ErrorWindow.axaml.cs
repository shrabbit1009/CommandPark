using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace CommandPark.Ui.Views;

/// <summary>
/// 出错提示窗口。全程只用键盘即可操作：
/// Tab / Shift+Tab 切换焦点，空格或回车展开“显示堆栈跟踪”，Enter 或 ESC 关闭。
/// </summary>
public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();

        // 打开后把焦点放在“确定”上，回车/ESC 可以立即关闭
        Opened += (_, _) => this.FindControl<Button>("OkButton")?.Focus();
    }

    private void Ok_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 已经被子控件处理掉的按键（例如在堆栈跟踪里回车换行）不再拦截
        if (e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;

            case Key.Enter:
                e.Handled = true;
                Close();
                break;
        }
    }
}