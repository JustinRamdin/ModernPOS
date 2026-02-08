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

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: "CASH",
                total: result.Total,
                cashGiven: cashGiven,
                change: result.Change);

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

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: method,
                total: result.Total,
                cashGiven: result.Total,
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

            await PrintReceiptAsync(
                receiptNo: result.ReceiptNo,
                paymentMethod: "ON ACCOUNT",
                total: result.Total,
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

         var customerDetails = await LoadCustomerReceiptDetailsAsync(SelectedCustomerId);
         var receiptText = BuildReceiptText(settings, receiptNo, paymentMethod, total, cashGiven, change, customerDetails);

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

            using var doc = new PrintDocument { PrinterSettings = printerSettings };
            doc.DefaultPageSettings.PaperSize = new PaperSize("Letter", 850, 1100);
            doc.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
            doc.PrintPage += (_, e) =>
            {
                using var headerFont = new Font("Segoe UI", 12, FontStyle.Bold);
                using var bodyFont = new Font("Consolas", 9);
                var bounds = e.MarginBounds;
                float y = bounds.Top;

                if (!string.IsNullOrWhiteSpace(settings.HeaderImagePath) && File.Exists(settings.HeaderImagePath))
                {
                    using var headerImage = Image.FromFile(settings.HeaderImagePath);
                    var maxWidth = bounds.Width;
                    var scale = Math.Min(maxWidth / headerImage.Width, 1f);
                    var height = headerImage.Height * scale;
                    e.Graphics.DrawImage(headerImage, bounds.Left, y, maxWidth, height);
                    y += height + 10;
                }

                if (!string.IsNullOrWhiteSpace(settings.CompanyName))
                {
                    y = DrawWrappedLine(e.Graphics, settings.CompanyName.Trim(), headerFont, bounds, y);
                }

                foreach (var line in GetCompanyInfoLines(settings))
                {
                    y = DrawWrappedLine(e.Graphics, line, bodyFont, bounds, y);
                }

                if (!string.IsNullOrWhiteSpace(settings.CompanyName)
                    || !string.IsNullOrWhiteSpace(settings.CompanyAddress)
                    || !string.IsNullOrWhiteSpace(settings.CompanyContact))
                {
                    y += 6;
                }

                var bodyRect = new RectangleF(bounds.Left, y, bounds.Width, bounds.Bottom - y);
                e.Graphics.DrawString(receiptText, bodyFont, Brushes.Black, bodyRect);
            };

            doc.Print();
            Status = $"Receipt sent to {settings.ReceiptPrinterName}";
        }
        catch (Exception ex)
        {
            Status = $"Print failed: {ex.Message}";
        }
    }

    private string BuildReceiptText(
        AppSettings settings,
        string receiptNo,
        string paymentMethod,
        decimal total,
        decimal cashGiven,
        decimal change,
        CustomerReceiptDetails? customerDetails)
    {
        var sb = new StringBuilder();

        if (customerDetails != null)
        {
            sb.AppendLine($"Customer: {customerDetails.Name}");
            if (!string.IsNullOrWhiteSpace(customerDetails.Phone))
                sb.AppendLine($"Phone: {customerDetails.Phone}");
            if (!string.IsNullOrWhiteSpace(customerDetails.Email))
                sb.AppendLine($"Email: {customerDetails.Email}");
            sb.AppendLine(new string('-', 32));
        }

        sb.AppendLine($"Receipt: {receiptNo}");
        sb.AppendLine($"Date: {DateTime.Now:g}");
        sb.AppendLine($"Payment: {paymentMethod}");
        sb.AppendLine(new string('-', 32));

        foreach (var line in CartLines)
        {
            var qtyLabel = line.IsLength ? $"{line.QtyInches} in" : $"{line.Qty:0.##}";
            sb.AppendLine($"{TrimText(line.Name, 18),-18} {qtyLabel,6} {line.LineTotal,7:0.00}");
        }

        sb.AppendLine(new string('-', 32));
        sb.AppendLine($"Subtotal: {Subtotal,16:0.00}");
        sb.AppendLine($"VAT: {VatTotal,21:0.00}");
        sb.AppendLine($"Total: {total,19:0.00}");

        if (paymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"Cash: {cashGiven,20:0.00}");
            sb.AppendLine($"Change: {change,18:0.00}");
        }

        sb.AppendLine(new string('-', 32));
        sb.AppendLine("Thank you!");
        return sb.ToString();
    }

    private static float DrawWrappedLine(Graphics graphics, string text, Font font, RectangleF bounds, float y)
    {
       if (string.IsNullOrWhiteSpace(text))
            return y;

        var size = graphics.MeasureString(text, font, (int)bounds.Width);
        var rect = new RectangleF(bounds.Left, y, bounds.Width, size.Height);
        graphics.DrawString(text, font, Brushes.Black, rect);
        return y + size.Height;
    }

        private static IEnumerable<string> GetCompanyInfoLines(AppSettings settings)
    {
        foreach (var line in settings.CompanyAddress.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line.Trim());
        }
    }

    private static string TrimText(string text, int max)
    {
        yield return line.Trim();
        }

        foreach (var line in settings.CompanyContact.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return line.Trim();
    }

     private async Task<CustomerReceiptDetails?> LoadCustomerReceiptDetailsAsync(Guid? customerId)
    {
        if (customerId == null)
            return null;

        await using var db = CreateLocalDb();
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId.Value);

        if (customer == null)
        {
            var fallbackName = string.IsNullOrWhiteSpace(_selectedCustomerName) ? "Unknown" : _selectedCustomerName;
            return new CustomerReceiptDetails(fallbackName, "", "");
        }

        return new CustomerReceiptDetails(customer.Name, customer.Phone, customer.Email);
    }

    private sealed record CustomerReceiptDetails(string Name, string Phone, string Email);
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