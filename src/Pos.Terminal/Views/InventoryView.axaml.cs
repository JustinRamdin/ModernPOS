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

        // ✅ Keep inventory self-contained and always load when opened
        DataContext = new InventoryViewModel();

        AttachedToVisualTree += async (_, __) => await VM.LoadAsync();
    }

    public void New_Click(object? sender, RoutedEventArgs e) => VM.NewItem();

    public async void Save_Click(object? sender, RoutedEventArgs e) => await VM.SaveAsync();

    public async void Delete_Click(object? sender, RoutedEventArgs e) => await VM.DeleteAsync();

    public async void Refresh_Click(object? sender, RoutedEventArgs e) => await VM.LoadAsync();
}
