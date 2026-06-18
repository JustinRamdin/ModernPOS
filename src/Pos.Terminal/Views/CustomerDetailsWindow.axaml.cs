using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class CustomerDetailsWindow : Window
{
    public CustomerDetailsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private async void Reprint_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomerDetailsViewModel vm)
            await vm.ReprintSelectedReceiptAsync();
    }
}
