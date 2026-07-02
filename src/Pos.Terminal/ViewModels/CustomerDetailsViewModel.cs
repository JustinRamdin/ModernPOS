using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClosedXML.Excel;
using Pos.Contracts;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class CustomerDetailsViewModel : INotifyPropertyChanged
{
    private readonly Guid _customerId;
    private List<CustomerActivityRow> _allRows = [];

    public CustomerDetailsViewModel(Guid customerId, string customerName)
    {
        _customerId = customerId;
        CustomerName = customerName;
        FromDate = DateTime.Today.AddMonths(-1);
        ToDate = DateTime.Today;
        FilterOptions = ["All", "Receipts", "Payments"];
        SelectedFilter = "All";
        ApplyFilterCommand = new VmRelayCommand(_ => ApplyFilter());
        LoadCommand = new VmRelayCommand(async _ => await LoadAsync());
    }

    public string CustomerName { get; }
    public string Title => $"{CustomerName} Details";
    public string ExportFileNameBase => $"customer-details-{SanitizeFileName(CustomerName)}";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public IReadOnlyList<string> FilterOptions { get; }
    public string SelectedFilter { get; set; }
    public CustomerActivityRow? SelectedRow { get; set; }
    public ObservableCollection<CustomerActivityRow> Rows { get; } = new();
    public string Status { get; set; } = "";
    public string TotalReceiptsLabel => $"Receipts: {Rows.Where(x => x.Type == "Receipt").Sum(x => x.Amount):0.00}";
    public string TotalPaymentsLabel => $"Payments: {Rows.Where(x => x.Type == "Payment").Sum(x => x.Amount):0.00}";
    public string RemainingLabel => $"Remaining: {Rows.Where(x => x.Type == "Receipt").Sum(x => x.Amount) - Rows.Where(x => x.Type == "Payment").Sum(x => x.Amount):0.00}";
    public ICommand ApplyFilterCommand { get; }
    public ICommand LoadCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            Status = "Loading...";
            Raise(nameof(Status));

            var from = (FromDate ?? DateTime.Today).Date;
            var to = (ToDate ?? DateTime.Today).Date;
            if (to < from)
                (from, to) = (to, from);

            var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
            var toUtc = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            using var api = await CreateApiAsync();
            _allRows = (await api.GetCustomerActivityAsync(_customerId, fromUtc, toUtc))
                .Select(x => new CustomerActivityRow(
                    x.OccurredAtUtc.ToLocalTime(),
                    x.Type,
                    x.Method,
                    x.Amount,
                    x.Note,
                    x.Details,
                    x.Subtotal,
                    x.Lines ?? []))
                .OrderByDescending(x => x.OccurredAt)
                .ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            Rows.Clear();
            Status = $"Error: {ex.Message}";
            Raise(nameof(Status));
            RaiseTotals();
        }
    }

    private void ApplyFilter()
    {
        var filtered = SelectedFilter switch
        {
            "Receipts" => _allRows.Where(x => x.Type == "Receipt"),
            "Payments" => _allRows.Where(x => x.Type == "Payment"),
            _ => _allRows
        };

        Rows.Clear();
        foreach (var row in filtered)
            Rows.Add(row);

        Status = Rows.Count == 0 ? "No activity for this date range." : $"{Rows.Count} item(s)";
        Raise(nameof(Status));
        RaiseTotals();
    }

    private void RaiseTotals()
    {
        Raise(nameof(TotalReceiptsLabel));
        Raise(nameof(TotalPaymentsLabel));
        Raise(nameof(RemainingLabel));
    }

    public async Task ReprintSelectedReceiptAsync()
    {
        if (SelectedRow is not { Type: "Receipt" } receipt)
        {
            Status = "Select a receipt to reprint.";
            Raise(nameof(Status));
            return;
        }

        var settings = await new SettingsStore().LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
        {
            Status = "No receipt printer configured.";
            Raise(nameof(Status));
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Status = "Receipt printing is only supported on Windows.";
            Raise(nameof(Status));
            return;
        }

#pragma warning disable CA1416
        var state = PhysicalReceiptRenderer.CreateState(
            receipt.Lines.Select(line => new PhysicalReceiptRenderer.ReceiptRenderLine(
                line.ProductName,
                IsLength: false,
                Qty: line.Qty,
                QtyInches: 0,
                UnitPrice: line.UnitPrice,
                LineTotal: line.LineTotal)));
#pragma warning restore CA1416

        try
        {
            using var api = await CreateApiAsync();
            var companyProfile = await api.GetCompanyProfileAsync();

            var printerSettings = new PrinterSettings
            {
                PrinterName = settings.ReceiptPrinterName
            };

            if (!printerSettings.IsValid)
            {
                Status = $"Printer not found: {settings.ReceiptPrinterName}";
                Raise(nameof(Status));
                return;
            }

            using var doc = new PrintDocument
            {
                PrinterSettings = printerSettings,
                DocumentName = $"Invoice {receipt.Note}"
            };
            doc.DefaultPageSettings.Margins = settings.UseTspReceiptStyle
                ? new Margins(3, 5, 25, 25)
                : new Margins(25, 60, 25, 25);

#pragma warning disable CA1416
            doc.PrintPage += (_, e) =>
            {
                if (e.Graphics is null)
                {
                    e.HasMorePages = false;
                    return;
                }

                if (settings.UseTspReceiptStyle)
                {
                    PhysicalReceiptRenderer.DrawInvoiceTspPage(
                        g: e.Graphics,
                        marginBounds: e.MarginBounds,
                        companyProfile: companyProfile,
                        receiptNo: receipt.Note,
                        invoiceDate: receipt.OccurredAt,
                        customer: new PhysicalReceiptRenderer.ReceiptCustomerInfo(CustomerName, string.Empty, string.Empty),
                        paymentMethod: receipt.Method,
                        subtotal: receipt.Subtotal,
                        discount: 0m,
                        vat: 0m,
                        zeroRated: 0m,
                        totalDue: receipt.Amount,
                        totalTendered: receipt.Amount,
                        change: 0m,
                        state: state);
                    e.HasMorePages = false;
                    return;
                }

                e.HasMorePages = PhysicalReceiptRenderer.DrawInvoiceLetterPage(
                    g: e.Graphics,
                    marginBounds: e.MarginBounds,
                    companyProfile: companyProfile,
                    receiptNo: receipt.Note,
                    invoiceDate: receipt.OccurredAt,
                    customer: new PhysicalReceiptRenderer.ReceiptCustomerInfo(CustomerName, string.Empty, string.Empty),
                    paymentMethod: receipt.Method,
                    subtotal: receipt.Subtotal,
                    discount: 0m,
                    vat: 0m,
                    zeroRated: 0m,
                    totalDue: receipt.Amount,
                    totalTendered: receipt.Amount,
                    change: 0m,
                    state: state);
            };
#pragma warning restore CA1416

            doc.Print();
            Status = $"Receipt {receipt.Note} sent to {settings.ReceiptPrinterName}.";
        }
        catch (Exception ex)
        {
            Status = $"Reprint failed: {ex.Message}";
        }

        Raise(nameof(Status));
    }

    public void ExportCurrentRows(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var exportPath = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}.xlsx";

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Customer Details");

        sheet.Cell("A1").Value = CustomerName;
        sheet.Range("A1:F1").Merge();
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 18;
        sheet.Cell("A2").Value = $"Period: {(FromDate ?? DateTime.Today):yyyy-MM-dd} to {(ToDate ?? DateTime.Today):yyyy-MM-dd}";
        sheet.Range("A2:F2").Merge();
        sheet.Cell("A3").Value = $"Filter: {SelectedFilter}";
        sheet.Range("A3:F3").Merge();

        var headers = new[] { "Date", "Type", "Method", "Amount", "Notes", "Details" };
        const int headerRow = 5;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        var row = headerRow + 1;
        foreach (var item in Rows)
        {
            sheet.Cell(row, 1).Value = item.OccurredAt;
            sheet.Cell(row, 2).Value = item.Type;
            sheet.Cell(row, 3).Value = item.Method;
            sheet.Cell(row, 4).Value = item.Amount;
            sheet.Cell(row, 5).Value = item.Note;
            sheet.Cell(row, 6).Value = item.Details;
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 1).Value = "Total";
        sheet.Cell(totalRow, 4).FormulaA1 = Rows.Count == 0
            ? "0"
            : $"SUM({sheet.Cell(headerRow + 1, 4).Address}:{sheet.Cell(totalRow - 1, 4).Address})";

        var usedRange = sheet.Range(headerRow, 1, totalRow, headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        sheet.Range(headerRow, 1, headerRow, headers.Length).Style.Font.Bold = true;
        sheet.Range(headerRow, 1, headerRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Font.Bold = true;
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");

        sheet.Column(1).Style.DateFormat.Format = "yyyy-mm-dd h:mm AM/PM";
        sheet.Column(4).Style.NumberFormat.Format = "$#,##0.00";
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.ColumnsUsed())
            column.Width = Math.Min(column.Width, 70);
        sheet.CellsUsed().Style.Alignment.WrapText = true;

        workbook.SaveAs(exportPath);
        Status = $"Customer details exported: {exportPath}";
        Raise(nameof(Status));
    }

    private static async Task<RemoteServerApi> CreateApiAsync()
    {
        var d = await new SettingsStore().LoadDeploymentAsync();
        return new RemoteServerApi(d.ServerHost, d.ServerPort, d.AuthToken);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned)
            ? DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)
            : cleaned.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record CustomerActivityRow(
    DateTime OccurredAt,
    string Type,
    string Method,
    decimal Amount,
    string Note,
    string Details,
    decimal Subtotal,
    IReadOnlyList<SaleLogLineDto> Lines);
