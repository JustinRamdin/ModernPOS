using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class FinancialView : UserControl
{
    private FinancialViewModel VM => (FinancialViewModel)DataContext!;

    public FinancialView()
    {
        InitializeComponent();
    }

    private void AddQuoteItem_Click(object? sender, RoutedEventArgs e) => VM.AddLine(VM.Quote);
    private void RemoveQuoteItem_Click(object? sender, RoutedEventArgs e) => VM.RemoveSelectedLine(VM.Quote);
    private void AddInvoiceItem_Click(object? sender, RoutedEventArgs e) => VM.AddLine(VM.Invoice);
    private void RemoveInvoiceItem_Click(object? sender, RoutedEventArgs e) => VM.RemoveSelectedLine(VM.Invoice);

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
