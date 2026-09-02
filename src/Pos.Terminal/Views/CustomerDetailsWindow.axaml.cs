using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Pos.Contracts;
using Pos.Terminal.ViewModels;
using System.Globalization;

namespace Pos.Terminal.Views;

public partial class CustomerDetailsWindow : Window
{
    public CustomerDetailsWindow()
    {
        InitializeComponent();
        ActivityGrid.AddHandler(InputElement.PointerPressedEvent, ActivityGrid_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private async void Reprint_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomerDetailsViewModel vm)
            await vm.ReprintSelectedReceiptAsync();
    }

    private void ActivityGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CustomerDetailsViewModel vm || !e.GetCurrentPoint(ActivityGrid).Properties.IsRightButtonPressed)
            return;

        var src = e.Source as Control;
        var row = FindAncestor<DataGridRow>(src);

        if (row?.DataContext is not CustomerActivityRow activity || activity.Type != "Receipt")
            return;

        vm.SelectedRow = activity;

        var menu = new ContextMenu();
        var reprint = new MenuItem { Header = "Reprint" };
        var refund = new MenuItem { Header = "Refund" };

        reprint.Click += async (_, __) => await vm.ReprintSelectedReceiptAsync();
        refund.Click += async (_, __) => await ShowRefundDialogAsync(vm, activity);

        menu.Items.Add(reprint);
        menu.Items.Add(refund);
        menu.PlacementTarget = row;
        menu.Open(row);

        e.Handled = true;
    }

    private async Task ShowRefundDialogAsync(CustomerDetailsViewModel vm, CustomerActivityRow receipt)
    {
        if (receipt.RefundableLines.Count == 0)
        {
            vm.Status = "No refundable items remain on this receipt.";
            return;
        }

        var itemPicker = new ComboBox
        {
            ItemsSource = receipt.RefundableLines,
            SelectedIndex = 0,
            MinWidth = 420,
            ItemTemplate = new FuncDataTemplate<SaleLogLineDto>((line, _) =>
                new TextBlock { Text = line?.RefundDisplay ?? string.Empty })
        };
        var quantityBox = new TextBox
        {
            Text = receipt.RefundableLines[0].RemainingQuantity.ToString("0.###", CultureInfo.InvariantCulture),
            Watermark = "Qty",
            MinWidth = 120
        };
        var message = new TextBlock
        {
            Text = $"Receipt {receipt.Note}",
            Opacity = 0.75,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        itemPicker.SelectionChanged += (_, __) =>
        {
            if (itemPicker.SelectedItem is SaleLogLineDto line)
            {
                quantityBox.Text = line.RemainingQuantity.ToString("0.###", CultureInfo.InvariantCulture);
                message.Text = $"{line.RemainingQuantity:0.###} of {line.Qty:0.###} {line.ProductName} remain refundable.";
            }
        };

        var refundButton = new Button { Content = "Refund", IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true };

        var dialog = new Window
        {
            Title = "Refund Item",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Choose item to refund", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    itemPicker,
                    new TextBlock { Text = "Quantity" },
                    quantityBox,
                    message,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, refundButton }
                    }
                }
            }
        };

        refundButton.Click += async (_, __) =>
        {
            if (itemPicker.SelectedItem is not SaleLogLineDto line)
                return;

            if (!decimal.TryParse(quantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
            {
                message.Text = "Enter a valid quantity.";
                return;
            }

            await vm.RefundReceiptLineAsync(receipt, line, quantity);
            dialog.Close();
        };

        cancelButton.Click += (_, __) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private static T? FindAncestor<T>(Control? start) where T : class
    {
        Control? current = start;
        while (current != null)
        {
            if (current is T match) return match;
            current = current.GetVisualParent() as Control;
        }

        return null;
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
