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
using System.Drawing.Printing;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

using Pos.Application.Checkout;
using Pos.Application.Tax;

using Pos.Terminal.Commands;
using Pos.Terminal.Models;
using Pos.Terminal.Services;
using Pos.Terminal.Views;

// enum aliases for mapping
using AppLineKind = Pos.Application.Checkout.LineQuantityKind;

namespace Pos.Terminal.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
     private CheckoutCalculator _checkout = new(new VatCalculator());
    private readonly SettingsStore _settingsStore = new();
    private readonly SharedCompanyProfileService _companyProfileService = new();
    private AppSettings _settings = new();

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

    private bool _isPracticeMode;
    public bool IsPracticeMode
    {
        get => _isPracticeMode;
        private set
        {
            _isPracticeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPracticeModeBannerVisible));
            OnPropertyChanged(nameof(PracticeModeBannerText));
        }
    }

    public bool IsPracticeModeBannerVisible => IsPracticeMode;
    public string PracticeModeBannerText => "(practice mode on)";


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
    public decimal ZeroRatedTotal => Math.Round(CartLines.Where(x => x.ZeroRated).Sum(x => x.LineTotal), 2);
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
    public ICommand AddSearchItemCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ClearCartCommand { get; }
    public ICommand IncreaseQtyCommand { get; }
    public ICommand DecreaseQtyCommand { get; }
    public ICommand EditQtyCommand { get; }
    public ICommand OpenDrawerCommand { get; }

    // View-bridge: TerminalView assigns this to show dialogs
    public Func<CartLine, Task>? EditQtyRequested { get; set; }
    public Func<Task<ProductDto?>>? ItemLookupRequested { get; set; }

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
    private string _selectedCustomerPhone = "N/A";
    private string _selectedCustomerEmail = "N/A";
    private bool _selectedCustomerIsCompany;
    public string SelectedCustomerLabel =>
        SelectedCustomerId == null ? "None" : _selectedCustomerName;

    public void ClearCustomer()
    {
        SelectedCustomerId = null;
        _selectedCustomerName = "None";
        _selectedCustomerPhone = "N/A";
        _selectedCustomerEmail = "N/A";
        _selectedCustomerIsCompany = false;

        // SelectedCustomerId setter already raises most props,
        // but we also raise label to be safe.
        OnPropertyChanged(nameof(SelectedCustomerLabel));
        RaiseTotalsChanged();
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
        
        AddSearchItemCommand = new AsyncRelayCommand(async _ =>
        {
            if (ItemLookupRequested == null) return;
            var selected = await ItemLookupRequested();
            if (selected != null)
                AddToCart(selected);
                },
        onError: ex =>
        {
            Status = $"Unable to open item search. {ex.Message}";
            Toast("Item search failed.");
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

        OpenDrawerCommand = new AsyncRelayCommand(async _ => await OpenCashDrawerAsync());

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
        var settings = await _settingsStore.LoadAsync();
        CurrentView = BuildInventoryTabs(settings.IsDualInventoryEnabled);
    }

    public async void ShowCustomers()
    {
        PageTitle = "Customers";
        var vm = new CustomersViewModel(isPicker: false);
        CurrentView = new CustomersView { DataContext = vm };
        await vm.LoadAsync();
    }


     public async void ShowFinancial()
    {
        PageTitle = "Financial";
        var vm = new FinancialViewModel();
        CurrentView = new FinancialView { DataContext = vm };
        await vm.LoadAsync();
    }

    public async void ShowReports()
    {
        PageTitle = "Reports";
        var settings = await _settingsStore.LoadAsync();
        CurrentView = BuildReportsTabs(settings.IsDualInventoryEnabled);
    }

     public async void ShowSettings()
    {
        PageTitle = "Settings";
        var vm = new SettingsViewModel(_settingsStore, ApplyHeaderSettings);
        CurrentView = new SettingsView { DataContext = vm };
        await vm.LoadAsync();
    }

    private static TabControl BuildInventoryTabs(bool isDualInventoryEnabled)
    {
        var inventory1 = new InventoryView { DataContext = new InventoryViewModel(1) };
        var inventory2 = new InventoryView { DataContext = new InventoryViewModel(2) };

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "Inventory 1", Content = inventory1 },
                new TabItem { Header = "Inventory 2", Content = inventory2, IsEnabled = isDualInventoryEnabled }
            }
        };
    }

    private static TabControl BuildReportsTabs(bool isDualInventoryEnabled)
    {
        var reports1Vm = new ReportsViewModel(1);
        var reports2Vm = new ReportsViewModel(2);
        _ = reports1Vm.LoadAllAsync();
        if (isDualInventoryEnabled)
            _ = reports2Vm.LoadAllAsync();

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "Reports 1", Content = new ReportsView { DataContext = reports1Vm } },
                new TabItem { Header = "Reports 2", Content = new ReportsView { DataContext = reports2Vm }, IsEnabled = isDualInventoryEnabled }
            }
        };
    }

    public async void ShowUserManagement()
    {
        PageTitle = "User Management";
        var vm = new UserManagementViewModel();
        CurrentView = new UserManagementView { DataContext = vm };
        await vm.LoadAsync();
    }

    public async void ShowBackup()
    {
        PageTitle = "Request Backup";
        var vm = new BackupViewModel();
        CurrentView = new BackupView { DataContext = vm };
        await vm.LoadStatusAsync();
    }

    public async void ShowUpdates()
    {
        PageTitle = "Updates";
        var vm = new UpdatesViewModel();
        CurrentView = new UpdatesView { DataContext = vm };
        await vm.LoadAsync();
    }

    // TerminalView expects this
    public void SelectCustomerFromTerminal() => ShowCustomerPicker();

    private async void ShowCustomerPicker()
    {
        PageTitle = "Select Customer";

        CustomersViewModel? vm = null;
        vm = new CustomersViewModel(
            isPicker: true,
            onPicked: async pickedId =>
            {
                if (pickedId != null)
                {
                    var selected = vm?.Selected;

                     _selectedCustomerName = !string.IsNullOrWhiteSpace(selected?.Name)
                        ? selected.Name
                        : "Unknown";
                    _selectedCustomerPhone = !string.IsNullOrWhiteSpace(selected?.Phone)
                        ? selected.Phone
                        : "N/A";
                    _selectedCustomerEmail = !string.IsNullOrWhiteSpace(selected?.Email)
                        ? selected.Email
                        : "N/A";
                    _selectedCustomerIsCompany = selected?.IsCompany == true;
                    SelectedCustomerId = pickedId.Value;

                    OnPropertyChanged(nameof(SelectedCustomerLabel));
                    RaiseTotalsChanged();
                    Toast($"Customer: {_selectedCustomerName}");
                }

                ShowTerminal();
                await Task.CompletedTask;
            });

        CurrentView = new CustomersView { DataContext = vm };
        await vm.LoadAsync();
    }

    // -----------------------------
    // Load products from the connected server only.
    // -----------------------------
    public async Task LoadAsync()
    {
        try
        {
            Status = "Loading products from server...";
            var deploy = await _settingsStore.LoadDeploymentAsync();
            var settings = await _settingsStore.LoadAsync();

            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            var remoteProducts = await api.GetProductsAsync();

             _allProducts = remoteProducts
                .Where(p => settings.IsDualInventoryEnabled || p.InventoryBucket == 1)
                .OrderBy(p => p.Name)
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Sku = p.Sku,
                    Description = p.Description ?? "",
                    Location = p.Location,
                    Price = p.Price,
                    VatInclusive = p.VatInclusive,
                    ZeroRated = p.ZeroRated,
                    IsLength = p.IsLength,
                    Department = string.IsNullOrWhiteSpace(p.Department) ? "Uncategorized" : p.Department,
                    OnHand = p.OnHand,
                    OnHandInches = p.OnHandInches,
                    InventoryBucket = p.InventoryBucket
                })
                .ToList();

            BuildCategories();
            ApplyFilters();

            Status = $"Loaded {_allProducts.Count} products from server";
        }
        catch (Exception ex)
        {
            _allProducts = [];
            Products.Clear();
            Status = BuildServerStatusMessage(ex, "load products");
            Toast("Server request failed.");
        }
    }


    public async Task<bool> IsServerReachableAsync()
    {
        try
        {
            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.ValidateServerAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
     public async Task HandleServerDisconnectedAsync()
    {
        Status = "Server unavailable. Waiting for server...";
        PageTitle = "Waiting for server";
        CurrentView = new WaitingForServerView { DataContext = this };
        await Task.CompletedTask;
    }

    public async Task HandleServerReconnectedAsync()
    {
        Status = "Server reconnected. Syncing...";
        Toast("Server reconnected. Resuming workflow.");
        ShowTerminal();
        await Task.CompletedTask;
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

    public IReadOnlyList<ProductDto> FindInventoryItems(string term)
    {
        var query = (term ?? "").Trim();
        IEnumerable<ProductDto> source = _allProducts;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(p =>
                (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Name) && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Description) && p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(p.Location) && p.Location.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        return source.OrderBy(p => p.Name).ToList();
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
        var existingBucket = CartLines.FirstOrDefault()?.InventoryBucket;
        if (existingBucket is not null && existingBucket.Value != p.InventoryBucket)
        {
            Toast($"Cannot mix Inventory {existingBucket.Value} and Inventory {p.InventoryBucket} items in one sale.");
            Status = "Sale blocked: inventory lists cannot be mixed.";
            return;
        }

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
            ItemNumber = p.Sku,
            Name = p.Name,
            ItemDescription = p.Description,
            Unit = p.Unit,
            UnitPrice = p.Price,
            VatInclusive = p.VatInclusive,
            ZeroRated = p.ZeroRated,
            IsLength = p.IsLength,
            Qty = p.IsLength ? 0m : 1m,
            QtyInches = p.IsLength ? 1 : 0,
            InventoryBucket = p.InventoryBucket
        };

        line.PropertyChanged += (_, __) => RaiseTotalsChanged();

        CartLines.Add(line);
        SelectedCartLine = line;
        RaiseTotalsChanged();
        Toast($"Added: {p.Name}");
    }

    public void AddMiscellaneousItem(string name, string description, decimal quantity, decimal unitPrice, bool vatInclusive)
    {
        var line = new CartLine
        {
            IsMiscellaneous = true,
            ProductId = Pos.Contracts.CheckoutSpecialProducts.MiscellaneousId,
            ItemNumber = "MISC",
            Name = name.Trim(),
            ItemDescription = description.Trim(),
            Unit = "ea",
            UnitPrice = unitPrice,
            VatInclusive = vatInclusive,
            Qty = quantity,
            QtyInches = 0,
            InventoryBucket = CartLines.FirstOrDefault()?.InventoryBucket ?? 1
        };

        line.PropertyChanged += (_, __) => RaiseTotalsChanged();
        CartLines.Add(line);
        SelectedCartLine = line;
        RaiseTotalsChanged();
        Toast($"Added: {line.Name}");
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

        var totalDue = TotalDue;
        if (cashGiven < totalDue)
        {
            Toast("Insufficient cash");
            Status = $"Checkout blocked: total due is {totalDue:0.00}";
            return;
        }

            try
        {
            var serverResult = await TryCheckoutServerAsync(paymentMethod: "CASH", paymentMethodCode: 1, paidAmount: cashGiven);
            if (serverResult != null)
            {
                var changeDue = Math.Round(cashGiven - totalDue, 2, MidpointRounding.AwayFromZero);
                await PrintReceiptAsync(
                    receiptNo: $"SRV-{serverResult.SaleId.ToString("N")[..8]}",
                    paymentMethod: "CASH",
                    subtotal: Subtotal,
                    discount: DiscountAmount,
                    vat: VatTotal,
                    totalDue: totalDue,
                    cashGiven: cashGiven,
                    change: changeDue);

             ClearCart();
                ClearCustomer();
                return;
            }
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
            var totalDue = TotalDue;
            var paymentCode = string.Equals(method, "DEBIT", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
            var serverResult = await TryCheckoutServerAsync(paymentMethod: method, paymentMethodCode: paymentCode, paidAmount: totalDue);
            if (serverResult != null)
            {
                await PrintReceiptAsync(
                    receiptNo: $"SRV-{serverResult.SaleId.ToString("N")[..8]}",
                    paymentMethod: method,
                    subtotal: Subtotal,
                    discount: DiscountAmount,
                    vat: VatTotal,
                    totalDue: totalDue,
                    cashGiven: totalDue,
                    change: 0m);

                ClearCart();
                ClearCustomer();
                return;
            }
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
            var totalDue = TotalDue;
            var serverResult = await TryCheckoutServerAsync(paymentMethod: "ON ACCOUNT", paymentMethodCode: 5, paidAmount: 0m);
            if (serverResult != null)
            {
                await PrintReceiptAsync(
                    receiptNo: $"SRV-{serverResult.SaleId.ToString("N")[..8]}",
                    paymentMethod: "ON ACCOUNT",
                    subtotal: Subtotal,
                    discount: DiscountAmount,
                    vat: VatTotal,
                    totalDue: totalDue,
                    cashGiven: 0m,
                    change: 0m);

                ClearCart();
                ClearCustomer();
            }
        }
        catch (Exception ex)
        {
            Status = $"Checkout failed: {ex.Message}";
            Toast("Checkout failed");
        }
    }

     private async Task<RemoteServerApi.ServerCheckoutResponse?> TryCheckoutServerAsync(string paymentMethod, int paymentMethodCode, decimal paidAmount)
    {
        try
        {
            Status = $"Submitting {paymentMethod} checkout to server...";
            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);

            var request = new Pos.Contracts.CheckoutRequest(
                TerminalId,
                CartLines.Select(x => new Pos.Contracts.CheckoutLineRequest(
                    ProductId: x.ProductId,
                    Qty: x.IsLength ? x.QtyInches : x.Qty,
                    OverrideUnitPrice: x.IsMiscellaneous ? x.UnitPrice : null,
                    VatTotal: x.TaxAmount,
                    GrossTotal: Math.Round(
                        x.LineTotal + ((!x.VatInclusive || _selectedCustomerIsCompany) ? x.TaxAmount : 0m),
                        2,
                        MidpointRounding.AwayFromZero))).ToList(),
                [new Pos.Contracts.CheckoutPaymentRequest(paymentMethodCode, paidAmount)],
                SelectedCustomerId,
                DiscountAmount,
                NetSubtotal: ComputeTotals().Net,
                VatTotal: VatTotal,
                TotalDue: TotalDue);

            var result = await api.CheckoutAsync(request);
            Status = $"Checkout completed on server. Sale {result.SaleId}";
            Toast("Checkout completed");
            return result;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            Status = BuildServerStatusMessage(ex, "submit checkout");
            Toast("Server checkout unavailable.");
            throw;
        }
         catch (HttpRequestException ex)
        {
            Status = BuildServerStatusMessage(ex, "submit checkout");
            Toast("Server rejected checkout; sale not saved locally.");
            throw;
        }
    }
    // -----------------------------
    // Helpers
    // -----------------------------
    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(VatTotal));
        OnPropertyChanged(nameof(ZeroRatedTotal));
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
        _checkout = new CheckoutCalculator(new VatCalculator(_settings.VatRatePercent / 100m, _settings.IsVatEnabled));

        if (CartLines.Count == 0) return (0m, 0m, 0m);

        var lineTotals = new List<CheckoutLineTotals>();

        foreach (var l in CartLines)
        {
            var lineCheckout = new CheckoutCalculator(new VatCalculator(
                _settings.VatRatePercent / 100m,
                _settings.IsVatEnabled && !l.ZeroRated));
            var kind = l.IsLength ? AppLineKind.Inches : AppLineKind.Unit;

            var calc = lineCheckout.CalculateLine(
                productId: l.ProductId,
                productName: l.Name,
                enteredSellingPrice: l.UnitPrice,
                vatInclusive: _selectedCustomerIsCompany ? false : l.VatInclusive,
                quantityKind: kind,
                qty: l.Qty,
                qtyInches: l.QtyInches
            );

            l.TaxAmount = calc.VatTotal;

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
        _settings = await _settingsStore.LoadAsync();

        try
        {
            var profile = await _companyProfileService.GetAsync();
            HeaderTitle = string.IsNullOrWhiteSpace(profile.HeaderTitle) ? profile.CompanyName : profile.HeaderTitle;
            HeaderImage = LoadBitmap(profile.HeaderImage);
        }
        catch
        {
            HeaderTitle = "ModernPOS";
            HeaderImage = null;
        }

        RaiseTotalsChanged();
        var settings = await _settingsStore.LoadAsync();
        ApplyHeaderSettings(settings);
    }

    private void ApplyHeaderSettings(AppSettings settings)
    {
        _settings = settings ?? new AppSettings();
        IsPracticeMode = _settings.IsPracticeMode;
        RaiseTotalsChanged();
    }

    private static AvaloniaBitmap? LoadBitmap(byte[]? imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
            return null;

        try
        {
            return new AvaloniaBitmap(new MemoryStream(imageBytes, writable: false));
        }
        catch
        {
            return null;
        }
    }

    private async Task PrintReceiptAsync(
        string receiptNo,
        string paymentMethod,
        decimal subtotal,
        decimal discount,
        decimal vat,
        decimal totalDue,
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

        PhysicalReceiptRenderer.ReceiptCustomerInfo customerInfo;
        {
            var name = _selectedCustomerName;
            var phone = _selectedCustomerPhone;
            var email = _selectedCustomerEmail;

            customerInfo = new PhysicalReceiptRenderer.ReceiptCustomerInfo(name, phone, email);
        }

#pragma warning disable CA1416
        var state = PhysicalReceiptRenderer.CreateState(
            CartLines.Select(line => new PhysicalReceiptRenderer.ReceiptRenderLine(
                line.Name,
                line.IsLength,
                line.Qty,
                line.QtyInches,
                line.UnitPrice,
                line.LineTotal)));
#pragma warning restore CA1416

        try
        {
             var companyProfile = await _companyProfileService.GetAsync();

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

             // Use wider print margins so content stays inside printable bounds on
            // printers with larger hardware non-printable areas.
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
                        receiptNo: receiptNo,
                        invoiceDate: DateTime.Now,
                        customer: customerInfo,
                        paymentMethod: paymentMethod,
                        subtotal: subtotal,
                        discount: discount,
                        vat: vat,
                        zeroRated: ZeroRatedTotal,
                        totalDue: totalDue,
                        totalTendered: cashGiven,
                        change: change,
                        state: state
                    );
                    e.HasMorePages = false;
                    return;
                }

                e.HasMorePages = PhysicalReceiptRenderer.DrawInvoiceLetterPage(
                    g: e.Graphics,
                    marginBounds: e.MarginBounds,
                    companyProfile: companyProfile,
                    receiptNo: receiptNo,
                    invoiceDate: DateTime.Now,
                    customer: customerInfo,
                    paymentMethod: paymentMethod,
                    subtotal: subtotal,
                    discount: discount,
                    vat: vat,
                    zeroRated: ZeroRatedTotal,
                    totalDue: totalDue,
                    totalTendered: cashGiven,
                    change: change,
                    state: state
                );
            };
    #pragma warning restore CA1416
            doc.Print();
            Status = $"Invoice sent to {settings.ReceiptPrinterName}";

            if (paymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase))
            {
                if (!CashDrawerService.TryOpen(settings.ReceiptPrinterName, out var drawerError))
                    Status = $"Invoice printed. Cash drawer signal failed: {drawerError}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Print failed: {ex.Message}";
        }
    }


    public async Task OpenCashDrawerAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
        {
            Status = "No receipt printer configured.";
            Toast("Set a receipt printer first in Settings.");
            return;
        }

        if (!CashDrawerService.TryOpen(settings.ReceiptPrinterName, out var error))
        {
            Status = $"Cash drawer signal failed: {error}";
            Toast("Unable to open cash drawer.");
            return;
        }

        Status = "Cash drawer opened.";
        Toast("Cash drawer opened.");
    }

    // ✅ The ONE shared DB config used by ALL modules
     private static string BuildServerStatusMessage(Exception ex, string operation)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode is null)
                return $"Cannot reach server while trying to {operation}: {httpEx.Message}";

            return $"Server error while trying to {operation} ({(int)httpEx.StatusCode} {httpEx.StatusCode}): {httpEx.Message}";
        }

        return $"Failed to {operation}: {ex.Message}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
