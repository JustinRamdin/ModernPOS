using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Pos.Contracts;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _store;
    private readonly SharedCompanyProfileService _companyProfileService;
    private readonly Action<AppSettings>? _onSaved;
    private readonly Action<bool>? _onPracticeModeToggled;

    public ObservableCollection<string> Printers { get; } = new();

    private string _companyContact = "";
    public string CompanyContact
    {
        get => _companyContact;
        set { _companyContact = value ?? ""; OnPropertyChanged(); }
    }

    private string _selectedPrinter = "";
    public string SelectedPrinter
    {
        get => _selectedPrinter;
        set { _selectedPrinter = value ?? ""; OnPropertyChanged(); }
    }

    private bool _isVatEnabled = true;
    public bool IsVatEnabled
    {
        get => _isVatEnabled;
        set { _isVatEnabled = value; OnPropertyChanged(); }
    }

    private string _vatRatePercent = "12.5";
    public string VatRatePercent
    {
        get => _vatRatePercent;
        set { _vatRatePercent = value ?? ""; OnPropertyChanged(); }
    }

    private bool _useTspReceiptStyle;
    public bool UseTspReceiptStyle
    {
        get => _useTspReceiptStyle;
        set { _useTspReceiptStyle = value; OnPropertyChanged(); }
    }
    private bool _isDualInventoryEnabled;
    public bool IsDualInventoryEnabled
    {
        get => _isDualInventoryEnabled;
        set { _isDualInventoryEnabled = value; OnPropertyChanged(); }
    }
    private bool _isPracticeMode;
    public bool IsPracticeMode
    {
        get => _isPracticeMode;
        private set
        {
            _isPracticeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PracticeModeButtonText));
            OnPropertyChanged(nameof(PracticeModeStatus));
        }
    }

    public string PracticeModeButtonText => IsPracticeMode ? "Exit Practice Mode" : "Enter Practice Mode";
    public string PracticeModeStatus => IsPracticeMode
        ? "Practice mode is ON. Sales and inventory changes stay local and do not update the server."
        : "Practice mode is OFF. Sales and inventory changes use the live server.";

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }


    private CompanyProfileDto? _sharedProfile;
    public CompanyProfileDto? SharedProfile
    {
        get => _sharedProfile;
        private set
        {
            _sharedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SharedCompanyName));
            OnPropertyChanged(nameof(SharedAddress));
            OnPropertyChanged(nameof(SharedContact));
            OnPropertyChanged(nameof(SharedTaxRegistrationNumber));
            OnPropertyChanged(nameof(SharedReceiptFooter));
            OnPropertyChanged(nameof(SharedHeaderTitle));
            OnPropertyChanged(nameof(CompanyProfileStatus));
        }
    }

    public string SharedCompanyName => SharedProfile?.CompanyName ?? "Not available";
    public string SharedAddress => string.Join(Environment.NewLine, new[] { SharedProfile?.AddressLine1, SharedProfile?.AddressLine2 }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string SharedContact => string.Join(" | ", new[] { SharedProfile?.Phone, SharedProfile?.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string SharedTaxRegistrationNumber => SharedProfile?.TaxRegistrationNumber ?? string.Empty;
    public string SharedReceiptFooter => SharedProfile?.ReceiptFooter ?? string.Empty;
    public string SharedHeaderTitle => SharedProfile?.HeaderTitle ?? string.Empty;
    public string CompanyProfileStatus => SharedProfile == null
        ? "Shared company profile could not be loaded."
        : "Business identity is managed centrally on the server and used by all connected clients.";

    private string _printerStatus = "";
    public string PrinterStatus
    {
        get => _printerStatus;
        private set { _printerStatus = value; OnPropertyChanged(); }
    }

    public SettingsViewModel(SettingsStore store, Action<AppSettings>? onSaved, Action<bool>? onPracticeModeToggled = null)
    {
        _store = store;
        _onSaved = onSaved;
        _onPracticeModeToggled = onPracticeModeToggled;
        _companyProfileService = new SharedCompanyProfileService(store);
    }

    public async Task LoadAsync()
    {
        var settings = await _store.LoadAsync();
        SelectedPrinter = settings.ReceiptPrinterName;
        UseTspReceiptStyle = settings.UseTspReceiptStyle;
        IsVatEnabled = settings.IsVatEnabled;
        VatRatePercent = settings.VatRatePercent.ToString("0.##");
        IsPracticeMode = settings.IsPracticeMode;
        IsDualInventoryEnabled = settings.IsDualInventoryEnabled;

        await _store.ClearLegacyReceiptIdentityAsync();
        LoadPrinters();
        await RefreshSharedProfileAsync();
        StatusMessage = "Settings loaded.";
    }

    public void LoadPrinters()
    {
        Printers.Clear();
        PrinterStatus = "";

        if (!OperatingSystem.IsWindows())
        {
            PrinterStatus = "Printer listing is only available on Windows.";
            return;
        }

        foreach (string printer in PrinterSettings.InstalledPrinters)
            Printers.Add(printer);

        if (Printers.Count == 0)
        {
            PrinterStatus = "No printers detected.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPrinter) && !Printers.Contains(SelectedPrinter))
            Printers.Insert(0, SelectedPrinter);
            }

    public async Task RefreshSharedProfileAsync()
    {
        try
        {
            SharedProfile = await _companyProfileService.GetAsync();
        }
        catch (Exception ex)
        {
            SharedProfile = null;
            StatusMessage = $"Unable to load shared company profile: {ex.Message}";
        }
    }

    public async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            ReceiptPrinterName = SelectedPrinter.Trim(),
            UseTspReceiptStyle = UseTspReceiptStyle,
            IsVatEnabled = IsVatEnabled,
             VatRatePercent = ParseVatRatePercent(),
            IsPracticeMode = IsPracticeMode,
            IsDualInventoryEnabled = IsDualInventoryEnabled
        };

        await _store.SaveAsync(settings);
        _onSaved?.Invoke(settings);
        StatusMessage = "Terminal preferences saved.";
    }

    public async Task TogglePracticeModeAsync()
    {
        var settings = await _store.LoadAsync();
        settings.IsPracticeMode = !settings.IsPracticeMode;
        await _store.SaveAsync(settings);

        IsPracticeMode = settings.IsPracticeMode;
        _onSaved?.Invoke(settings);
        StatusMessage = settings.IsPracticeMode
            ? "Practice mode enabled. Restarting into practice mode..."
            : "Practice mode disabled. Restarting into live mode...";

        _onPracticeModeToggled?.Invoke(settings.IsPracticeMode);
    }

    private decimal ParseVatRatePercent()
    {
        if (decimal.TryParse(VatRatePercent, out var parsed))
            return Math.Round(Math.Clamp(parsed, 0m, 100m), 2);

        return 12.5m;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
