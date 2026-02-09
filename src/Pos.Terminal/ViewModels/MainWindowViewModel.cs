// File: src/Pos.Terminal/ViewModels/MainWindowViewModel.cs
// Replace the ENTIRE file with this (copy/paste).
//
// ✅ Uses ONE DB file for Terminal/Inventory/Customers: pos.local.db
// ✅ Customer REQUIRED for all checkouts
// ✅ Customer clears after checkout
// ✅ Modern Terminal UX bindings: categories, search, cart qty controls, toast

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

using DataLocalDb = Pos.Local.Data.LocalDb;

using Avalonia.Threading;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using Microsoft.EntityFrameworkCore;

using Pos.Application.Checkout;
using Pos.Application.Tax;

using Pos.Local.Data;
using Pos.Local.Entities;
using Pos.Local.Services;

using Pos.Terminal.Commands;
using Pos.Terminal.Models;
using Pos.Terminal.Services;
using Pos.Terminal.Views;
using System.Drawing;
using System.Drawing.Printing;

// enum aliases for mapping
using AppLineKind = Pos.Application.Checkout.LineQuantityKind;
using LocalLineKind = Pos.Local.Entities.LineQuantityKind;

namespace Pos.Terminal.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly CheckoutCalculator _checkout = new(new VatCalculator());
    private readonly SettingsStore _settingsStore = new();

    // -----------------------------
    // Shell / navigation
    // -----------------------------
    private object? _currentView;
    public object? CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    private string _pageTitle = "Terminal";
    public string PageTitle
    {
        get => _pageTitle;
        set { _pageTitle = value; OnPropertyChanged(); }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

     private string _headerTitle = "ModernPOS";
    public string HeaderTitle
    {
        get => _headerTitle;
        set { _headerTitle = value; OnPropertyChanged(); }
    }

    private AvaloniaBitmap? _headerImage;
    public AvaloniaBitmap? HeaderImage
    {
        get => _headerImage;
        set { _headerImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHeaderImage)); }
    }

    public bool HasHeaderImage => HeaderImage != null;

    // -----------------------------
    // Toast (short, non-blocking notifications)
    // -----------------------------
    private readonly DispatcherTimer _toastTimer;

    private string _toastMessage = "";
    public string ToastMessage
    {
        get => _toastMessage;
        private set { _toastMessage = value; OnPropertyChanged(); }
    }

    private bool _isToastVisible;
    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set { _isToastVisible = value; OnPropertyChanged(); }
    }

    public void Toast(string message, int milliseconds = 2200)
    {
        ToastMessage = message;
        IsToastVisible = true;

        _toastTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(800, milliseconds));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // -----------------------------
    // Terminal data (TerminalView expects these)
    // -----------------------------
    public ObservableCollection<ProductDto> Products { get; } = new();
    public ObservableCollection<CartLine> CartLines { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public bool IsCartEmpty => CartLines.Count == 0;
    public bool IsCartNotEmpty => CartLines.Count > 0;
    public bool IsCustomerSelected => SelectedCustomerId != null;
    public bool IsCartNotEmptyAndCustomerSelected => IsCartNotEmpty && IsCustomerSelected;

    private List<ProductDto> _allProducts = new();

    private string _selectedCategory = "All";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "All" : value;
            if (_selectedCategory == v) return;
            _selectedCategory = v;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private ProductDto? _selectedProduct;
    public ProductDto? SelectedProduct
    {
        get => _selectedProduct;
        set { _selectedProduct = value; OnPropertyChanged(); }
    }

    private CartLine? _selectedCartLine;
    public CartLine? SelectedCartLine
    {
        get => _selectedCartLine;
        set { _selectedCartLine = value; OnPropertyChanged(); }
    }

    // -----------------------------
    // Search (barcode scanners usually "type" then send Enter)
    // -----------------------------
    private string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            if (_search == value) return;
            _search = value ?? "";
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public string TerminalId { get; set; } = "TILL-01";

    // -----------------------------
    // Totals shown in UI
    // -----------------------------
    public decimal Subtotal => Math.Round(CartLines.Sum(x => x.LineTotal), 2);
    public decimal VatTotal => Math.Round(ComputeTotals().Vat, 2);
    public decimal GrandTotal => Math.Round(ComputeTotals().Gross, 2);

    private decimal _discountAmount;
    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            var v = Math.Round(Math.Max(0m, value), 2);
            if (_discountAmount == v) return;
            _discountAmount = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalDue));
        }
    }

    public decimal TotalDue => Math.Round(Math.Max(0m, GrandTotal - DiscountAmount), 2);

    // -----------------------------
    // Commands
    // -----------------------------
    public ICommand AddToCartCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ClearCartCommand { get; }
    public ICommand IncreaseQtyCommand { get; }
    public ICommand DecreaseQtyCommand { get; }
    public ICommand EditQtyCommand { get; }

    // View-bridge: TerminalView assigns this to show dialogs
    public Func<CartLine, Task>? EditQtyRequested { get; set; }

    // -----------------------------
    // Customer selection
    // -----------------------------
    private Guid? _selectedCustomerId;
    public Guid? SelectedCustomerId
    {
        get => _selectedCustomerId;
        set
        {
            if (_selectedCustomerId == value) return;
            _selectedCustomerId = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCustomerLabel));
            OnPropertyChanged(nameof(IsCustomerSelected));
            OnPropertyChanged(nameof(IsCartNotEmptyAndCustomerSelected));
        }
    }

    private string _selectedCustomerName = "None";
    public string SelectedCustomerLabel =>
        SelectedCustomerId == null ? "None" : _selectedCustomerName;

    public void ClearCustomer()
    {
        SelectedCustomerId = null;
        _selectedCustomerName = "None";

        // SelectedCustomerId setter already raises most props,
        // but we also raise label to be safe.
        OnPropertyChanged(nameof(SelectedCustomerLabel));
    }

    // -----------------------------
    // Ctor
    // -----------------------------
    public MainWindowViewModel()
    {
        CartLines.CollectionChanged += (_, __) =>
        {
            RaiseTotalsChanged();
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _toastTimer.Tick += (_, __) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        AddToCartCommand = new RelayCommand(p =>
        {
            if (p is ProductDto prod) AddToCart(prod);
        });

        RemoveLineCommand = new RelayCommand(p =>
        {
            if (p is CartLine line) RemoveLine(line);
        });

        ClearCartCommand = new RelayCommand(_ => ClearCart());

        IncreaseQtyCommand = new RelayCommand(p =>
        {
            if (p is CartLine line) IncreaseQty(line);
        });

        DecreaseQtyCommand = new RelayCommand(p =>
        {
            if (p is CartLine line) DecreaseQty(line);
        });

        EditQtyCommand = new AsyncRelayCommand(async p =>
        {
            if (p is not CartLine line) return;
            if (EditQtyRequested != null) await EditQtyRequested(line);
        });

        ShowTerminal();

        _ = LoadHeaderAsync();
    }

    // -----------------------------
    // Navigation
    // -----------------------------
    public async void ShowTerminal()
    {
        PageTitle = "Terminal";
        CurrentView = new TerminalView { DataContext = this };
        await LoadAsync();
    }

    public async void ShowInventory()
    {
        PageTitle = "Inventory";
        var vm = new InventoryViewModel();
        CurrentView = new InventoryView { DataContext = vm };
        await vm.LoadAsync();
    }

    public async void ShowCustomers()
    {
        PageTitle = "Customers";
        var vm = new CustomersViewModel(isPicker: false);
        CurrentView = new CustomersView { DataContext = vm };
        await vm.LoadAsync();
    }

    public async void ShowReports()
    {
        PageTitle = "Reports";
        var vm = new ReportsViewModel();
        CurrentView = new ReportsView { DataContext = vm };
        await vm.LoadAllAsync();
    }

     public async void ShowSettings()
    {
        PageTitle = "Settings";
        var vm = new SettingsViewModel(_settingsStore, ApplyHeaderSettings);
        CurrentView = new SettingsView { DataContext = vm };
        await vm.LoadAsync();
    }

    // TerminalView expects this
    public void SelectCustomerFromTerminal() => ShowCustomerPicker();

    private async void ShowCustomerPicker()
    {
        PageTitle = "Select Customer";

        var vm = new CustomersViewModel(
            isPicker: true,
            onPicked: async pickedId =>
            {
                if (pickedId != null)
                {
                    await using var db2 = CreateLocalDb();
                    var c = await db2.Customers.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == pickedId.Value);

                    _selectedCustomerName = c?.Name ?? "Unknown";
                    SelectedCustomerId = pickedId.Value;

                    OnPropertyChanged(nameof(SelectedCustomerLabel));
                    Toast($"Customer: {_selectedCustomerName}");
                }

                ShowTerminal();
                await Task.CompletedTask;
            });

        CurrentView = new CustomersView { DataContext = vm };
        await vm.LoadAsync();
    }

    // -----------------------------
    // Load products (and stock) from the SAME DB as Inventory/Customers
    // -----------------------------
    public async Task LoadAsync()
    {
        try
        {
            Status = "Loading products...";
            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var products = await db.Products.AsNoTracking()
                .Where(p => p.IsActive && p.DeletedAtUtc == null)
                .OrderBy(p => p.Name)
                .ToListAsync();

            // DEFAULT location (change later if you support multiple)
            var balances = await db.Inventory.AsNoTracking()
                .Where(b => b.LocationCode == "DEFAULT")
                .ToListAsync();

            var balanceByProduct = balances
                .GroupBy(b => b.ProductId)
                .ToDictionary(g => g.Key, g => g.First());

            _allProducts = products.Select(p =>
            {
                balanceByProduct.TryGetValue(p.Id, out var bal);
                return new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Sku = p.Sku,
                    Price = p.Price,
                    VatInclusive = p.VatInclusive,
                    IsLength = p.IsLength,
                    Department = string.IsNullOrWhiteSpace(p.Department) ? "Uncategorized" : p.Department,
                    OnHand = bal?.OnHand ?? 0m,
                    OnHandInches = bal?.OnHandInches ?? 0
                };
            }).ToList();

            BuildCategories();
            ApplyFilters();

            Status = $"Loaded {_allProducts.Count} products ({DataLocalDb.DefaultDbPath})";
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
            Toast("Failed to load products");
        }
    }

    private void BuildCategories()
    {
        Categories.Clear();
        Categories.Add("All");

        foreach (var dept in _allProducts
                     .Select(p => p.Department ?? "Uncategorized")
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
        {
            if (!Categories.Contains(dept))
                Categories.Add(dept);
        }

        if (string.IsNullOrWhiteSpace(SelectedCategory))
            SelectedCategory = "All";
    }

    private void ApplyFilters()
    {
        var term = (Search ?? "").Trim();
        var cat = (SelectedCategory ?? "All").Trim();

        IEnumerable<ProductDto> filtered = _allProducts;

        if (!string.Equals(cat, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => string.Equals(p.Department ?? "", cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = filtered.Where(p =>
                (!string.IsNullOrWhiteSpace(p.Name) && p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        Products.Clear();
        foreach (var p in filtered.OrderBy(x => x.Name))
            Products.Add(p);
    }

    // Enter in search box calls this
    public void TryAddFirstSearchMatch()
    {
        var term = (Search ?? "").Trim();
        if (string.IsNullOrWhiteSpace(term)) return;

        // Prefer exact SKU match for barcode scanners
        var exactSku = _allProducts.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.Sku) && string.Equals(p.Sku, term, StringComparison.OrdinalIgnoreCase));

        var match = exactSku ?? _allProducts.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.Name) && p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            Toast("No match");
            return;
        }

        AddToCart(match);
        Search = "";
    }

    // -----------------------------
    // Cart actions
    // -----------------------------
    public void AddToCart(ProductDto p)
    {
        var existing = CartLines.FirstOrDefault(x => x.ProductId == p.Id);
        if (existing != null)
        {
            if (!existing.IsLength) existing.BumpUnit(+1);
            else existing.BumpInches(+1);

            RaiseTotalsChanged();
            Toast($"Added: {p.Name}");
            return;
        }

        var line = new CartLine
        {
            ProductId = p.Id,
            Name = p.Name,
            UnitPrice = p.Price,
            VatInclusive = p.VatInclusive,
            IsLength = p.IsLength,
            Qty = p.IsLength ? 0m : 1m,
            QtyInches = p.IsLength ? 1 : 0
        };

        line.PropertyChanged += (_, __) => RaiseTotalsChanged();

        CartLines.Add(line);
        SelectedCartLine = line;
        RaiseTotalsChanged();
        Toast($"Added: {p.Name}");
    }

    public void IncreaseQty(CartLine line)
    {
        if (line.IsLength) line.BumpInches(+1);
        else line.BumpUnit(+1);
        RaiseTotalsChanged();
    }

    public void DecreaseQty(CartLine line)
    {
        if (line.IsLength)
        {
            line.BumpInches(-1);
            if (line.QtyInches <= 0) CartLines.Remove(line);
        }
        else
        {
            line.BumpUnit(-1);
            if (line.Qty <= 0m) CartLines.Remove(line);
        }

        RaiseTotalsChanged();
    }

    public void RemoveLine(CartLine line)
    {
        CartLines.Remove(line);
        RaiseTotalsChanged();
    }

    public void ClearCart()
    {
        CartLines.Clear();
        DiscountAmount = 0m;
        RaiseTotalsChanged();
        Toast("Cart cleared");
    }

    // -----------------------------
    // Checkout -> SAME DB
    // -----------------------------
    private bool EnsureCustomerSelected()
    {
        if (SelectedCustomerId != null) return true;

        Toast("Select a customer first");
        Status = "Checkout blocked: customer required";
        return false;
    }

    public async Task CheckoutCashAsync(decimal cashGiven)
    {
        if (CartLines.Count == 0)
        {
            Toast("Cart is empty");
            return;
        }

        if (!EnsureCustomerSelected())
            return;

        try
        {
            Status = "Saving CASH sale locally...";

            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var saleService = new LocalSaleService(db);

            var result = await saleService.CreateCashSaleAsync(
                terminalId: TerminalId,
                lines: CartLines.Select(x => new LocalCartLine
                {
                    ProductId = x.ProductId,
                    QuantityKind = x.IsLength ? LocalLineKind.Inches : LocalLineKind.Unit,
                    Qty = x.Qty,
                    QtyInches = x.QtyInches
                }).ToList(),
                cashGiven: cashGiven,
                customerId: SelectedCustomerId,          // required now
                allowNegativeStock: false
            );

            Status = $"Saved locally. Receipt {result.ReceiptNo} Total {result.Total:0.00} Change {result.Change:0.00}";
            Toast($"Saved: {result.ReceiptNo}");

            var totalDue = TotalDue;
            var changeDue = Math.Round(
                cashGiven - totalDue,
                2,
                MidpointRounding.AwayFromZero);

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: "CASH",
                total: totalDue,
                cashGiven: cashGiven,
                change: changeDue);

             ClearCart();
            ClearCustomer();
        }
        catch (Exception ex)
        {
            Status = $"Checkout failed: {ex.Message}";
            Toast("Checkout failed");
        }
    }

    public async Task CheckoutCardAsync(string method)
    {
        if (CartLines.Count == 0)
        {
            Toast("Cart is empty");
            return;
        }

        if (!EnsureCustomerSelected())
            return;

        try
        {
            Status = $"Saving {method} sale locally...";

            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var saleService = new LocalSaleService(db);

            var result = await saleService.CreateCardSaleAsync(
                terminalId: TerminalId,
                lines: CartLines.Select(x => new LocalCartLine
                {
                    ProductId = x.ProductId,
                    QuantityKind = x.IsLength ? LocalLineKind.Inches : LocalLineKind.Unit,
                    Qty = x.Qty,
                    QtyInches = x.QtyInches
                }).ToList(),
                method: method,
                customerId: SelectedCustomerId,          // required now
                allowNegativeStock: false
            );

            Status = $"Saved locally. Receipt {result.ReceiptNo} Total ${result.Total:0.00} ({method})";
            Toast($"Saved: {result.ReceiptNo}");

            var totalDue = TotalDue;

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: method,
                total: totalDue,
                cashGiven: totalDue,
                change: 0m);

            ClearCart();
            ClearCustomer();
        }
        catch (Exception ex)
        {
            Status = $"Checkout failed: {ex.Message}";
            Toast("Checkout failed");
        }
    }

    public async Task CheckoutOnAccountAsync()
    {
        if (CartLines.Count == 0)
        {
            Toast("Cart is empty");
            return;
        }

        if (!EnsureCustomerSelected())
            return;

        try
        {
            Status = "Saving ON ACCOUNT sale locally...";

            await using var db = CreateLocalDb();
            await db.Database.EnsureCreatedAsync();

            var saleService = new LocalSaleService(db);

            var result = await saleService.CreateOnAccountSaleAsync(
                terminalId: TerminalId,
                lines: CartLines.Select(x => new LocalCartLine
                {
                    ProductId = x.ProductId,
                    QuantityKind = x.IsLength ? LocalLineKind.Inches : LocalLineKind.Unit,
                    Qty = x.Qty,
                    QtyInches = x.QtyInches
                }).ToList(),
                customerId: SelectedCustomerId!.Value,
                allowNegativeStock: false
            );

            Status = $"Saved locally. Receipt {result.ReceiptNo} Total ${result.Total:0.00} (ON ACCOUNT)";
            Toast($"Saved: {result.ReceiptNo}");

            var totalDue = TotalDue;

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: "ON ACCOUNT",
                total: totalDue,
                cashGiven: 0m,
                change: 0m);

            ClearCart();
            ClearCustomer();
        }
        catch (Exception ex)
        {
            Status = $"Checkout failed: {ex.Message}";
            Toast("Checkout failed");
        }
    }

    // -----------------------------
    // Helpers
    // -----------------------------
    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(VatTotal));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(TotalDue));
        OnPropertyChanged(nameof(DiscountAmount));

        OnPropertyChanged(nameof(IsCartEmpty));
        OnPropertyChanged(nameof(IsCartNotEmpty));
        OnPropertyChanged(nameof(IsCartNotEmptyAndCustomerSelected));
    }

    public void RefreshTotals()
    {
        RaiseTotalsChanged();
    }

    private (decimal Net, decimal Vat, decimal Gross) ComputeTotals()
    {
        if (CartLines.Count == 0) return (0m, 0m, 0m);

        var lineTotals = new List<CheckoutLineTotals>();

        foreach (var l in CartLines)
        {
            var kind = l.IsLength ? AppLineKind.Inches : AppLineKind.Unit;

            var calc = _checkout.CalculateLine(
                productId: l.ProductId,
                productName: l.Name,
                enteredSellingPrice: l.UnitPrice,
                vatInclusive: l.VatInclusive,
                quantityKind: kind,
                qty: l.Qty,
                qtyInches: l.QtyInches
            );

            lineTotals.Add(new CheckoutLineTotals(
                l.ProductId,
                l.Name,
                kind,
                l.Qty,
                l.QtyInches,
                calc.UnitNet,
                calc.UnitVat,
                calc.UnitGross,
                calc.NetTotal,
                calc.VatTotal,
                calc.GrossTotal
            ));
        }

        var totals = _checkout.SumTotals(lineTotals);
        return (totals.Net, totals.Vat, totals.Gross);
    }

     private async Task LoadHeaderAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        ApplyHeaderSettings(settings);
    }

    private void ApplyHeaderSettings(AppSettings settings)
    {
        HeaderTitle = string.IsNullOrWhiteSpace(settings.HeaderTitle) ? "ModernPOS" : settings.HeaderTitle;
        HeaderImage = LoadBitmap(settings.HeaderImagePath);
    }

    private static AvaloniaBitmap? LoadBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return null;

        try
        {
            return new AvaloniaBitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private sealed class InvoicePrintState
    {
        public int ItemIndex;
        public List<(string desc, decimal amount)> Items = new();
    }

    private async Task PrintReceiptAsync(
        string receiptNo,
        string paymentMethod,
        decimal total,
        decimal cashGiven,
        decimal change)
    {
        var settings = await _settingsStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
            return;

        if (!OperatingSystem.IsWindows())
        {
            Status = "Receipt printing is only supported on Windows.";
            return;
        }

        ReceiptCustomerInfo customerInfo;
        {
            var name = _selectedCustomerName;
            var phone = "N/A";
            var email = "N/A";

            if (SelectedCustomerId is not null)
            {
                await using var db = CreateLocalDb();
                var customer = await db.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == SelectedCustomerId.Value);

                if (customer is not null)
                {
                    name = string.IsNullOrWhiteSpace(customer.Name) ? name : customer.Name;
                    phone = string.IsNullOrWhiteSpace(customer.Phone) ? phone : customer.Phone;
                    email = string.IsNullOrWhiteSpace(customer.Email) ? email : customer.Email;
                }
            }

            customerInfo = new ReceiptCustomerInfo(name, phone, email);
        }

       var state = new InvoicePrintState
        {
            ItemIndex = 0,
            Items = CartLines.Select(line =>
            {
                var qtyLabel = line.IsLength ? $"{line.QtyInches:0.##} in" : $"{line.Qty:0.##}";
                var desc = $"{line.Name}\nQty: {qtyLabel}  @  {line.UnitPrice:0.00}";
                return (desc, line.LineTotal);
            }).ToList()
        };

        try
        {
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
                DocumentName = $"Invoice {receiptNo}"
            };

            doc.DefaultPageSettings.PaperSize = new PaperSize("Letter", 850, 1100);
            doc.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);

            doc.PrintPage += (_, e) =>
            {
                e.HasMorePages = DrawInvoiceLetterPage(
                    g: e.Graphics,
                    pageBounds: e.PageBounds,
                    margins: doc.DefaultPageSettings.Margins,
                    settings: settings,
                    receiptNo: receiptNo,
                    invoiceDate: DateTime.Now,
                    customer: customerInfo,
                    paymentMethod: paymentMethod,
                    total: total,
                    cashGiven: cashGiven,
                    change: change,
                    state: state
                );
            };

            doc.Print();
            Status = $"Invoice sent to {settings.ReceiptPrinterName}";
        }
        catch (Exception ex)
        {
            Status = $"Print failed: {ex.Message}";
        }
    }

    private static bool DrawInvoiceLetterPage(
        Graphics g,
        Rectangle pageBounds,
        Margins margins,
        AppSettings settings,
        string receiptNo,
        DateTime invoiceDate,
        ReceiptCustomerInfo customer,
        string paymentMethod,
        decimal total,
        decimal cashGiven,
        decimal change,
        InvoicePrintState state)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float dpiX = g.DpiX;
        float dpiY = g.DpiY;

        float left = (margins.Left / 100f) * dpiX;
        float right = (margins.Right / 100f) * dpiX;
        float top = (margins.Top / 100f) * dpiY;
        float bottom = (margins.Bottom / 100f) * dpiY;

        float pageW = pageBounds.Width;
        float pageH = pageBounds.Height;

        var content = new RectangleF(
            x: left,
            y: top,
            width: pageW - left - right,
            height: pageH - top - bottom
        );

        using var fontCompany = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var fontSmall = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var fontSmallBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var fontTitle = new Font("Segoe UI", 22f, FontStyle.Bold);
        using var fontSection = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fontTableHeader = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fontTable = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        using var pen = new Pen(Color.Gray, 1f);
        using var penDark = new Pen(Color.DimGray, 1f);
        using var brushHeaderFill = new SolidBrush(Color.FromArgb(220, 230, 245));

        static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

        var sfNearTop = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
        var sfFarTop = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

        float y = content.Top;

        float headerH = 110f;
        var headerRect = new RectangleF(content.Left, y, content.Width, headerH);

        var companyRect = new RectangleF(headerRect.Left, headerRect.Top, headerRect.Width * 0.55f, headerRect.Height);
        var companyText =
            $"{Safe(settings.CompanyName)}\n" +
            $"{Safe(settings.CompanyAddress)}\n" +
            $"{Safe(settings.CompanyContact)}";
        g.DrawString(companyText, fontCompany, Brushes.Black, companyRect);

        var titleRect = new RectangleF(headerRect.Left + headerRect.Width * 0.55f, headerRect.Top, headerRect.Width * 0.45f, 40f);
        var sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
        g.DrawString("INVOICE", fontTitle, new SolidBrush(Color.FromArgb(95, 135, 200)), titleRect, sfRight);

        float boxW = headerRect.Width * 0.45f;
        float boxX = headerRect.Right - boxW;
        float boxY = headerRect.Top + 50f;
        float boxH = 48f;

        var infoBox = new RectangleF(boxX + (boxW * 0.35f), boxY, boxW * 0.65f, boxH);
        g.DrawRectangle(penDark, infoBox.X, infoBox.Y, infoBox.Width, infoBox.Height);

        float colW = infoBox.Width / 2f;
        float rowH = infoBox.Height / 2f;

        g.FillRectangle(brushHeaderFill, infoBox.X, infoBox.Y, colW, rowH);
        g.FillRectangle(brushHeaderFill, infoBox.X + colW, infoBox.Y, colW, rowH);

        g.DrawLine(penDark, infoBox.X + colW, infoBox.Y, infoBox.X + colW, infoBox.Bottom);
        g.DrawLine(penDark, infoBox.X, infoBox.Y + rowH, infoBox.Right, infoBox.Y + rowH);

        var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString("INVOICE #", fontSmallBold, Brushes.Black, new RectangleF(infoBox.X, infoBox.Y, colW, rowH), sfCenter);
        g.DrawString("DATE", fontSmallBold, Brushes.Black, new RectangleF(infoBox.X + colW, infoBox.Y, colW, rowH), sfCenter);

        g.DrawString(receiptNo, fontSmall, Brushes.Black, new RectangleF(infoBox.X, infoBox.Y + rowH, colW, rowH), sfCenter);
        g.DrawString(invoiceDate.ToString("yyyy-MM-dd"), fontSmall, Brushes.Black, new RectangleF(infoBox.X + colW, infoBox.Y + rowH, colW, rowH), sfCenter);

        y += headerH + 10f;

        float billToW = content.Width * 0.45f;
        float billToH = 95f;

        var billToRect = new RectangleF(content.Left, y, billToW, billToH);
        g.DrawRectangle(penDark, billToRect.X, billToRect.Y, billToRect.Width, billToRect.Height);

        var billToHeader = new RectangleF(billToRect.X, billToRect.Y, billToRect.Width, 18f);
        g.FillRectangle(brushHeaderFill, billToHeader);
        g.DrawRectangle(penDark, billToHeader.X, billToHeader.Y, billToHeader.Width, billToHeader.Height);
        g.DrawString(
            "BILL TO",
            fontSection,
            Brushes.Black,
            new RectangleF(billToHeader.X + 6, billToHeader.Y, billToHeader.Width - 12, billToHeader.Height),
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

        var billTextRect = new RectangleF(billToRect.X + 8, billToRect.Y + 24, billToRect.Width - 16, billToRect.Height - 28);
        var billText =
            $"{Safe(customer.Name)}\n" +
            $"{Safe(customer.Phone)}\n" +
            $"{Safe(customer.Email)}";
          g.DrawString(billText, fontSmall, Brushes.Black, billTextRect);

        y += billToH + 12f;

        float tableX = content.Left;
        float tableW = content.Width;
        float tableTop = y;

        float rowHeight = 20f;
        float headerHeight = 22f;

        float descW = tableW * 0.78f;
        float amtW = tableW - descW;

        var hdrRect = new RectangleF(tableX, tableTop, tableW, headerHeight);
        g.DrawRectangle(penDark, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
        g.FillRectangle(brushHeaderFill, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
        g.DrawLine(penDark, tableX + descW, tableTop, tableX + descW, tableTop + headerHeight);

         g.DrawString(
            "DESCRIPTION",
            fontTableHeader,
            Brushes.Black,
            new RectangleF(tableX + 6, tableTop, descW - 12, headerHeight),
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

         g.DrawString(
            "AMOUNT",
            fontTableHeader,
            Brushes.Black,
            new RectangleF(tableX + descW + 6, tableTop, amtW - 12, headerHeight),
            new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

        float bodyTop = tableTop + headerHeight;
        float bodyH = content.Bottom - bodyTop - 90f;
        var bodyRect = new RectangleF(tableX, bodyTop, tableW, bodyH);
        g.DrawRectangle(penDark, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);

        float curY = bodyTop;
        int startIndex = state.ItemIndex;

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();

        while (state.ItemIndex < state.Items.Count)
        {
            var (desc, amount) = state.Items[state.ItemIndex];
            int lines = 1 + desc.Count(c => c == '\n');
            float thisRowH = Math.Max(rowHeight, lines * 16f);

            if (curY + thisRowH > bodyRect.Bottom - 4)
                break;

            g.DrawLine(pen, tableX, curY + thisRowH, tableX + tableW, curY + thisRowH);
            g.DrawLine(pen, tableX + descW, curY, tableX + descW, curY + thisRowH);

            var descRect = new RectangleF(tableX + 6, curY + 3, descW - 12, thisRowH - 6);
            var amtRect = new RectangleF(tableX + descW + 6, curY + 3, amtW - 12, thisRowH - 6);

            g.DrawString(desc, fontTable, Brushes.Black, descRect, sfNearTop);
            g.DrawString(amount.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, amtRect, sfFarTop);

            curY += thisRowH;
            state.ItemIndex++;
        }

        float footerTop = content.Bottom - 78f;

        g.DrawString(
            "Thank you for your business!",
            fontSmall,
            Brushes.Black,
            new RectangleF(content.Left, footerTop, content.Width * 0.65f, 22f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        float totalBoxW = content.Width * 0.35f;
        float totalBoxH = 34f;
        var totalRect = new RectangleF(content.Right - totalBoxW, footerTop, totalBoxW, totalBoxH);
        g.DrawRectangle(penDark, totalRect.X, totalRect.Y, totalRect.Width, totalRect.Height);

        float totalLabelW = totalBoxW * 0.45f;
        g.DrawLine(penDark, totalRect.X + totalLabelW, totalRect.Y, totalRect.X + totalLabelW, totalRect.Bottom);

        g.DrawString(
            "TOTAL",
            fontSmallBold,
            Brushes.Black,
            new RectangleF(totalRect.X + 6, totalRect.Y, totalLabelW - 12, totalBoxH),
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

        g.DrawString(
            total.ToString("C", CultureInfo.CurrentCulture),
            fontSmallBold,
            Brushes.Black,
            new RectangleF(totalRect.X + totalLabelW + 6, totalRect.Y, totalBoxW - totalLabelW - 12, totalBoxH),
            new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

        var contactLine = $"If you have any questions about this invoice, please contact {Safe(settings.CompanyContact)}";
        g.DrawString(
            contactLine,
            fontSmall,
            Brushes.DimGray,
            new RectangleF(content.Left, content.Bottom - 26f, content.Width, 18f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        var payLine = paymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase)
            ? $"Payment: CASH   Cash: {cashGiven:0.00}   Change: {change:0.00}"
            : $"Payment: {paymentMethod}";
        g.DrawString(
            payLine,
            fontSmall,
            Brushes.DimGray,
            new RectangleF(content.Left, content.Bottom - 46f, content.Width, 18f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        bool hasMore = state.ItemIndex < state.Items.Count;

        if (hasMore && state.ItemIndex == startIndex)
            state.ItemIndex = Math.Min(state.Items.Count, startIndex + 1);

        return hasMore;
    }

    private sealed record ReceiptCustomerInfo(string Name, string Phone, string Email);


    // ✅ The ONE shared DB config used by ALL modules
    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
    => DataLocalDb.BuildOptions();

    private static PosLocalDbContext CreateLocalDb()
    {
        var options = BuildDbOptions();
        return new PosLocalDbContext(options);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}