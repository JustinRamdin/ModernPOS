using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
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

    public string TemplateName => _template.Name;
    public string TemplateDescription => _template.Description;

    private DateTime? _fromDate;
    public DateTime? FromDate { get => _fromDate; set { _fromDate = value; OnPropertyChanged(); } }

    private DateTime? _toDate;
    public DateTime? ToDate { get => _toDate; set { _toDate = value; OnPropertyChanged(); } }

    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public ObservableCollection<string> ColumnHeaders { get; } = new();
    public ObservableCollection<ExportRow> Rows { get; } = new();

    public System.Windows.Input.ICommand RefreshCommand { get; }

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
    }

    private static TimeZoneInfo GetTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Port_of_Spain"); }
        catch { return TimeZoneInfo.Local; }
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

    public async Task LoadAsync()
    {
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
                    ColumnHeaders.Add("Date (UTC)");
                    ColumnHeaders.Add("Receipt");
                    ColumnHeaders.Add("Status");
                    ColumnHeaders.Add("Customer");
                    ColumnHeaders.Add("Net");
                    ColumnHeaders.Add("VAT");
                    ColumnHeaders.Add("Gross");
                    foreach (var row in await svc.GetSalesExportAsync(fromUtc, toUtc))
                    {
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["Date (UTC)"] = row.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                            ["Receipt"] = row.ReceiptNo,
                            ["Status"] = row.Status,
                            ["Customer"] = row.CustomerName,
                            ["Net"] = row.NetTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["VAT"] = row.VatTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                case ExportTemplateKind.Purchases:
                    ColumnHeaders.Add("Date (UTC)");
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Reason");
                    foreach (var row in await svc.GetPurchaseAdjustmentsAsync(fromUtc, toUtc, _locationCode))
                    {
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
                case ExportTemplateKind.Customers:
                    ColumnHeaders.Add("Customer");
                    ColumnHeaders.Add("Receipts");
                    ColumnHeaders.Add("Gross");
                    ColumnHeaders.Add("Balance");
                    foreach (var row in await svc.GetCustomerSalesAsync(fromUtc, toUtc))
                    {
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["Customer"] = row.CustomerName,
                            ["Receipts"] = row.ReceiptCount.ToString(CultureInfo.InvariantCulture),
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture),
                            ["Balance"] = row.CurrentBalance.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                case ExportTemplateKind.Inventory:
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("On hand");
                    ColumnHeaders.Add("Sell value");
                    ColumnHeaders.Add("Cost value");
                    ColumnHeaders.Add("Margin");
                    foreach (var row in await svc.GetInventoryValuationAsync(_locationCode))
                    {
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
                case ExportTemplateKind.LowStock:
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("On hand");
                    ColumnHeaders.Add("Avg/day");
                    ColumnHeaders.Add("Days left");
                    ColumnHeaders.Add("Reorder");
                    var rangeDays = Math.Clamp((int)Math.Ceiling((toUtc - fromUtc).TotalDays), 1, 90);
                    foreach (var row in await svc.GetLowStockAsync(_locationCode, rangeDays, suggestedReorderDays: 7m))
                    {
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
                case ExportTemplateKind.TopProducts:
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Gross");
                    foreach (var row in await svc.GetTopProductsAsync(fromUtc, toUtc, 50))
                    {
                        Rows.Add(new ExportRow(new Dictionary<string, string>
                        {
                            ["SKU"] = row.Sku,
                            ["Item"] = row.Name,
                            ["Qty"] = row.QuantityDisplay,
                            ["Gross"] = row.GrossTotal.ToString("0.00", CultureInfo.InvariantCulture)
                        }));
                    }
                    break;
                case ExportTemplateKind.Profit:
                    ColumnHeaders.Add("SKU");
                    ColumnHeaders.Add("Item");
                    ColumnHeaders.Add("Qty");
                    ColumnHeaders.Add("Sales");
                    ColumnHeaders.Add("COGS");
                    ColumnHeaders.Add("Profit");
                    ColumnHeaders.Add("Margin %");
                    foreach (var row in await svc.GetProfitByProductAsync(fromUtc, toUtc, 200))
                    {
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
                default:
                    break;
            }

            Status = $"Loaded {Rows.Count} rows.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load: {ex.Message}";
        }
    }

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
                worksheet.Cell(r + 2, c + 1).Value = row[header];
            }
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);

        Status = $"Exported {Rows.Count} rows.";
        return Task.CompletedTask;
    }

    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = "pos.local.db",
            ForeignKeys = true
        }.ToString();

        return new DbContextOptionsBuilder<PosLocalDbContext>()
            .UseSqlite(cs)
            .Options;
    }

    private static PosLocalDbContext CreateLocalDb() => new(BuildDbOptions());

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ExportTemplateDefinition(
    string Name,
    string Description,
    ExportTemplateKind Kind
);

public enum ExportTemplateKind
{
    Sales,
    Purchases,
    Customers,
    Inventory,
    LowStock,
    TopProducts,
    Profit
}

public sealed class ExportRow
{
    public ExportRow(Dictionary<string, string> values)
    {
        Values = values;
    }

    public Dictionary<string, string> Values { get; }

    public string this[string key] => Values.TryGetValue(key, out var value) ? value : "";
}
