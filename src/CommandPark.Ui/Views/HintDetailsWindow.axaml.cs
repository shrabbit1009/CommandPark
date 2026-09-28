using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace CommandPark.Ui.Views;

public partial class HintDetailsWindow : Window
{
    public HintDetailsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<Button>("CloseButton")?.Focus();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }
}