using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Pos.Local.Data;
using Pos.Local.Services;
using Pos.Terminal.Commands;

namespace Pos.Terminal.ViewModels;

public sealed class ReportsViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private DateTimeOffset? _fromDate;
    public DateTimeOffset? FromDate { get => _fromDate; set { _fromDate = value; OnPropertyChanged(); } }

    private DateTimeOffset? _toDate;
    public DateTimeOffset? ToDate { get => _toDate; set { _toDate = value; OnPropertyChanged(); } }

    private string _locationCode = "DEFAULT";
    public string LocationCode { get => _locationCode; set { _locationCode = string.IsNullOrWhiteSpace(value) ? "DEFAULT" : value.Trim(); OnPropertyChanged(); } }

    private int _lookbackDays = 14;
    public int LookbackDays { get => _lookbackDays; set { _lookbackDays = Math.Clamp(value, 1, 90); OnPropertyChanged(); } }

    // --- Z Report / Sales summary ---
    private int _receiptCount;
    public int ReceiptCount { get => _receiptCount; private set { _receiptCount = value; OnPropertyChanged(); } }

    private decimal _netTotal;
    public decimal NetTotal { get => _netTotal; private set { _netTotal = value; OnPropertyChanged(); } }

    private decimal _vatTotal;
    public decimal VatTotal { get => _vatTotal; private set { _vatTotal = value; OnPropertyChanged(); } }

    private decimal _grossTotal;
    public decimal GrossTotal { get => _grossTotal; private set { _grossTotal = value; OnPropertyChanged(); } }

    private decimal _avgGross;
    public decimal AvgGross { get => _avgGross; private set { _avgGross = value; OnPropertyChanged(); } }

    // --- Profit ---
    private decimal _salesGross;
    public decimal SalesGross { get => _salesGross; private set { _salesGross = value; OnPropertyChanged(); } }

    private decimal _cogs;
    public decimal Cogs { get => _cogs; private set { _cogs = value; OnPropertyChanged(); } }

    private decimal _grossProfit;
    public decimal GrossProfit { get => _grossProfit; private set { _grossProfit = value; OnPropertyChanged(); } }

    private decimal _grossMarginPct;
    public decimal GrossMarginPct { get => _grossMarginPct; private set { _grossMarginPct = value; OnPropertyChanged(); } }

    // --- Tenders ---
    private decimal _cashTotal;
    public decimal CashTotal { get => _cashTotal; private set { _cashTotal = value; OnPropertyChanged(); } }

    private decimal _debitTotal;
    public decimal DebitTotal { get => _debitTotal; private set { _debitTotal = value; OnPropertyChanged(); } }

    private decimal _creditTotal;
    public decimal CreditTotal { get => _creditTotal; private set { _creditTotal = value; OnPropertyChanged(); } }

    private decimal _onAccountTotal;
    public decimal OnAccountTotal { get => _onAccountTotal; private set { _onAccountTotal = value; OnPropertyChanged(); } }

    private decimal _changeGiven;
    public decimal ChangeGiven { get => _changeGiven; private set { _changeGiven = value; OnPropertyChanged(); } }

    private decimal _expectedCash;
    public decimal ExpectedCash { get => _expectedCash; private set { _expectedCash = value; OnPropertyChanged(); } }

    // Collections
    public ObservableCollection<SalesByDayRowDto> SalesByDay { get; } = new();
    public ObservableCollection<TopProductRowDto> TopProducts { get; } = new();
    public ObservableCollection<ProfitByProductRowDto> ProfitByProduct { get; } = new();
    public ObservableCollection<InventoryValuationRowDto> InventoryValuation { get; } = new();
    public ObservableCollection<InventoryMovementRowDto> InventoryMovements { get; } = new();
    public ObservableCollection<LowStockRowDto> LowStock { get; } = new();
    public ObservableCollection<CustomerSalesRowDto> CustomerSales { get; } = new();

    // Commands
    public ICommand RefreshAllCommand { get; }
    public ICommand RefreshInventoryCommand { get; }
    public ICommand RefreshLowStockCommand { get; }

    public ReportsViewModel()
    {
        var now = DateTimeOffset.Now;
        FromDate = new DateTimeOffset(now.Date.AddDays(-6), now.Offset);
        ToDate = new DateTimeOffset(now.Date, now.Offset);

        RefreshAllCommand = new AsyncRelayCommand(async _ => await LoadAllAsync());
        RefreshInventoryCommand = new AsyncRelayCommand(async _ => await LoadInventoryAsync());
        RefreshLowStockCommand = new AsyncRelayCommand(async _ => await LoadLowStockAsync());
    }

    private static TimeZoneInfo GetTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Port_of_Spain"); }
        catch { return TimeZoneInfo.Local; }
    }

    private (DateTime fromUtc, DateTime toUtc) GetUtcRange()
    {
        var tz = GetTz();

        var fromLocal = (FromDate ?? DateTimeOffset.Now).Date;
        var toLocal = (ToDate ?? DateTimeOffset.Now).Date;

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

    public async Task LoadAllAsync()
    {
        try
        {
            Status = "Loading reports...";
            var (fromUtc, toUtc) = GetUtcRange();
            var tz = GetTz();

            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var svc = new ReportingService(db);

            // Z / Sales
            var summary = await svc.GetSalesSummaryAsync(fromUtc, toUtc);
            ReceiptCount = summary.ReceiptCount;
            NetTotal = summary.NetTotal;
            VatTotal = summary.VatTotal;
            GrossTotal = summary.GrossTotal;
            AvgGross = summary.AverageGross;

            var byDay = await svc.GetSalesByDayAsync(fromUtc, toUtc, tz);
            SalesByDay.Clear();
            foreach (var r in byDay) SalesByDay.Add(r);

            var top = await svc.GetTopProductsAsync(fromUtc, toUtc, 15);
            TopProducts.Clear();
            foreach (var r in top) TopProducts.Add(r);

            // Profit
            var psum = await svc.GetProfitSummaryAsync(fromUtc, toUtc);
            SalesGross = psum.SalesGross;
            Cogs = psum.Cogs;
            GrossProfit = psum.GrossProfit;
            GrossMarginPct = psum.GrossMarginPct;

            var pprod = await svc.GetProfitByProductAsync(fromUtc, toUtc, 50);
            ProfitByProduct.Clear();
            foreach (var r in pprod) ProfitByProduct.Add(r);

            // VAT (reuse totals from sales, but if you want separate VAT view you already have VatTotal)
            // Tenders
            var tend = await svc.GetTenderSummaryAsync(fromUtc, toUtc);
            CashTotal = tend.CashTotal;
            DebitTotal = tend.DebitTotal;
            CreditTotal = tend.CreditTotal;
            OnAccountTotal = tend.OnAccountTotal;
            ChangeGiven = tend.ChangeGivenTotal;
            ExpectedCash = tend.ExpectedCashInDrawer;

            // Inventory movement
            var moves = await svc.GetInventoryMovementsAsync(fromUtc, toUtc, LocationCode);
            InventoryMovements.Clear();
            foreach (var r in moves) InventoryMovements.Add(r);

            // Customers
            var cust = await svc.GetCustomerSalesAsync(fromUtc, toUtc);
            CustomerSales.Clear();
            foreach (var r in cust) CustomerSales.Add(r);

            Status = "Reports loaded.";
        }
        catch (Exception ex)
        {
            Status = $"Reports failed: {ex.Message}";
        }
    }

    public async Task LoadInventoryAsync()
    {
        try
        {
            Status = "Loading inventory valuation...";
            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();
            var svc = new ReportingService(db);

            var rows = await svc.GetInventoryValuationAsync(LocationCode);
            InventoryValuation.Clear();
            foreach (var r in rows) InventoryValuation.Add(r);

            Status = $"Inventory valuation loaded ({InventoryValuation.Count}).";
        }
        catch (Exception ex)
        {
            Status = $"Inventory valuation failed: {ex.Message}";
        }
    }

    public async Task LoadLowStockAsync()
    {
        try
        {
            Status = "Loading low stock...";
            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();
            var svc = new ReportingService(db);

            var rows = await svc.GetLowStockAsync(LocationCode, LookbackDays, suggestedReorderDays: 7m);
            LowStock.Clear();
            foreach (var r in rows) LowStock.Add(r);

            Status = $"Low stock loaded ({LowStock.Count}).";
        }
        catch (Exception ex)
        {
            Status = $"Low stock failed: {ex.Message}";
        }
    }

    // --- DB (same file as Terminal) ---
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
