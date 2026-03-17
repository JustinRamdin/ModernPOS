using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class FinancialView : UserControl
{
    private FinancialViewModel VM => (FinancialViewModel)DataContext!;

    public FinancialView()
    {
        InitializeComponent();
    }

    private async void OpenQuoteItemsWindow_Click(object? sender, RoutedEventArgs e)
        => await OpenItemsWindowAsync(VM.Quote);

    private async void OpenInvoiceItemsWindow_Click(object? sender, RoutedEventArgs e)
        => await OpenItemsWindowAsync(VM.Invoice);

    private async Task OpenItemsWindowAsync(FinancialDocumentEditorViewModel editor)
    {
        var topLevel = TopLevel.GetTopLevel(this) as Window;
        if (topLevel == null)
            return;

        var window = new FinancialItemsWindow(VM, editor);
        await window.ShowDialog<bool?>(topLevel);
    }

    private async void SaveQuotePdf_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSavePathAsync("quote");
        if (path != null) await VM.SavePdfAsync(VM.Quote, path);
    }

    private async void SaveInvoicePdf_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSavePathAsync("invoice");
        if (path != null) await VM.SavePdfAsync(VM.Invoice, path);
    }

    private void PrintQuote_Click(object? sender, RoutedEventArgs e) => VM.Print(VM.Quote);
    private void PrintInvoice_Click(object? sender, RoutedEventArgs e) => VM.Print(VM.Invoice);

    private async Task<string?> PickSavePathAsync(string docType)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return null;

        var suggested = $"{docType}-{DateTime.Now:yyyyMMdd-HHmm}.pdf";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PDF",
            SuggestedFileName = suggested,
            DefaultExtension = "pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
            ]
        });

        return file?.TryGetLocalPath();
    }
}
