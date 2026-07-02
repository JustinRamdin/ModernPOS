using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClosedXML.Excel;
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
    public ObservableCollection<SaleLogEntryDto> SalesLog { get; } = new();
    public ObservableCollection<SaleLogLineDto> SelectedSaleLines { get; } = new();
    public ObservableCollection<ReportExportOption> ExportReports { get; } = new(new[]
    {
        new ReportExportOption("Daily Sales Report"),
        new ReportExportOption("Sales by Date Range Report"),
        new ReportExportOption("Product Sales Report"),
        new ReportExportOption("Top Selling Products Report"),
        new ReportExportOption("Current Stock on Hand Report"),
        new ReportExportOption("Low Stock/Reorder Report"),
        new ReportExportOption("Inventory Valuation Report"),
        new ReportExportOption("Returns and Refunds Report"),
        new ReportExportOption("Discount Report"),
        new ReportExportOption("VAT/GST Sales Tax Report"),
        new ReportExportOption("End-of-Day (Z) Report"),
        new ReportExportOption("Cash Drawer Report"),
        new ReportExportOption("Sales by Cashier Report"),
        new ReportExportOption("Customer Purchase History Report"),
        new ReportExportOption("Customer Outstanding Balances Report"),
        new ReportExportOption("Customer Payments and Receivables Report"),
        new ReportExportOption("Purchase History/Receiving Report"),
        new ReportExportOption("Stock Adjustment Report"),
        new ReportExportOption("Transaction Audit Report")
    });
    private SaleLogEntryDto? _selectedSale;
    public SaleLogEntryDto? SelectedSale { get => _selectedSale; set { _selectedSale = value; SelectedRefundLine = null; SelectedSaleLines.Clear(); if (value is not null) foreach (var l in value.Lines) SelectedSaleLines.Add(l); OnPropertyChanged(); } }
    public string SalesSearchText { get; set; } = string.Empty;
    private SaleLogLineDto? _selectedRefundLine;
    public SaleLogLineDto? SelectedRefundLine
    {
        get => _selectedRefundLine;
        set
        {
            if (ReferenceEquals(_selectedRefundLine, value)) return;
            _selectedRefundLine = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RefundRemainingMessage));
        }
    }
    public string RefundRemainingMessage => SelectedRefundLine is null
        ? string.Empty
        : $"{SelectedRefundLine.RemainingQuantity:0.###} of {SelectedRefundLine.Qty:0.###} {SelectedRefundLine.ProductName} remain refundable.";
    public decimal RefundQuantity { get; set; } = 1;

    public ICommand RefreshAllCommand { get; }
    public ICommand ApplyDateRangeCommand { get; }
    public ICommand RefreshInventoryCommand { get; }
    public ICommand RefreshLowStockCommand { get; }

    public int? InventoryBucket { get; }
    public string ReportsTitle => InventoryBucket is null ? "Reports" : $"Reports {InventoryBucket}";

    public ReportsViewModel(int? inventoryBucket = null)
    {
        InventoryBucket = inventoryBucket is null ? null : Math.Clamp(inventoryBucket.Value, 1, 2);

        RefreshAllCommand = new AsyncRelayCommand(async _ => await LoadAllAsync());
        ApplyDateRangeCommand = new AsyncRelayCommand(async _ => await LoadAllAsync());
        RefreshInventoryCommand = new AsyncRelayCommand(async _ => await LoadInventoryAsync());
        RefreshLowStockCommand = new AsyncRelayCommand(async _ => await LoadLowStockAsync());
    }

    public async Task LoadAllAsync()
    {
        try
        {
            Status = "Loading reports...";
            var (fromUtc, toUtc) = GetUtcRange();
using var api = await CreateApiAsync();
            var report = await api.GetReportSummaryAsync(fromUtc, toUtc, InventoryBucket);

            ReceiptCount = report.ReceiptCount; GrossTotal = report.GrossTotal; NetTotal = report.GrossTotal; VatTotal = 0m;
            AvgGross = ReceiptCount == 0 ? 0m : GrossTotal / ReceiptCount;
            SalesGross = report.SalesGross; Cogs = report.Cogs; GrossProfit = report.GrossProfit;
            GrossMarginPct = SalesGross <= 0 ? 0 : GrossProfit / SalesGross * 100m;

            SalesByDay.Clear(); foreach (var r in report.SalesByDay) SalesByDay.Add(r);
            TopProducts.Clear(); foreach (var r in report.TopProducts) TopProducts.Add(r);
            ProfitByProduct.Clear(); foreach (var r in report.ProfitByProduct) ProfitByProduct.Add(r);
            CustomerSales.Clear(); foreach (var r in report.CustomerSales) CustomerSales.Add(r);
            InventoryValuation.Clear(); foreach (var r in report.InventoryValuation) InventoryValuation.Add(r);
            var movements = await api.GetInventoryMovementsAsync(fromUtc, toUtc, LocationCode, InventoryBucket);
            InventoryMovements.Clear(); foreach (var r in movements) InventoryMovements.Add(r);
            var salesLog = await api.GetSalesLogAsync(fromUtc, toUtc, InventoryBucket);
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
            var rows = await api.GetLowStockAsync(LocationCode, LookbackDays, InventoryBucket);
            LowStock.Clear();
            foreach (var row in rows) LowStock.Add(row);
            Status = rows.Count == 0 ? "No low-stock items from server." : "Low stock loaded.";
        }
        catch (Exception ex) { Status = $"Low stock failed: {ex.Message}"; }
    }

    private (DateTime fromUtc, DateTime toUtc) GetUtcRange()
        => GetUtcRange(FromDate, ToDate);

    private static (DateTime fromUtc, DateTime toUtc) GetUtcRange(DateTime? from, DateTime? to)
    {
        var tz = TimeZoneInfo.Local;
        var fromDate = (from ?? DateTime.Today).Date;
        var toDate = (to ?? DateTime.Today).Date;
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
        if (RefundQuantity > SelectedRefundLine.RemainingQuantity)
        {
            Status = $"Only {SelectedRefundLine.RemainingQuantity:0.###} of {SelectedRefundLine.ProductName} remains refundable.";
            return;
        }
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
                        vat: SelectedSale.VatTotal,
                        zeroRated: 0m,
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
                    vat: SelectedSale.VatTotal,
                    zeroRated: 0m,
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

    public async Task ExportSalesLogAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        await LoadAllAsync();

        var exportPath = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}.xlsx";

        var settings = await new SettingsStore().LoadAsync();
        var sales = SalesLog.OrderBy(s => s.SoldAtUtc).ThenBy(s => s.ReceiptNo).ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sales Report");

        sheet.Cell("A1").Value = "Sales Report";
        sheet.Range("A1:I1").Merge();
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 18;

        sheet.Cell("A2").Value = $"Period: {(FromDate ?? DateTime.Today):yyyy-MM-dd} to {(ToDate ?? DateTime.Today):yyyy-MM-dd}";
        sheet.Range("A2:I2").Merge();

        var headerRow = 4;
        var headers = new[] { "Sale Date", "Sale Time", "Receipt No", "Payment Type", "Items", "Subtotal", "VAT", "Discount", "Total" };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        var row = headerRow + 1;
        foreach (var sale in sales)
        {
            var soldAt = sale.SoldAtUtc.ToLocalTime();
            var discount = CalculateDiscountAmount(sale);
            var vat = CalculateVatAmount(sale.Total, settings.IsVatEnabled, settings.VatRatePercent);

            sheet.Cell(row, 1).Value = soldAt.Date;
            sheet.Cell(row, 2).Value = soldAt;
            sheet.Cell(row, 3).Value = sale.ReceiptNo;
            sheet.Cell(row, 4).Value = sale.PaymentType;
            sheet.Cell(row, 5).Value = string.Join("; ", sale.Lines.Select(line => $"{line.ProductName} x {line.Qty.ToString("0.##", CultureInfo.InvariantCulture)}"));
            sheet.Cell(row, 6).Value = sale.Subtotal;
            sheet.Cell(row, 7).Value = vat;
            sheet.Cell(row, 8).Value = discount;
            sheet.Cell(row, 9).Value = sale.Total;
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 5).Value = "Total";
        if (sales.Count > 0)
        {
            sheet.Cell(totalRow, 6).FormulaA1 = $"SUM(F{headerRow + 1}:F{totalRow - 1})";
            sheet.Cell(totalRow, 7).FormulaA1 = $"SUM(G{headerRow + 1}:G{totalRow - 1})";
            sheet.Cell(totalRow, 8).FormulaA1 = $"SUM(H{headerRow + 1}:H{totalRow - 1})";
            sheet.Cell(totalRow, 9).FormulaA1 = $"SUM(I{headerRow + 1}:I{totalRow - 1})";
        }
        else
        {
            sheet.Range(totalRow, 6, totalRow, 9).Value = 0m;
        }

        var usedRange = sheet.Range(headerRow, 1, totalRow, headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var headerRange = sheet.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");

        var totalRange = sheet.Range(totalRow, 1, totalRow, headers.Length);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");

        sheet.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Column(2).Style.DateFormat.Format = "h:mm AM/PM";
        sheet.Range(headerRow + 1, 6, totalRow, 9).Style.NumberFormat.Format = "$#,##0.00";
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();
        sheet.Column(5).Width = Math.Min(sheet.Column(5).Width, 70);
        sheet.Column(5).Style.Alignment.WrapText = true;

        workbook.SaveAs(exportPath);
        Status = $"Sales report exported: {exportPath}";
    }

    public async Task ExportNamedReportAsync(string reportName, string path, DateTime? fromDate, DateTime? toDate)
    {
        if (string.IsNullOrWhiteSpace(reportName) || string.IsNullOrWhiteSpace(path))
            return;

        var exportPath = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}.xlsx";

        var from = (fromDate ?? DateTime.Today).Date;
        var to = (toDate ?? DateTime.Today).Date;
        if (to < from)
            (from, to) = (to, from);

        var (fromUtc, toUtc) = GetUtcRange(from, to);
        using var api = await CreateApiAsync();
        var settings = await new SettingsStore().LoadAsync();
        var report = await api.GetReportSummaryAsync(fromUtc, toUtc, InventoryBucket);
        var salesLog = (await api.GetSalesLogAsync(fromUtc, toUtc, InventoryBucket)).OrderBy(s => s.SoldAtUtc).ThenBy(s => s.ReceiptNo).ToList();
        var movements = await api.GetInventoryMovementsAsync(fromUtc, toUtc, LocationCode, InventoryBucket);
        var lowStock = await api.GetLowStockAsync(LocationCode, LookbackDays, InventoryBucket);
        var customerReceivables = await api.GetCustomerReceivablesAsync(fromUtc, toUtc);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(reportName));
        BuildSelectedReportSheet(sheet, reportName, from, to, settings.IsVatEnabled, settings.VatRatePercent, report, salesLog, movements, lowStock, customerReceivables);
        workbook.SaveAs(exportPath);
        Status = $"{reportName} exported: {exportPath}";
    }

    private static void BuildSelectedReportSheet(
        IXLWorksheet sheet,
        string reportName,
        DateTime from,
        DateTime to,
        bool isVatEnabled,
        decimal vatRatePercent,
        ReportSummaryDto report,
        IReadOnlyList<SaleLogEntryDto> salesLog,
        IReadOnlyList<InventoryMovementRowDto> movements,
        IReadOnlyList<LowStockRowDto> lowStock,
        IReadOnlyList<CustomerReceivablesRowDto> customerReceivables)
    {
        var rows = new List<object?[]>();
        string[] headers;
        int[] totalColumns;

        switch (reportName)
        {
            case "Daily Sales Report":
                headers = ["Date", "Receipts", "Gross Sales"];
                rows.AddRange(report.SalesByDay.OrderBy(x => x.Day).Select(x => new object?[] { x.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.ReceiptCount, x.GrossTotal }));
                totalColumns = [3];
                break;
            case "Product Sales Report":
                headers = ["SKU", "Product", "Qty Sold", "Revenue", "COGS", "Profit", "Margin %"];
                rows.AddRange(report.ProfitByProduct.OrderBy(x => x.Name).Select(x => new object?[] { x.Sku, x.Name, x.QuantityDisplay, x.Revenue, x.Cogs, x.Profit, x.GrossMarginPct }));
                totalColumns = [4, 5, 6];
                break;
            case "Top Selling Products Report":
                headers = ["SKU", "Product", "Qty Sold", "Gross Sales"];
                rows.AddRange(report.TopProducts.OrderByDescending(x => x.GrossTotal).Select(x => new object?[] { x.Sku, x.Name, x.QuantityDisplay, x.GrossTotal }));
                totalColumns = [4];
                break;
            case "Current Stock on Hand Report":
                headers = ["SKU", "Product", "On Hand"];
                rows.AddRange(report.InventoryValuation.OrderBy(x => x.Name).Select(x => new object?[] { x.Sku, x.Name, x.OnHandDisplay }));
                totalColumns = [];
                break;
            case "Low Stock/Reorder Report":
                headers = ["SKU", "Product", "On Hand", "Avg/Day", "Days Left", "Suggested Reorder"];
                rows.AddRange(lowStock.OrderBy(x => x.Name).Select(x => new object?[] { x.Sku, x.Name, x.OnHandDisplay, x.AvgDailyUsageBase, x.DaysRemaining, x.SuggestedReorderBase }));
                totalColumns = [6];
                break;
            case "Inventory Valuation Report":
                headers = ["SKU", "Product", "On Hand", "Selling Value", "Cost Value", "Estimated Margin"];
                rows.AddRange(report.InventoryValuation.OrderBy(x => x.Name).Select(x => new object?[] { x.Sku, x.Name, x.OnHandDisplay, x.SellingValue, x.CostValue, x.EstimatedGrossMargin }));
                totalColumns = [4, 5, 6];
                break;
            case "Returns and Refunds Report":
                headers = ["Date", "Receipt No", "Payment Type", "Items", "Refund Total"];
                rows.AddRange(salesLog.Where(x => x.Total < 0m).Select(x => new object?[] { x.SoldAtUtc.ToLocalTime(), x.ReceiptNo, x.PaymentType, FormatSaleItems(x), x.Total }));
                totalColumns = [5];
                break;
            case "Discount Report":
                headers = ["Date", "Receipt No", "Subtotal", "Discount", "Total"];
                rows.AddRange(salesLog.Select(x => new { Sale = x, Discount = CalculateDiscountAmount(x) }).Where(x => x.Discount > 0m).Select(x => new object?[] { x.Sale.SoldAtUtc.ToLocalTime(), x.Sale.ReceiptNo, x.Sale.Subtotal, x.Discount, x.Sale.Total }));
                totalColumns = [3, 4, 5];
                break;
            case "VAT/GST Sales Tax Report":
                headers = ["Date", "Receipt No", "Taxable Sales", "VAT/GST", "Total"];
                rows.AddRange(salesLog.Select(x =>
                {
                    var vat = CalculateVatAmount(x.Total, isVatEnabled, vatRatePercent);
                    return new object?[] { x.SoldAtUtc.ToLocalTime(), x.ReceiptNo, x.Total - vat, vat, x.Total };
                }));
                totalColumns = [3, 4, 5];
                break;
            case "End-of-Day (Z) Report":
                headers = ["Metric", "Value"];
                rows.AddRange(new[]
                {
                    new object?[] { "Receipts", report.ReceiptCount },
                    new object?[] { "Gross Sales", report.GrossTotal },
                    new object?[] { "Average Receipt", report.ReceiptCount == 0 ? 0m : report.GrossTotal / report.ReceiptCount },
                    new object?[] { "Cash Sales", SumPaymentType(salesLog, "Cash") },
                    new object?[] { "Card Sales", SumPaymentType(salesLog, "Card") },
                    new object?[] { "VAT/GST", salesLog.Sum(x => CalculateVatAmount(x.Total, isVatEnabled, vatRatePercent)) },
                    new object?[] { "Discounts", salesLog.Sum(CalculateDiscountAmount) }
                });
                totalColumns = [];
                break;
            case "Cash Drawer Report":
                headers = ["Metric", "Amount"];
                rows.AddRange(new[]
                {
                    new object?[] { "Cash Sales", SumPaymentType(salesLog, "Cash") },
                    new object?[] { "Cash Refunds", salesLog.Where(x => x.PaymentType.Contains("Cash", StringComparison.OrdinalIgnoreCase) && x.Total < 0m).Sum(x => x.Total) },
                    new object?[] { "Expected Cash", SumPaymentType(salesLog, "Cash") }
                });
                totalColumns = [];
                break;
            case "Sales by Cashier Report":
                headers = ["Cashier", "Receipts", "Gross Sales"];
                rows.Add(new object?[] { "Not tracked", salesLog.Count, salesLog.Sum(x => x.Total) });
                totalColumns = [3];
                break;
            case "Customer Purchase History Report":
                headers = ["Customer", "Receipts", "Gross Sales", "Current Balance"];
                rows.AddRange(report.CustomerSales.OrderBy(x => x.CustomerName).Select(x => new object?[] { x.CustomerName, x.ReceiptCount, x.GrossTotal, x.CurrentBalance }));
                totalColumns = [3, 4];
                break;
            case "Customer Outstanding Balances Report":
                headers = ["Customer", "Outstanding Balance"];
                rows.AddRange(report.CustomerSales.Where(x => x.CurrentBalance != 0m).OrderBy(x => x.CustomerName).Select(x => new object?[] { x.CustomerName, x.CurrentBalance }));
                totalColumns = [2];
                break;
            case "Customer Payments and Receivables Report":
                headers = ["Customer", "Receivables", "Payments Made", "Remaining Balance"];
                rows.AddRange(customerReceivables
                    .OrderBy(x => x.CustomerName)
                    .Select(x => new object?[]
                    {
                        x.CustomerName,
                        x.Receivables,
                        x.PaymentsMade,
                        x.RemainingBalance
                    }));
                totalColumns = [2, 3, 4];
                break;
            case "Purchase History/Receiving Report":
                headers = ["Time", "Type", "SKU", "Reason", "Delta"];
                rows.AddRange(movements.Where(x => !x.Type.Equals("SALE", StringComparison.OrdinalIgnoreCase)).Select(x => new object?[] { x.OccurredAtUtc.ToLocalTime(), x.Type, x.Sku, x.Reason, x.DeltaDisplay }));
                totalColumns = [];
                break;
            case "Stock Adjustment Report":
                headers = ["Time", "SKU", "Reason", "Delta"];
                rows.AddRange(movements.Where(x => !x.Type.Equals("SALE", StringComparison.OrdinalIgnoreCase)).Select(x => new object?[] { x.OccurredAtUtc.ToLocalTime(), x.Sku, x.Reason, x.DeltaDisplay }));
                totalColumns = [];
                break;
            case "Transaction Audit Report":
                headers = ["Time", "Type", "Reference", "Details", "Amount"];
                rows.AddRange(salesLog.Select(x => new object?[] { x.SoldAtUtc.ToLocalTime(), x.Total < 0m ? "Refund" : "Sale", x.ReceiptNo, x.PaymentType, x.Total }));
                totalColumns = [5];
                break;
            case "Sales by Date Range Report":
            default:
                headers = ["Date", "Receipt No", "Payment Type", "Items", "Subtotal", "VAT/GST", "Discount", "Total"];
                rows.AddRange(salesLog.Select(x => new object?[] { x.SoldAtUtc.ToLocalTime(), x.ReceiptNo, x.PaymentType, FormatSaleItems(x), x.Subtotal, CalculateVatAmount(x.Total, isVatEnabled, vatRatePercent), CalculateDiscountAmount(x), x.Total }));
                totalColumns = [5, 6, 7, 8];
                break;
        }

        WriteReportSheet(sheet, reportName, from, to, headers, rows, totalColumns);
    }

    private static void WriteReportSheet(IXLWorksheet sheet, string reportName, DateTime from, DateTime to, string[] headers, IReadOnlyList<object?[]> rows, IReadOnlyCollection<int> totalColumns)
    {
        sheet.Cell("A1").Value = reportName;
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 18;
        sheet.Cell("A2").Value = $"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        sheet.Range(2, 1, 2, headers.Length).Merge();

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];

        var row = headerRow + 1;
        foreach (var values in rows)
        {
            for (var column = 0; column < headers.Length; column++)
                SetCellValue(sheet.Cell(row, column + 1), values.ElementAtOrDefault(column));
            row++;
        }

        var totalRow = row;
        if (totalColumns.Count > 0)
        {
            sheet.Cell(totalRow, 1).Value = "Total";
            foreach (var column in totalColumns)
            {
                sheet.Cell(totalRow, column).FormulaA1 = rows.Count == 0
                    ? "0"
                    : $"SUM({sheet.Cell(headerRow + 1, column).Address}:{sheet.Cell(totalRow - 1, column).Address})";
            }
        }
        else if (rows.Count == 0)
        {
            sheet.Cell(totalRow, 1).Value = "No data available for this report and date range.";
        }

        var lastRow = totalColumns.Count > 0 || rows.Count == 0 ? totalRow : totalRow - 1;
        var usedRange = sheet.Range(headerRow, 1, Math.Max(headerRow, lastRow), headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var headerRange = sheet.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        if (totalColumns.Count > 0)
        {
            var totalRange = sheet.Range(totalRow, 1, totalRow, headers.Length);
            totalRange.Style.Font.Bold = true;
            totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
        }

        for (var column = 1; column <= headers.Length; column++)
        {
            var header = headers[column - 1];
            if (IsMoneyHeader(header))
                sheet.Column(column).Style.NumberFormat.Format = "$#,##0.00";
            else if (header.Contains("%", StringComparison.OrdinalIgnoreCase))
                sheet.Column(column).Style.NumberFormat.Format = "0.00";
            else if (header.Contains("Date", StringComparison.OrdinalIgnoreCase) || header.Contains("Time", StringComparison.OrdinalIgnoreCase))
                sheet.Column(column).Style.DateFormat.Format = "yyyy-mm-dd h:mm AM/PM";
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.ColumnsUsed())
            column.Width = Math.Min(column.Width, 70);
        sheet.CellsUsed().Style.Alignment.WrapText = true;
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                break;
            case DateOnly dateOnly:
                cell.Value = dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case decimal decimalValue:
                cell.Value = decimalValue;
                break;
            case double doubleValue:
                cell.Value = doubleValue;
                break;
            case float floatValue:
                cell.Value = floatValue;
                break;
            case int intValue:
                cell.Value = intValue;
                break;
            case long longValue:
                cell.Value = longValue;
                break;
            case bool boolValue:
                cell.Value = boolValue;
                break;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private static bool IsMoneyHeader(string header)
        => header.Contains("Amount", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Balance", StringComparison.OrdinalIgnoreCase)
            || header.Contains("COGS", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Cost", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Discount", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Gross", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Margin", StringComparison.OrdinalIgnoreCase) && !header.Contains("%", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Payment", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Profit", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Payable", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Refund", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Receivable", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Revenue", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Sales", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Subtotal", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Tax", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Total", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Value", StringComparison.OrdinalIgnoreCase)
            || header.Contains("VAT", StringComparison.OrdinalIgnoreCase)
            || header.Contains("GST", StringComparison.OrdinalIgnoreCase);

    private static string FormatSaleItems(SaleLogEntryDto sale)
        => string.Join("; ", sale.Lines.Select(line => $"{line.ProductName} x {line.Qty.ToString("0.##", CultureInfo.InvariantCulture)}"));

    private static decimal SumPaymentType(IEnumerable<SaleLogEntryDto> sales, string paymentType)
        => sales.Where(x => x.PaymentType.Contains(paymentType, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Total);

    private static string SheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    private static decimal CalculateDiscountAmount(SaleLogEntryDto sale)
        => Math.Max(0m, Math.Round(sale.Subtotal - sale.Total, 2, MidpointRounding.AwayFromZero));

    private static decimal CalculateVatAmount(decimal total, bool isVatEnabled, decimal vatRatePercent)
    {
        if (!isVatEnabled || vatRatePercent <= 0m || total == 0m)
            return 0m;

        var divisor = 100m + vatRatePercent;
        return Math.Round(total * vatRatePercent / divisor, 2, MidpointRounding.AwayFromZero);
    }

    private void NotifyAll() { foreach (var n in new[] { nameof(ReceiptCount), nameof(NetTotal), nameof(VatTotal), nameof(GrossTotal), nameof(AvgGross), nameof(SalesGross), nameof(Cogs), nameof(GrossProfit), nameof(GrossMarginPct), nameof(SelectedSale), nameof(SelectedRefundLine), nameof(RefundQuantity) }) OnPropertyChanged(n); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ReportExportOption(string Name);

