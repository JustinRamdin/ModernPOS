using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Measurements;
using Pos.Contracts;
using Pos.Local.Data;
using Pos.Local.Entities;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class InventoryViewModel : INotifyPropertyChanged
{
    private readonly bool _isPracticeMode;
    private const string PracticeLocationCode = "DEFAULT";

    public InventoryViewModel(bool isPracticeMode = false)
    {
        _isPracticeMode = isPracticeMode;
    }

    public ObservableCollection<ProductListItemVm> Products { get; } = new();
    private List<ProductListItemVm> _all = [];    
    private string _search = "";
    public string Search { get => _search; set { if (_search == value) return; _search = value; OnPropertyChanged(); ApplySearch(); } }

    private string _listStatus = "Loading...";
    public string ListStatus { get => _listStatus; set { _listStatus = value; OnPropertyChanged(); } }
    private string _editorStatus = "Select an item or click New.";
    public string EditorStatus { get => _editorStatus; set { _editorStatus = value; OnPropertyChanged(); } }

    private ProductListItemVm? _selected;
    public ProductListItemVm? Selected { get => _selected; set { if (ReferenceEquals(_selected, value)) return; _selected = value; OnPropertyChanged(); LoadSelectedIntoEditor(); } }
    private Guid? _editingId;

    public string EditSku { get; set; } = "";
    public string EditName { get; set; } = "";
    public string EditDescription { get; set; } = "";
    public string EditLocation { get; set; } = "";
    public string EditCostPriceText { get; set; } = "0.00";
    public string EditSellingPriceText { get; set; } = "0.00";
    public bool EditVatInclusive { get; set; }
    private bool _editIsLength;
    public bool EditIsLength
    {
        get => _editIsLength;
        set
        {
            if (_editIsLength == value) return;
            _editIsLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLengthStock));
            OnPropertyChanged(nameof(IsUnitStock));
            OnPropertyChanged(nameof(LengthPreviewLine));
        }
    }
    public string EditOnHandQtyText { get; set; } = "0";
    private string _editFeetText = "0";
    public string EditFeetText
    {
        get => _editFeetText;
        set
        {
            if (_editFeetText == value) return;
            _editFeetText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LengthPreviewLine));
        }
    }
    private string _editInchesText = "0";
    public string EditInchesText
    {
        get => _editInchesText;
        set
        {
            if (_editInchesText == value) return;
            _editInchesText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LengthPreviewLine));
        }
    }

    public bool IsLengthStock => EditIsLength;
    public bool IsUnitStock => !EditIsLength;
    public string LengthPreviewLine
    {
        get
        {
            if (!EditIsLength) return "";
            var ft = int.TryParse(EditFeetText, out var f) ? Math.Max(0, f) : 0;
            var inch = int.TryParse(EditInchesText, out var i) ? Math.Max(0, i) : 0;
            var norm = LengthConverter.Normalize(ft, inch);
            var totalInches = LengthConverter.ToTotalInches(norm.Feet, norm.Inches);
            return $"Normalized: {norm.Feet} ft {norm.Inches} in ({totalInches} in)";
        }
    }
    public async Task LoadAsync()
    {
        try
        {
            ListStatus = "Loading...";
            Products.Clear();

           if (_isPracticeMode)
            {
                await using var db = CreateLocalDb();
                await db.Database.EnsureCreatedAsync();

                var localProducts = await db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.DeletedAtUtc == null)
                    .OrderBy(p => p.Name)
                    .ToListAsync();

                var localBalances = await db.Inventory
                    .AsNoTracking()
                    .Where(i => i.LocationCode == PracticeLocationCode)
                    .ToDictionaryAsync(i => i.ProductId);

                _all = localProducts.Select(p =>
                {
                    localBalances.TryGetValue(p.Id, out var bal);
                    return new ProductListItemVm
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Sku = p.Sku,
                        Description = p.Description,
                        Location = PracticeLocationCode,
                        CostPrice = p.CostPrice,
                        SellingPrice = p.Price,
                        VatInclusive = p.VatInclusive,
                        IsLength = p.IsLength,
                        OnHandQty = bal?.OnHand ?? 0m,
                        OnHandInches = bal?.OnHandInches ?? 0
                    };
                }).ToList();
            }
            else
            {
                using var api = await CreateApiAsync();
                var items = await api.GetInventoryAsync();
                _all = items.Select(ProductListItemVm.From).ToList();
            }

            ApplySearch();
            ListStatus = _all.Count == 0
                ? (_isPracticeMode ? "No inventory items in practice mode." : "No inventory items on server.")
                : $"Loaded {_all.Count} items.";
        }
        catch (Exception ex) { ListStatus = "Load failed."; EditorStatus = BuildServerStatusMessage(ex, "load inventory"); }
    }

    public void NewItem() { _editingId = null; Selected = null; EditSku = EditName = EditDescription = EditLocation = ""; EditCostPriceText = EditSellingPriceText = "0.00"; EditVatInclusive = EditIsLength = false; EditOnHandQtyText = EditFeetText = EditInchesText = "0"; NotifyEditor(); EditorStatus = "Creating new item."; }

    public async Task SaveAsync()
    {
        try
        {
            if (!decimal.TryParse(EditCostPriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost)) cost = 0;
            if (!decimal.TryParse(EditSellingPriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var sell)) sell = 0;
            if (!decimal.TryParse(EditOnHandQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty)) qty = 0;
            var ft = int.TryParse(EditFeetText, out var f) ? Math.Max(0, f) : 0;
            var inch = int.TryParse(EditInchesText, out var i) ? Math.Max(0, i) : 0;
            var norm = LengthConverter.Normalize(ft, inch);
            var onHandInches = LengthConverter.ToTotalInches(norm.Feet, norm.Inches);

            if (_isPracticeMode)
            {
                await using var db = CreateLocalDb();
                await db.Database.EnsureCreatedAsync();

                Product product;
                if (_editingId is null)
                {
                    product = new Product { Id = Guid.NewGuid() };
                    db.Products.Add(product);
                }
                else
                {
                    product = await db.Products.FirstOrDefaultAsync(p => p.Id == _editingId.Value)
                        ?? throw new InvalidOperationException("Product not found.");
                }

                product.Sku = EditSku.Trim();
                product.Name = EditName.Trim();
                product.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();
                product.CostPrice = cost;
                product.Price = sell;
                product.VatInclusive = EditVatInclusive;
                product.IsLength = EditIsLength;
                product.IsActive = true;
                product.DeletedAtUtc = null;

                var balance = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == product.Id && i.LocationCode == PracticeLocationCode);
                if (balance == null)
                {
                    balance = new InventoryBalance
                    {
                        ProductId = product.Id,
                        LocationCode = PracticeLocationCode
                    };
                    db.Inventory.Add(balance);
                }

                balance.OnHand = qty;
                balance.OnHandInches = onHandInches;

                await db.SaveChangesAsync();
                EditorStatus = "Saved locally in practice mode.";
            }
            else
            {
                var req = new UpsertInventoryItemRequest(EditSku.Trim(), EditName.Trim(), string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(), string.IsNullOrWhiteSpace(EditLocation) ? null : EditLocation.Trim(), cost, sell, EditVatInclusive, EditIsLength, qty, onHandInches, true);
                using var api = await CreateApiAsync();
                if (_editingId is null) await api.CreateInventoryAsync(req); else await api.UpdateInventoryAsync(_editingId.Value, req);
                EditorStatus = "Saved to server.";
            }

            await LoadAsync();
        }
        catch (Exception ex) { EditorStatus = BuildServerStatusMessage(ex, "save inventory item"); }
    }

    public async Task DeleteAsync()
    {
        if (_editingId is null) return;
         try
        {
            if (_isPracticeMode)
            {
                await using var db = CreateLocalDb();
                await db.Database.EnsureCreatedAsync();
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == _editingId.Value);
                if (product != null)
                {
                    product.IsActive = false;
                    product.DeletedAtUtc = DateTime.UtcNow;
                }

                var balances = await db.Inventory.Where(i => i.ProductId == _editingId.Value).ToListAsync();
                if (balances.Count > 0)
                    db.Inventory.RemoveRange(balances);

                await db.SaveChangesAsync();
                EditorStatus = "Deleted locally in practice mode.";
            }
            else
            {
                using var api = await CreateApiAsync();
                await api.DeleteInventoryAsync(_editingId.Value);
                EditorStatus = "Deleted from server.";
            }

            NewItem();
            await LoadAsync();
        }
        catch (Exception ex) { EditorStatus = BuildServerStatusMessage(ex, "delete inventory item"); }
    }
    private void ApplySearch() { var t = (Search ?? "").Trim(); var f = string.IsNullOrWhiteSpace(t) ? _all : _all.Where(p => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Contains(t, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(p.Location) && p.Location.Contains(t, StringComparison.OrdinalIgnoreCase))); Products.Clear(); foreach (var p in f) Products.Add(p); }    private void LoadSelectedIntoEditor()
    {
         if (Selected is null) { _editingId = null; return; }
        _editingId = Selected.Id; EditSku = Selected.Sku ?? ""; EditName = Selected.Name; EditDescription = Selected.Description ?? ""; EditLocation = Selected.Location ?? ""; EditCostPriceText = Selected.CostPrice.ToString(CultureInfo.InvariantCulture); EditSellingPriceText = Selected.SellingPrice.ToString(CultureInfo.InvariantCulture); EditVatInclusive = Selected.VatInclusive; EditIsLength = Selected.IsLength; EditOnHandQtyText = Selected.OnHandQty.ToString(CultureInfo.InvariantCulture); var fi = LengthConverter.FromTotalInches(Selected.OnHandInches); EditFeetText = fi.Feet.ToString(); EditInchesText = fi.Inches.ToString(); NotifyEditor();    }

    private void NotifyEditor() { foreach (var n in new[]{ nameof(EditSku), nameof(EditName), nameof(EditDescription), nameof(EditLocation), nameof(EditCostPriceText), nameof(EditSellingPriceText), nameof(EditVatInclusive), nameof(EditIsLength), nameof(EditOnHandQtyText), nameof(EditFeetText), nameof(EditInchesText), nameof(IsLengthStock), nameof(IsUnitStock), nameof(LengthPreviewLine)}) OnPropertyChanged(n); }  
     private static async Task<RemoteServerApi> CreateApiAsync()
    {
       var deploy = await new SettingsStore().LoadDeploymentAsync();
        return new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
    }

     private static PosLocalDbContext CreateLocalDb() => new(LocalDb.BuildOptions());
     private static string BuildServerStatusMessage(Exception ex, string operation)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode is null)
                return $"Cannot reach server while trying to {operation}: {httpEx.Message}";

            return $"Server failed while trying to {operation} ({(int)httpEx.StatusCode} {httpEx.StatusCode}).";
        }

        return $"Operation failed while trying to {operation}: {ex.Message}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
     private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ProductListItemVm
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Sku { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public decimal CostPrice { get; init; }
    public decimal SellingPrice { get; init; }
    public bool VatInclusive { get; init; }
    public bool IsLength { get; init; }
    public decimal OnHandQty { get; init; }
    public int OnHandInches { get; init; }
    public string SkuLine => $"SKU: {Sku}";
    public string PriceLine => $"Price: {SellingPrice:0.00}";
    public string FlagsLine => $"VAT Incl: {VatInclusive} | Length: {IsLength}";
    public string LocationLine => $"Location: {Location ?? "Main Store"}";

    public string StockLine => !IsLength ? $"Stock: {OnHandQty:0.###}" : $"Stock: {OnHandInches / 12} ft {OnHandInches % 12} in";
    public static ProductListItemVm From(InventoryItemDto p) => new() { Id = p.Id, Name = p.Name, Sku = p.Sku, Description = p.Description, Location = p.Location, CostPrice = p.CostPrice, SellingPrice = p.Price, VatInclusive = p.VatInclusive, IsLength = p.IsLength, OnHandQty = p.OnHand, OnHandInches = p.OnHandInches };}
