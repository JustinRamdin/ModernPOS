using System.Collections.ObjectModel;
using System.ComponentModel;
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
        ExportTemplates.Add(new ExportTemplateDefinition("Sales", "Receipts with totals, customer, and status.", ExportTemplateKind.Sales));
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

            Status = report.ReceiptCount == 0 && report.InventoryValuation.Count == 0 && report.CustomerSales.Count == 0
                ? "No data available from server."
                : "Reports loaded.";
            NotifyAll();
        }
        catch (Exception ex) { Status = $"Reports failed: {ex.Message}"; }
    }

    public Task LoadInventoryAsync() => LoadAllAsync();
    public Task LoadLowStockAsync() { LowStock.Clear(); Status = "No data available."; return Task.CompletedTask; }

    private (DateTime fromUtc, DateTime toUtc) GetUtcRange()
    {
        var fromDate = (FromDate ?? DateTime.Today).Date;
        var toDate = (ToDate ?? DateTime.Today).Date;
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
        return (DateTime.SpecifyKind(fromDate, DateTimeKind.Utc), DateTime.SpecifyKind(toDate.AddDays(1), DateTimeKind.Utc));
    }

    private static async Task<RemoteServerApi> CreateApiAsync() { var d = await new SettingsStore().LoadDeploymentAsync(); return new RemoteServerApi(d.ServerHost, d.ServerPort, d.AuthToken); }
    private void NotifyAll() { foreach (var n in new[] { nameof(ReceiptCount), nameof(NetTotal), nameof(VatTotal), nameof(GrossTotal), nameof(AvgGross), nameof(SalesGross), nameof(Cogs), nameof(GrossProfit), nameof(GrossMarginPct) }) OnPropertyChanged(n); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record InventoryMovementRowDto(DateTime AtUtc, string Type, string Name, decimal Qty);
public sealed record LowStockRowDto(string Name, string? Sku, decimal OnHand, int OnHandInches, decimal DaysRemaining, decimal SuggestedReorderQty);
