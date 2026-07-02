using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

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

    public async void ReprintSaleA4_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await vm.ReprintSelectedSaleAsync(useA4Printer: true);
    }

    public void RefundSale_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        vm.Status = "Select item and quantity, then click Submit Refund.";
    }

    public async void ExportSalesLog_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var from = vm.FromDate ?? DateTime.Today;
        var to = vm.ToDate ?? DateTime.Today;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Sales Report",
            SuggestedFileName = $"sales-report-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }
            ]
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await vm.ExportSalesLogAsync(path);
    }

    public async void OpenReportExportDialog_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm || sender is not Button { Tag: string reportName })
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var storageProvider = owner?.StorageProvider;
        if (owner is null || storageProvider is null)
            return;

        var fromPicker = new DatePicker
        {
            SelectedDate = new DateTimeOffset(vm.FromDate ?? DateTime.Today),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var toPicker = new DatePicker
        {
            SelectedDate = new DateTimeOffset(vm.ToDate ?? DateTime.Today),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var statusText = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var exportButton = new Button
        {
            Classes = { "primary" },
            Content = "Export to Excel",
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var dialog = new Window
        {
            Title = "Export Report",
            Width = 520,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Avalonia.Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = reportName, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                        new TextBlock { Text = "Starting Date", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        fromPicker,
                        new TextBlock { Text = "End Date", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        toPicker,
                        exportButton,
                        statusText
                    }
                }
            }
        };

        exportButton.Click += async (_, _) =>
        {
            var from = fromPicker.SelectedDate?.DateTime ?? DateTime.Today;
            var to = toPicker.SelectedDate?.DateTime ?? DateTime.Today;
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Excel Report",
                SuggestedFileName = $"{BuildExportFileName(reportName)}-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices =
                [
                    new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }
                ]
            });

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            exportButton.IsEnabled = false;
            statusText.Text = "Exporting...";
            await vm.ExportNamedReportAsync(reportName, path, from, to);
            statusText.Text = "Export completed successfully.";
            exportButton.IsEnabled = true;
        };

        await dialog.ShowDialog(owner);
    }

    private static string BuildExportFileName(string reportName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(reportName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return cleaned.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
    }
}
