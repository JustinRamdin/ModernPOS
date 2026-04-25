using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using DataLocalDb = Pos.Local.Data.LocalDb;


using ClosedXML.Excel;

using Microsoft.EntityFrameworkCore;

using Pos.Local.Data;
using Pos.Local.Services;
using Pos.Terminal.Commands;

namespace Pos.Terminal.ViewModels;

public sealed class ExportTemplateDialogViewModel : INotifyPropertyChanged
{
    private readonly ExportTemplateDefinition _template;
    private readonly string _locationCode;

    private string _status = "Ready";

    // Debounce refresh
    private CancellationTokenSource? _refreshCts;

    // Cancel in-flight loads (prevents older loads overwriting new filters)
    private CancellationTokenSource? _loadCts;

    public string TemplateName => _template.Name;
    public string TemplateDescription => _template.Description;

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (_fromDate == value) return;
            _fromDate = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (_toDate == value) return;
            _toDate = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    // Filters
    public ObservableCollection<string> PaymentTypes { get; } = new();
    private string _selectedPaymentType = "All";
    public string SelectedPaymentType
    {
        get => _selectedPaymentType;
        set
        {
            if (_selectedPaymentType == value) return;
            _selectedPaymentType = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    public ObservableCollection<string> Customers { get; } = new();
    private string _selectedCustomer = "All";
    public string SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (_selectedCustomer == value) return;
            _selectedCustomer = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    public ObservableCollection<string> Items { get; } = new();
    private string _selectedItem = "All";
    public string SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            _selectedItem = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            _ = ScheduleRefreshAsync();
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> ColumnHeaders { get; } = new();
    public ObservableCollection<ExportRow> Rows { get; } = new();

    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }

    public ExportTemplateDialogViewModel(
        ExportTemplateDefinition template,
        string locationCode,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        _template = template;
        _locationCode = locationCode;

        FromDate = fromDate;
        ToDate = toDate;

        RefreshCommand = new AsyncRelayCommand(async _ => await LoadAsync());

        // ExportCommand expects the view to pass a file path. If you already have a SaveFile dialog in the View,
        // call vm.ExportAsync(path). Here we keep a placeholder that does nothing without a path.
        ExportCommand = new AsyncRelayCommand(async _ =>
        {
            Status = "Choose a file path to export...";
            await Task.CompletedTask;
        });

        InitFilterListsDefaults();

        _ = LoadFilterOptionsAsync();
        _ = LoadAsync();
    }

    private void InitFilterListsDefaults()
    {
        PaymentTypes.Clear(); PaymentTypes.Add("All");
        Customers.Clear(); Customers.Add("All");
        Items.Clear(); Items.Add("All");

        _selectedPaymentType = "All";
        _selectedCustomer = "All";
        _selectedItem = "All";
    }

    private static TimeZoneInfo GetTz()
    {
       // Use the terminal's configured OS timezone so selected calendar dates map
        // to the operator's local business day boundaries.
        return TimeZoneInfo.Local;
    }

    private (DateTime fromUtc, DateTime toUtc) GetUtcRange()
    {
        var tz = GetTz();

        var fromLocal = (FromDate ?? DateTime.Today).Date;
        var toLocal = (ToDate ?? DateTime.Today).Date;

        if (toLocal < fromLocal)
        {
            var tmp = fromLocal;
            fromLocal = toLocal;
            toLocal = tmp;
        }

        var startLocal = DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified);
        var endLocalExclusive = DateTime.SpecifyKind(toLocal.AddDays(1), DateTimeKind.Unspecified);

        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(endLocalExclusive, tz);

        return (fromUtc, toUtc);
    }

    private string? PaymentTypeFilter => SelectedPaymentType == "All" ? null : SelectedPaymentType;
    private string? CustomerFilter => SelectedCustomer == "All" ? null : SelectedCustomer;
    private string? ItemFilter => SelectedItem == "All" ? null : SelectedItem;
    private string? SearchFilter => string.IsNullOrWhiteSpace(SearchText) ? null : SearchText!.Trim();

    public async Task LoadAsync()
    {
        // cancel any in-flight load
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            Status = "Loading template data...";
            var (fromUtc, toUtc) = GetUtcRange();

            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var svc = new ReportingService(db);

            ColumnHeaders.Clear();
            Rows.Clear();

            switch (_template.Kind)
            {
                case ExportTemplateKind.Sales:
                {
                    ColumnHeaders.Add("Date (UTC)");
                    ColumnHeaders.Add("Receipt");
                    ColumnHeaders.Add("Status");
                    ColumnHeaders.Add("Payment Type");
                    ColumnHeaders.Add("Customer");
                    ColumnHeaders.Add("Net");
                    ColumnHeaders.Add("VAT");
                    ColumnHeaders.Add("Gross");

                    var netTotal = 0m;
                    var vatTotal = 0m;
                    var grossTotal = 0m;

                     var results = await svc.GetSalesExportAsync(fromUtc, toUtc);
                    var filtered = ApplySalesFilters(results);

                    foreach (var row in filtered)
                    {
                        token.ThrowIfCancellationRequested();

                        if (!IsPaymentToAccount(row.Status))
                        {
                            netTotal += row.NetTotal;
                            vatTotal += row.VatTotal;
                            grossTotal += row.GrossTotal;
                        }

                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["Date (UTC)"] = row.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                            ["Receipt"] = row.ReceiptNo,
                            ["Status"] = row.Status,
                            ["Payment Type"] = TryGetPaymentType(row) ?? "",
                            ["Customer"] = row.CustomerName,
                            ["Net"] = row.NetTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["VAT"] = row.VatTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }

                    Rows.Add(new ExportRow(new Dictionary<string, string>
                    {
                        ["Date (UTC)"] = "Total",
                        ["Receipt"] = "",
                        ["Status"] = "",
                        ["Payment Type"] = "",
                        ["Customer"] = "",
                        ["Net"] = netTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        ["VAT"] = vatTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        ["Gross"] = grossTotal.ToString("0.00", CultureInfo.InvariantCulture)
                    }));

                    break;
                }

                case ExportTemplateKind.Purchases:
                {
                    ColumnHeaders.Add("Date (UTC)");
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Reason");

                    foreach (var row in await svc.GetPurchaseAdjustmentsAsync(fromUtc, toUtc, _locationCode))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["Date (UTC)"] = row.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["Qty"] = row.QuantityDisplay,
                            ["Reason"] = row.Reason
                        }));
                    }
                    break;
                }

                case ExportTemplateKind.Customers:
                {
                    ColumnHeaders.Add("Customer");
                    ColumnHeaders.Add("Receipts");
                    ColumnHeaders.Add("Gross");
                    ColumnHeaders.Add("Balance");

                    foreach (var row in await svc.GetCustomerSalesAsync(fromUtc, toUtc))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["Customer"] = row.CustomerName,
                            ["Receipts"] = row.ReceiptCount.ToString(CultureInfo.InvariantCulture),
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Balance"] = row.CurrentBalance.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                }

                case ExportTemplateKind.Inventory:
                {
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("On hand");
                    ColumnHeaders.Add("Sell value");
                    ColumnHeaders.Add("Cost value");
                    ColumnHeaders.Add("Margin");

                    foreach (var row in await svc.GetInventoryValuationAsync(_locationCode))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["On hand"] = row.OnHandDisplay,
                            ["Sell value"] = row.SellingValue.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Cost value"] = row.CostValue.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Margin"] = row.EstimatedGrossMargin.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                }

                case ExportTemplateKind.LowStock:
                {
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("On hand");
                    ColumnHeaders.Add("Avg/day");
                    ColumnHeaders.Add("Days left");
                    ColumnHeaders.Add("Reorder");

                    var rangeDays = Math.Clamp((int)Math.Ceiling((toUtc - fromUtc).TotalDays), 1, 90);
                    foreach (var row in await svc.GetLowStockAsync(_locationCode, rangeDays, suggestedReorderDays: 7m))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["On hand"] = row.OnHandDisplay,
                            ["Avg/day"] = row.AvgDailyUsageBase.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Days left"] = row.DaysRemaining.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Reorder"] = row.SuggestedReorderBase.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                }

                case ExportTemplateKind.TopProducts:
                {
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Gross");

                    foreach (var row in await svc.GetTopProductsAsync(fromUtc, toUtc, 50))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["Qty"] = row.QuantityDisplay,
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                }

                case ExportTemplateKind.Profit:
                {
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Sales");
                    ColumnHeaders.Add("COGS");
                    ColumnHeaders.Add("Profit");
                    ColumnHeaders.Add("Margin %");

                    foreach (var row in await svc.GetProfitByProductAsync(fromUtc, toUtc, 200))
                    {
                        token.ThrowIfCancellationRequested();
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["Qty"] = row.QuantityDisplay,
                            ["Sales"] = row.SalesGross.ToString("0.00", CultureInfo.InvariantCulture),
                            ["COGS"] = row.Cogs.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Profit"] = row.GrossProfit.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Margin %"] = row.GrossMarginPct.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                }
            }

            Status = $"Loaded {Rows.Count} rows.";
        }
        catch (OperationCanceledException)
        {
            // ignore (user changed filters quickly)
        }
        catch (Exception ex)
        {
            Status = $"Failed to load: {ex.Message}";
        }
    }

    private async Task ScheduleRefreshAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
                await LoadAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();
            var svc = new ReportingService(db);

            // Only populate filters for Sales-like templates (safe to do for all; service can return empty)
            var (fromUtc, toUtc) = GetUtcRange();
            var paymentTypes = (await svc.GetSalesExportAsync(fromUtc, toUtc))
                .Select(TryGetPaymentType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var customers = (await svc.GetCustomerSalesAsync(fromUtc, toUtc))
                .Select(row => row.CustomerName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var items = await LoadItemOrSkuListAsync(db, CancellationToken.None);

            PaymentTypes.Clear(); PaymentTypes.Add("All");
            foreach (var p in paymentTypes)
                PaymentTypes.Add(p);

            Customers.Clear(); Customers.Add("All");
            foreach (var c in customers)
                Customers.Add(c);

            Items.Clear(); Items.Add("All");
            foreach (var i in items)
                Items.Add(i);

            // Ensure Selected values remain valid
            if (!PaymentTypes.Contains(SelectedPaymentType)) SelectedPaymentType = "All";
            if (!Customers.Contains(SelectedCustomer)) SelectedCustomer = "All";
            if (!Items.Contains(SelectedItem)) SelectedItem = "All";
        }
        catch
        {
            // Keep usable even if something fails
            InitFilterListsDefaults();
        }
    }

 private static Task<List<string>> LoadItemOrSkuListAsync(
        PosLocalDbContext db,
        CancellationToken ct)
        => db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAtUtc == null)
            .OrderBy(p => p.Name)
            .Select(p => string.IsNullOrWhiteSpace(p.Sku) ? p.Name : $"{p.Sku} - {p.Name}")
            .ToListAsync(ct);

    public Task ExportAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Export");

        for (var c = 0; c < ColumnHeaders.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = ColumnHeaders[c];
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows[r];
            for (var c = 0; c < ColumnHeaders.Count; c++)
            {
                var header = ColumnHeaders[c];
                 var cell = worksheet.Cell(r + 2, c + 1);
                var value = row[header];

                if (TryWriteTypedValue(cell, header, value))
                    continue;

                cell.Value = value ?? string.Empty;
            }
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);

        Status = $"Exported {Rows.Count} rows.";
        return Task.CompletedTask;
    }

     private static bool TryWriteTypedValue(IXLCell cell, string header, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (header.Contains("Date", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUtc))
        {
            cell.Value = parsedUtc;
            cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericValue))
        {
            cell.Value = numericValue;
            cell.Style.NumberFormat.Format = "0.00";
            return true;
        }

        return false;
    }
    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
   => DataLocalDb.BuildOptions();

    private static PosLocalDbContext CreateLocalDb() => new(BuildDbOptions());

    private IEnumerable<SalesExportRowDto> ApplySalesFilters(IEnumerable<SalesExportRowDto> rows)
    {
        var filtered = rows;

        if (!string.IsNullOrWhiteSpace(PaymentTypeFilter))
        {
            filtered = filtered.Where(row =>
                string.Equals(TryGetPaymentType(row), PaymentTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(CustomerFilter))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.CustomerName, CustomerFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchFilter))
        {
            var term = SearchFilter;
            filtered = filtered.Where(row =>
                row.ReceiptNo.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Status.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (TryGetPaymentType(row)?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return filtered;
    }

    private static bool IsPaymentToAccount(string status)
        => string.Equals(status, "Payment to Account", StringComparison.OrdinalIgnoreCase);

     private static string? TryGetPaymentType(SalesExportRowDto row)
        => string.IsNullOrWhiteSpace(row.PaymentType) ? null : row.PaymentType;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
