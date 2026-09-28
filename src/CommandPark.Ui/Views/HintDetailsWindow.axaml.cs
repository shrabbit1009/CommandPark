using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommandPark.Ui.ViewModels;

namespace CommandPark.Ui.Views;

public partial class HintDetailsWindow : Window
{
    public HintDetailsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<Button>("CloseButton")?.Focus();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.D &&
            e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
            DataContext is HintDetailsWindowViewModel { HasAction: true } viewModel &&
            viewModel.RunActionCommand is { } actionCommand)
        {
            e.Handled = true;
            await actionCommand.ExecuteAsync(null);
            return;
        }

        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }
}