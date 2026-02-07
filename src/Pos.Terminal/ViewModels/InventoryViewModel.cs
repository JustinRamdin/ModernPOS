// File: src/Pos.Terminal/ViewModels/InventoryViewModel.cs
// Replace the ENTIRE file with this (copy/paste).
//
// ✅ Uses SAME DB file as Terminal/Customers: pos.local.db

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using DataLocalDb = Pos.Local.Data.LocalDb;

using Microsoft.EntityFrameworkCore;

using Pos.Application.Measurements;
using Pos.Local.Data;
using Pos.Local.Entities;
using Pos.Local.Services;
namespace Pos.Terminal.ViewModels;

public sealed class InventoryViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ProductListItemVm> Products { get; } = new();

    private List<ProductListItemVm> _all = new();

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (_search == value) return; _search = value; OnPropertyChanged(); ApplySearch(); }
    }

    private string _listStatus = "Loading...";
    public string ListStatus
    {
        get => _listStatus;
        set { _listStatus = value; OnPropertyChanged(); }
    }

    private string _editorStatus = "Select an item or click New.";
    public string EditorStatus
    {
        get => _editorStatus;
        set { _editorStatus = value; OnPropertyChanged(); }
    }

    private ProductListItemVm? _selected;
    public ProductListItemVm? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            OnPropertyChanged();
            LoadSelectedIntoEditor();
        }
    }

    // -----------------------------
    // Editor backing fields
    // -----------------------------
    private Guid? _editingId;

    private string _editSku = "";
    public string EditSku { get => _editSku; set { _editSku = value; OnPropertyChanged(); } }

    private string _editName = "";
    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }

    private string _editDescription = "";
    public string EditDescription { get => _editDescription; set { _editDescription = value; OnPropertyChanged(); } }

    private string _editCostPriceText = "0.00";
    public string EditCostPriceText { get => _editCostPriceText; set { _editCostPriceText = value; OnPropertyChanged(); } }

    private string _editSellingPriceText = "0.00";
    public string EditSellingPriceText { get => _editSellingPriceText; set { _editSellingPriceText = value; OnPropertyChanged(); } }

    private bool _editVatInclusive;
    public bool EditVatInclusive { get => _editVatInclusive; set { _editVatInclusive = value; OnPropertyChanged(); } }

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

    // Stock editor fields
    private string _editOnHandQtyText = "0";
    public string EditOnHandQtyText { get => _editOnHandQtyText; set { _editOnHandQtyText = value; OnPropertyChanged(); } }

    private string _editFeetText = "0";
    public string EditFeetText { get => _editFeetText; set { _editFeetText = value; OnPropertyChanged(); OnPropertyChanged(nameof(LengthPreviewLine)); } }

    private string _editInchesText = "0";
    public string EditInchesText { get => _editInchesText; set { _editInchesText = value; OnPropertyChanged(); OnPropertyChanged(nameof(LengthPreviewLine)); } }

    public bool IsLengthStock => EditIsLength;
    public bool IsUnitStock => !EditIsLength;

    public string LengthPreviewLine
    {
        get
        {
            if (!EditIsLength) return "";
            if (!TryParseNonNegativeInt(EditFeetText, out var ft)) ft = 0;
            if (!TryParseNonNegativeInt(EditInchesText, out var inch)) inch = 0;

            var norm = LengthConverter.Normalize(ft, inch);
            var total = LengthConverter.ToTotalInches(norm.Feet, norm.Inches);
            return $"Normalized: {norm.Feet} ft {norm.Inches} in  (Total inches: {total})";
        }
    }

    // -----------------------------
    // Public API
    // -----------------------------
    public async Task LoadAsync()
    {
        try
        {
            ListStatus = "Loading...";
            EditorStatus = "Loading inventory...";
            Products.Clear();

            var options = BuildDbOptions();
            await using var db = new PosLocalDbContext(options);

            await db.Database.EnsureCreatedAsync();

            var prods = await db.Products
                .AsNoTracking()
                .Where(p => p.DeletedAtUtc == null && p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var ids = prods.Select(p => p.Id).ToList();

            // Stock comes from InventoryBalance, not Product
            var balances = await db.Inventory
                .AsNoTracking()
                .Where(i => ids.Contains(i.ProductId) && i.LocationCode == "DEFAULT" && i.DeletedAtUtc == null)
                .ToDictionaryAsync(i => i.ProductId);

            _all = prods.Select(p =>
            {
                balances.TryGetValue(p.Id, out var bal);
                return ProductListItemVm.From(p, bal);
            }).ToList();

            ApplySearch();

            ListStatus = $"Loaded {_all.Count} items.";
            EditorStatus = "Select an item or click New.";
        }
        catch (Exception ex)
        {
            ListStatus = "Load failed.";
            EditorStatus = ex.Message;
        }
    }

    public void NewItem()
    {
        _editingId = null;
        Selected = null;

        EditSku = "";
        EditName = "";
        EditDescription = "";
        EditCostPriceText = "0.00";
        EditSellingPriceText = "0.00";
        EditVatInclusive = false;
        EditIsLength = false;

        EditOnHandQtyText = "0";
        EditFeetText = "0";
        EditInchesText = "0";

        EditorStatus = "Creating new item. Fill details and click Save.";
    }

    public async Task SaveAsync()
    {
        try
        {
            var errors = ValidateEditor(out var cost, out var sell, out var onHandQty, out var onHandInches);
            if (errors.Count > 0)
            {
                EditorStatus = "Fix: " + string.Join(" | ", errors);
                return;
            }

            var options = BuildDbOptions();
            await using var db = new PosLocalDbContext(options);

            await db.Database.EnsureCreatedAsync();

            Product p;
            if (_editingId == null)
            {
                p = new Product { Id = Guid.NewGuid(), IsActive = true };
                db.Products.Add(p);
            }
            else
            {
                p = await db.Products.FirstAsync(x => x.Id == _editingId.Value);
            }

            p.Sku = (EditSku ?? "").Trim();
            p.Name = (EditName ?? "").Trim();
            p.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();
            p.CostPrice = cost;
            p.Price = sell; // selling price (per unit OR per inch)
            p.VatInclusive = EditVatInclusive;
            p.IsLength = EditIsLength;

            var inv = await db.Inventory.FirstOrDefaultAsync(x => x.ProductId == p.Id && x.LocationCode == "DEFAULT");
            if (inv == null)
            {
                inv = new InventoryBalance
                {
                    ProductId = p.Id,
                    LocationCode = "DEFAULT",
                    OnHand = 0m,
                    OnHandInches = 0
                };
                db.Inventory.Add(inv);
            }

            if (!p.IsLength)
            {
                inv.OnHand = onHandQty;
                inv.OnHandInches = 0;
            }
            else
            {
                inv.OnHand = 0m;
                inv.OnHandInches = onHandInches;
            }

            await db.SaveChangesAsync();

            EditorStatus = "Saved.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            EditorStatus = "Save failed: " + ex.Message;
        }
    }

    public async Task DeleteAsync()
    {
        if (_editingId == null)
        {
            EditorStatus = "Select an item to delete.";
            return;
        }

        try
        {
            var options = BuildDbOptions();
            await using var db = new PosLocalDbContext(options);

            await db.Database.EnsureCreatedAsync();

            var p = await db.Products.FirstAsync(x => x.Id == _editingId.Value);
            p.DeletedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            EditorStatus = "Deleted.";
            NewItem();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            EditorStatus = "Delete failed: " + ex.Message;
        }
    }

    // -----------------------------
    // Internal helpers
    // -----------------------------
    private void ApplySearch()
    {
        var term = (Search ?? "").Trim();

        IEnumerable<ProductListItemVm> filtered = _all;
        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = _all.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        Products.Clear();
        foreach (var p in filtered)
            Products.Add(p);
    }

    private void LoadSelectedIntoEditor()
    {
        if (Selected == null)
        {
            _editingId = null;
            EditorStatus = "Select an item or click New.";
            return;
        }

        _editingId = Selected.Id;
        EditSku = Selected.Sku ?? "";
        EditName = Selected.Name ?? "";
        EditDescription = Selected.Description ?? "";
        EditCostPriceText = Selected.CostPrice.ToString("0.00", CultureInfo.InvariantCulture);
        EditSellingPriceText = Selected.SellingPrice.ToString("0.00", CultureInfo.InvariantCulture);
        EditVatInclusive = Selected.VatInclusive;
        EditIsLength = Selected.IsLength;

        if (!Selected.IsLength)
        {
            EditOnHandQtyText = Selected.OnHandQty.ToString("0.###", CultureInfo.InvariantCulture);
            EditFeetText = "0";
            EditInchesText = "0";
        }
        else
        {
            var fi = LengthConverter.FromTotalInches(Selected.OnHandInches);
            EditFeetText = fi.Feet.ToString(CultureInfo.InvariantCulture);
            EditInchesText = fi.Inches.ToString(CultureInfo.InvariantCulture);
            EditOnHandQtyText = "0";
        }

        EditorStatus = "Editing item. Update and click Save.";
    }

    private List<string> ValidateEditor(out decimal cost, out decimal sell, out decimal onHandQty, out int onHandInches)
    {
        var errs = new List<string>();

        cost = 0m;
        sell = 0m;
        onHandQty = 0m;
        onHandInches = 0;

        if (string.IsNullOrWhiteSpace(EditName))
            errs.Add("Item Name is required.");

        if (!TryParseDecimal(EditCostPriceText, out cost) || cost < 0)
            errs.Add("Cost Price must be a valid number (>= 0).");

        if (!TryParseDecimal(EditSellingPriceText, out sell) || sell < 0)
            errs.Add("Selling Price must be a valid number (>= 0).");

        if (!EditIsLength)
        {
            if (!TryParseDecimal(EditOnHandQtyText, out onHandQty) || onHandQty < 0)
                errs.Add("Quantity must be a valid number (>= 0).");
            onHandInches = 0;
        }
        else
        {
            if (!TryParseNonNegativeInt(EditFeetText, out var ft)) ft = 0;
            if (!TryParseNonNegativeInt(EditInchesText, out var inch)) inch = 0;

            var norm = LengthConverter.Normalize(ft, inch);
            onHandInches = LengthConverter.ToTotalInches(norm.Feet, norm.Inches);
            onHandQty = 0m;
        }

        return errs;
    }

    private static bool TryParseDecimal(string text, out decimal value)
    {
        text = (text ?? "").Trim();

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            return true;

        value = 0m;
        return false;
    }

    private static bool TryParseNonNegativeInt(string text, out int value)
    {
        text = (text ?? "").Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;
        return value >= 0;
    }

    // ✅ SAME DB OPTIONS as Terminal + Customers
    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
    => DataLocalDb.BuildOptions();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// -----------------------------
// List item VM for left list
// -----------------------------
public sealed class ProductListItemVm
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Sku { get; init; }
    public string? Description { get; init; }
    public decimal CostPrice { get; init; }
    public decimal SellingPrice { get; init; }
    public bool VatInclusive { get; init; }
    public bool IsLength { get; init; }

    public decimal OnHandQty { get; init; }
    public int OnHandInches { get; init; }

    public string SkuLine => $"SKU: {Sku}";
    public string PriceLine => $"Price: {SellingPrice:0.00}";
    public string FlagsLine => $"VAT Incl: {VatInclusive} | Length: {IsLength}";

    public string StockLine
    {
        get
        {
            if (!IsLength) return $"Stock: {OnHandQty:0.###}";
            var fi = LengthConverter.FromTotalInches(OnHandInches);
            return $"Stock: {fi.Feet} ft {fi.Inches} in ({OnHandInches} in)";
        }
    }

    public static ProductListItemVm From(Product p, InventoryBalance? bal)
    {
        return new ProductListItemVm
        {
            Id = p.Id,
            Name = p.Name,
            Sku = p.Sku,
            Description = p.Description,
            CostPrice = p.CostPrice,
            SellingPrice = p.Price,
            VatInclusive = p.VatInclusive,
            IsLength = p.IsLength,
            OnHandQty = bal?.OnHand ?? 0m,
            OnHandInches = bal?.OnHandInches ?? 0
        };
    }
}
