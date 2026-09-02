using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Pos.Application.Measurements;
using Pos.Contracts;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class InventoryViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();
    private bool _isLoadingSettings;

    public InventoryViewModel(int inventoryBucket = 1)
    {
        InventoryBucket = Math.Clamp(inventoryBucket, 1, 2);
    }

    public int InventoryBucket { get; }
    public string InventoryTitle => $"Inventory {InventoryBucket}";

    public ObservableCollection<ProductListItemVm> Products { get; } = new();
    private List<ProductListItemVm> _all = [];    
    private string _search = "";
    public string Search { get => _search; set { if (_search == value) return; _search = value; OnPropertyChanged(); ApplySearch(); } }
    private bool _useEasyInventoryNames;
    public bool UseEasyInventoryNames
    {
        get => _useEasyInventoryNames;
        set
        {
            if (_useEasyInventoryNames == value) return;
            _useEasyInventoryNames = value;
            OnPropertyChanged();
            ApplySearch();
            ListStatus = value
                ? "Showing easy names based on item descriptions."
                : _all.Count == 0 ? "No inventory items on server." : $"Loaded {_all.Count} items.";

            if (!_isLoadingSettings)
                _ = _settingsStore.SaveEasyInventoryNamesAsync(value);
        }
    }

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
    public bool EditZeroRated { get; set; }
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
            var settings = await _settingsStore.LoadAsync();
            _isLoadingSettings = true;
            UseEasyInventoryNames = settings.UseEasyInventoryNames;
            _isLoadingSettings = false;

            using var api = await CreateApiAsync();
            var items = await api.GetInventoryAsync();
            _all = items.Where(x => x.InventoryBucket == InventoryBucket).Select(ProductListItemVm.From).ToList();

            ApplySearch();
            ListStatus = UseEasyInventoryNames
                ? "Showing easy names based on item descriptions."
                : _all.Count == 0 ? "No inventory items on server." : $"Loaded {_all.Count} items.";
        }
        catch (Exception ex) { ListStatus = "Load failed."; EditorStatus = BuildServerStatusMessage(ex, "load inventory"); }
    }

    public void NewItem() { _editingId = null; Selected = null; EditSku = EditName = EditDescription = EditLocation = ""; EditCostPriceText = EditSellingPriceText = "0.00"; EditVatInclusive = EditZeroRated = EditIsLength = false; EditOnHandQtyText = EditFeetText = EditInchesText = "0"; NotifyEditor(); EditorStatus = "Creating new item."; }

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

            var req = new UpsertInventoryItemRequest(EditSku.Trim(), EditName.Trim(), string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(), string.IsNullOrWhiteSpace(EditLocation) ? null : EditLocation.Trim(), cost, sell, EditVatInclusive, EditIsLength, qty, onHandInches, InventoryBucket, true, EditZeroRated);
            using var api = await CreateApiAsync();
            if (_editingId is null) await api.CreateInventoryAsync(req); else await api.UpdateInventoryAsync(_editingId.Value, req);
            EditorStatus = "Saved to server.";

            await LoadAsync();
        }
        catch (Exception ex) { EditorStatus = BuildServerStatusMessage(ex, "save inventory item"); }
    }

    public async Task DeleteAsync()
    {
        if (_editingId is null) return;
         try
        {
            using var api = await CreateApiAsync();
            await api.DeleteInventoryAsync(_editingId.Value);
            EditorStatus = "Deleted from server.";

            NewItem();
            await LoadAsync();
        }
        catch (Exception ex) { EditorStatus = BuildServerStatusMessage(ex, "delete inventory item"); }
    }
    private void ApplySearch()
    {
        var t = (Search ?? "").Trim();
        var f = string.IsNullOrWhiteSpace(t)
            ? _all
            : _all.Where(p =>
                p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                || (UseEasyInventoryNames && p.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Contains(t, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(p.Description) && p.Description.Contains(t, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(p.Location) && p.Location.Contains(t, StringComparison.OrdinalIgnoreCase)));

        Products.Clear();
        foreach (var p in f)
        {
            p.UseEasyInventoryName = UseEasyInventoryNames;
            Products.Add(p);
        }
    }

    private void LoadSelectedIntoEditor()
    {
         if (Selected is null) { _editingId = null; return; }
        _editingId = Selected.Id; EditSku = Selected.Sku ?? ""; EditName = Selected.Name; EditDescription = Selected.Description ?? ""; EditLocation = Selected.Location ?? ""; EditCostPriceText = Selected.CostPrice.ToString(CultureInfo.InvariantCulture); EditSellingPriceText = Selected.SellingPrice.ToString(CultureInfo.InvariantCulture); EditVatInclusive = Selected.VatInclusive; EditZeroRated = Selected.ZeroRated; EditIsLength = Selected.IsLength; EditOnHandQtyText = Selected.OnHandQty.ToString(CultureInfo.InvariantCulture); var fi = LengthConverter.FromTotalInches(Selected.OnHandInches); EditFeetText = fi.Feet.ToString(); EditInchesText = fi.Inches.ToString(); NotifyEditor();    }

    private void NotifyEditor() { foreach (var n in new[]{ nameof(EditSku), nameof(EditName), nameof(EditDescription), nameof(EditLocation), nameof(EditCostPriceText), nameof(EditSellingPriceText), nameof(EditVatInclusive), nameof(EditZeroRated), nameof(EditIsLength), nameof(EditOnHandQtyText), nameof(EditFeetText), nameof(EditInchesText), nameof(IsLengthStock), nameof(IsUnitStock), nameof(LengthPreviewLine)}) OnPropertyChanged(n); }  
     private static async Task<RemoteServerApi> CreateApiAsync()
    {
       var deploy = await new SettingsStore().LoadDeploymentAsync();
        return new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
    }

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

public sealed class ProductListItemVm : INotifyPropertyChanged
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Sku { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public decimal CostPrice { get; init; }
    public decimal SellingPrice { get; init; }
    public bool VatInclusive { get; init; }
    public bool ZeroRated { get; init; }
    public bool IsLength { get; init; }
    public decimal OnHandQty { get; init; }
    public int OnHandInches { get; init; }
    private bool _useEasyInventoryName;
    public bool UseEasyInventoryName
    {
        get => _useEasyInventoryName;
        set
        {
            if (_useEasyInventoryName == value) return;
            _useEasyInventoryName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => UseEasyInventoryName ? InventoryNameHelper.BuildEasyName(Name, Description) : Name;
    public string SkuLine => $"SKU: {Sku}";
    public string PriceLine => $"Price: {SellingPrice:0.00}";
    public string FlagsLine => $"VAT Incl: {VatInclusive} | Zero Rated: {ZeroRated} | Length: {IsLength}";
    public string LocationLine => $"Location: {Location ?? "Main Store"}";

    public string StockLine => !IsLength ? $"Stock: {OnHandQty:0.###}" : $"Stock: {OnHandInches / 12} ft {OnHandInches % 12} in";
    public int InventoryBucket { get; init; } = 1;
    public static ProductListItemVm From(InventoryItemDto p) => new() { Id = p.Id, Name = p.Name, Sku = p.Sku, Description = p.Description, Location = p.Location, CostPrice = p.CostPrice, SellingPrice = p.Price, VatInclusive = p.VatInclusive, ZeroRated = p.ZeroRated, IsLength = p.IsLength, OnHandQty = p.OnHand, OnHandInches = p.OnHandInches, InventoryBucket = p.InventoryBucket };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
