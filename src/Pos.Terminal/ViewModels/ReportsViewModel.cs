using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Contracts;
using Pos.Terminal.Commands;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class ReportsViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public DateTime? FromDate { get; set; } = DateTime.Today.AddDays(-6);
    public DateTime? ToDate { get; set; } = DateTime.Today;
    public string DateRangeLabel => $"Apply {(FromDate ?? DateTime.Today):MMM d} – {(ToDate ?? DateTime.Today):MMM d}";
    public string LocationCode { get; set; } = "DEFAULT";
    public int LookbackDays { get; set; } = 14;
    public int ReceiptCount { get; private set; }
    public decimal NetTotal { get; private set; }
    public decimal VatTotal { get; private set; }
    public decimal GrossTotal { get; private set; }
    public decimal AvgGross { get; private set; }
    public decimal SalesGross { get; private set; }
    public decimal Cogs { get; private set; }
    public decimal GrossProfit { get; private set; }
    public decimal GrossMarginPct { get; private set; }
    public decimal CashTotal { get; private set; }
    public decimal DebitTotal { get; private set; }
    public decimal CreditTotal { get; private set; }
    public decimal OnAccountTotal { get; private set; }
    public decimal ChangeGiven { get; private set; }
    public decimal ExpectedCash { get; private set; }

    public ObservableCollection<SalesByDayRowDto> SalesByDay { get; } = new();
    public ObservableCollection<TopProductRowDto> TopProducts { get; } = new();
    public ObservableCollection<ProfitByProductRowDto> ProfitByProduct { get; } = new();
    public ObservableCollection<InventoryValuationRowDto> InventoryValuation { get; } = new();
    public ObservableCollection<InventoryMovementRowDto> InventoryMovements { get; } = new();
    public ObservableCollection<LowStockRowDto> LowStock { get; } = new();
    public ObservableCollection<CustomerSalesRowDto> CustomerSales { get; } = new();
    public ObservableCollection<ExportTemplateDefinition> ExportTemplates { get; } = new();
    public ObservableCollection<SaleLogEntryDto> SalesLog { get; } = new();
    public ObservableCollection<SaleLogLineDto> SelectedSaleLines { get; } = new();
    private SaleLogEntryDto? _selectedSale;
    public SaleLogEntryDto? SelectedSale { get => _selectedSale; set { _selectedSale = value; SelectedSaleLines.Clear(); if (value is not null) foreach (var l in value.Lines) SelectedSaleLines.Add(l); OnPropertyChanged(); } }
    public string SalesSearchText { get; set; } = string.Empty;
    public SaleLogLineDto? SelectedRefundLine { get; set; }
    public decimal RefundQuantity { get; set; } = 1;

    public ICommand RefreshAllCommand { get; }
    public ICommand ApplyDateRangeCommand { get; }
    public ICommand RefreshInventoryCommand { get; }
    public ICommand RefreshLowStockCommand { get; }

    public ReportsViewModel()
    {

        RefreshAllCommand = new AsyncRelayCommand(async _ => await LoadAllAsync());
        ApplyDateRangeCommand = new AsyncRelayCommand(async _ => await LoadAllAsync());
        RefreshInventoryCommand = new AsyncRelayCommand(async _ => await LoadInventoryAsync());
        RefreshLowStockCommand = new AsyncRelayCommand(async _ => await LoadLowStockAsync());
        ExportTemplates.Add(new ExportTemplateDefinition("Sales register", "Receipts with totals, customer, payment type, and status.", ExportTemplateKind.Sales));
        ExportTemplates.Add(new ExportTemplateDefinition("Financial summary", "High-level financial totals including sales, COGS, gross profit, and margin.", ExportTemplateKind.FinancialSummary));
        ExportTemplates.Add(new ExportTemplateDefinition("COGS & profitability by product", "Per-product sales, COGS, profit, and gross margin percent.", ExportTemplateKind.Profit));
        ExportTemplates.Add(new ExportTemplateDefinition("Top selling products", "Best-selling products by quantity and gross sales.", ExportTemplateKind.TopProducts));
        ExportTemplates.Add(new ExportTemplateDefinition("Customer sales", "Customer-level receipts, gross totals, and balances.", ExportTemplateKind.Customers));
    }

    public async Task LoadAllAsync()
    {
        try
        {
            Status = "Loading reports...";
            var (fromUtc, toUtc) = GetUtcRange();
using var api = await CreateApiAsync();
            var report = await api.GetReportSummaryAsync(fromUtc, toUtc);

            ReceiptCount = report.ReceiptCount; GrossTotal = report.GrossTotal; NetTotal = report.GrossTotal; VatTotal = 0m;
            AvgGross = ReceiptCount == 0 ? 0m : GrossTotal / ReceiptCount;
            SalesGross = report.SalesGross; Cogs = report.Cogs; GrossProfit = report.GrossProfit;
            GrossMarginPct = SalesGross <= 0 ? 0 : GrossProfit / SalesGross * 100m;

            SalesByDay.Clear(); foreach (var r in report.SalesByDay) SalesByDay.Add(r);
            TopProducts.Clear(); foreach (var r in report.TopProducts) TopProducts.Add(r);
            ProfitByProduct.Clear(); foreach (var r in report.ProfitByProduct) ProfitByProduct.Add(r);
            CustomerSales.Clear(); foreach (var r in report.CustomerSales) CustomerSales.Add(r);
            InventoryValuation.Clear(); foreach (var r in report.InventoryValuation) InventoryValuation.Add(r);
            var movements = await api.GetInventoryMovementsAsync(fromUtc, toUtc, LocationCode);
            InventoryMovements.Clear(); foreach (var r in movements) InventoryMovements.Add(r);
            var salesLog = await api.GetSalesLogAsync(fromUtc, toUtc);
            SalesLog.Clear();
            foreach (var sale in salesLog.Where(s => string.IsNullOrWhiteSpace(SalesSearchText) || s.ReceiptNo.Contains(SalesSearchText, StringComparison.OrdinalIgnoreCase) || s.Lines.Any(l => l.ProductName.Contains(SalesSearchText, StringComparison.OrdinalIgnoreCase))))
                SalesLog.Add(sale);

            Status = report.ReceiptCount == 0 && report.InventoryValuation.Count == 0 && report.CustomerSales.Count == 0
                ? "No data available from server."
                : "Reports loaded.";
            NotifyAll();
        }
        catch (Exception ex) { Status = $"Reports failed: {ex.Message}"; }
    }

    public Task LoadInventoryAsync() => LoadAllAsync();
    public async Task LoadLowStockAsync()
    {
        try
        {
            using var api = await CreateApiAsync();
            var rows = await api.GetLowStockAsync(LocationCode, LookbackDays);
            LowStock.Clear();
            foreach (var row in rows) LowStock.Add(row);
            Status = rows.Count == 0 ? "No low-stock items from server." : "Low stock loaded.";
        }
        catch (Exception ex) { Status = $"Low stock failed: {ex.Message}"; }
    }

    private (DateTime fromUtc, DateTime toUtc) GetUtcRange()
    {
        var tz = TimeZoneInfo.Local;
        var fromDate = (FromDate ?? DateTime.Today).Date;
        var toDate = (ToDate ?? DateTime.Today).Date;
         if (toDate < fromDate)
            (fromDate, toDate) = (toDate, fromDate);

        var startLocal = DateTime.SpecifyKind(fromDate, DateTimeKind.Unspecified);
        var endLocalExclusive = DateTime.SpecifyKind(toDate.AddDays(1), DateTimeKind.Unspecified);

        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, tz),
            TimeZoneInfo.ConvertTimeToUtc(endLocalExclusive, tz));
    }

    private static async Task<RemoteServerApi> CreateApiAsync() { var d = await new SettingsStore().LoadDeploymentAsync(); return new RemoteServerApi(d.ServerHost, d.ServerPort, d.AuthToken); }
    public async Task RefundSelectedLineAsync()
    {
        if (SelectedSale is null || SelectedRefundLine is null || RefundQuantity <= 0) return;
        using var api = await CreateApiAsync();
        await api.RefundSaleItemAsync(SelectedSale.SaleId, SelectedRefundLine.SaleLineId, RefundQuantity);
        Status = $"Refund posted for {SelectedRefundLine.ProductName}.";
        await LoadAllAsync();
    }

    public async Task ReprintSelectedSaleAsync()
    {
        if (SelectedSale is null)
            return;

        var settings = await new SettingsStore().LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
        {
            Status = "No receipt printer configured.";
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Status = "Receipt printing is only supported on Windows.";
            return;
        }

#pragma warning disable CA1416
        var state = PhysicalReceiptRenderer.CreateState(
            SelectedSale.Lines.Select(line => new PhysicalReceiptRenderer.ReceiptRenderLine(
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
                return;
            }

            using var doc = new PrintDocument
            {
                PrinterSettings = printerSettings,
                DocumentName = $"Invoice {SelectedSale.ReceiptNo}"
            };
            doc.DefaultPageSettings.Margins = new Margins(3, 5, 25, 25);

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
                        receiptNo: SelectedSale.ReceiptNo,
                        invoiceDate: SelectedSale.SoldAtUtc.ToLocalTime(),
                        customer: new PhysicalReceiptRenderer.ReceiptCustomerInfo(null, null, null),
                        paymentMethod: SelectedSale.PaymentType,
                        subtotal: SelectedSale.Subtotal,
                        discount: 0m,
                        vat: 0m,
                        totalDue: SelectedSale.Total,
                        totalTendered: SelectedSale.Total,
                        change: 0m,
                        state: state);
                    e.HasMorePages = false;
                    return;
                }

                e.HasMorePages = PhysicalReceiptRenderer.DrawInvoiceLetterPage(
                    g: e.Graphics,
                    marginBounds: e.MarginBounds,
                    companyProfile: companyProfile,
                    receiptNo: SelectedSale.ReceiptNo,
                    invoiceDate: SelectedSale.SoldAtUtc.ToLocalTime(),
                    customer: new PhysicalReceiptRenderer.ReceiptCustomerInfo(null, null, null),
                    paymentMethod: SelectedSale.PaymentType,
                    subtotal: SelectedSale.Subtotal,
                    discount: 0m,
                    vat: 0m,
                    totalDue: SelectedSale.Total,
                    totalTendered: SelectedSale.Total,
                    change: 0m,
                    state: state);
            };
#pragma warning restore CA1416
            doc.Print();
            Status = $"Receipt {SelectedSale.ReceiptNo} sent to {settings.ReceiptPrinterName}.";
        }
        catch (Exception ex)
        {
            Status = $"Reprint failed: {ex.Message}";
        }
    }

    private void NotifyAll() { foreach (var n in new[] { nameof(ReceiptCount), nameof(NetTotal), nameof(VatTotal), nameof(GrossTotal), nameof(AvgGross), nameof(SalesGross), nameof(Cogs), nameof(GrossProfit), nameof(GrossMarginPct), nameof(SelectedSale), nameof(SelectedRefundLine), nameof(RefundQuantity) }) OnPropertyChanged(n); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

