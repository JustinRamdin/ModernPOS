using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    public async void SubmitRefund_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await vm.RefundSelectedLineAsync();
    }

    public async void ReprintSale_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await vm.ReprintSelectedSaleAsync();
    }

    public void RefundSale_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        vm.Status = "Select item and quantity, then click Submit Refund.";
    }
}
