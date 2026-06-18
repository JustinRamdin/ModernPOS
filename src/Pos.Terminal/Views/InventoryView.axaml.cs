using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class InventoryView : UserControl
{
    private InventoryViewModel VM => (InventoryViewModel)DataContext!;

    public InventoryView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, __) =>
        {
            if (DataContext is InventoryViewModel vm)
                await vm.LoadAsync();
        };
    }

    public void New_Click(object? sender, RoutedEventArgs e) => VM.NewItem();

    public async void Save_Click(object? sender, RoutedEventArgs e) => await VM.SaveAsync();

    public async void Delete_Click(object? sender, RoutedEventArgs e) => await VM.DeleteAsync();

    public async void Refresh_Click(object? sender, RoutedEventArgs e) => await VM.LoadAsync();
}
