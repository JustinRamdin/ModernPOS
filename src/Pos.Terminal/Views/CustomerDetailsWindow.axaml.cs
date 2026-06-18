using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomerDetailsViewModel vm)
            return;

        var storageProvider = StorageProvider;
        if (storageProvider is null)
            return;

        var from = vm.FromDate ?? DateTime.Today;
        var to = vm.ToDate ?? DateTime.Today;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Customer Details",
            SuggestedFileName = $"{vm.ExportFileNameBase}-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }
            ]
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.ExportCurrentRows(path);
    }
}
