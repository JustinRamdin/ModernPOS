using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _store;
    private readonly Action<AppSettings>? _onSaved;

    public ObservableCollection<string> Printers { get; } = new();

    private string _companyName = "";
    public string CompanyName
    {
        get => _companyName;
        set { _companyName = value ?? ""; OnPropertyChanged(); }
    }

    private string _companyAddress = "";
    public string CompanyAddress
    {
        get => _companyAddress;
        set { _companyAddress = value ?? ""; OnPropertyChanged(); }
    }

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

    private string _headerTitle = "";
    public string HeaderTitle
    {
        get => _headerTitle;
        set { _headerTitle = value ?? ""; OnPropertyChanged(); }
    }

    private string _receiptRemarks = "";
    public string ReceiptRemarks
    {
        get => _receiptRemarks;
        set { _receiptRemarks = value ?? ""; OnPropertyChanged(); }
    }

    private string _headerImagePath = "";
    public string HeaderImagePath
    {
        get => _headerImagePath;
        private set { _headerImagePath = value ?? ""; OnPropertyChanged(); }
    }

    private Bitmap? _headerImage;
    public Bitmap? HeaderImage
    {
        get => _headerImage;
        private set { _headerImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasHeaderImage)); }
    }

    public bool HasHeaderImage => HeaderImage != null;

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
    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _printerStatus = "";
    public string PrinterStatus
    {
        get => _printerStatus;
        private set { _printerStatus = value; OnPropertyChanged(); }
    }

    public SettingsViewModel(SettingsStore store, Action<AppSettings>? onSaved)
    {
        _store = store;
        _onSaved = onSaved;
    }

    public async Task LoadAsync()
    {
        var settings = await _store.LoadAsync();
        CompanyName = settings.CompanyName;
        CompanyAddress = settings.CompanyAddress;
        CompanyContact = settings.CompanyContact;
        SelectedPrinter = settings.ReceiptPrinterName;
        HeaderTitle = settings.HeaderTitle;
        ReceiptRemarks = settings.ReceiptRemarks;
        HeaderImagePath = settings.HeaderImagePath;
        HeaderImage = LoadBitmap(settings.HeaderImagePath);
        IsVatEnabled = settings.IsVatEnabled;
        VatRatePercent = settings.VatRatePercent.ToString("0.##");

        LoadPrinters();
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
        {
            Printers.Add(printer);
        }

        if (Printers.Count == 0)
        {
            PrinterStatus = "No printers detected.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPrinter) && !Printers.Contains(SelectedPrinter))
        {
            Printers.Insert(0, SelectedPrinter);
        }
    }

    public async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            CompanyName = CompanyName.Trim(),
            CompanyAddress = CompanyAddress.Trim(),
            CompanyContact = CompanyContact.Trim(),
            ReceiptPrinterName = SelectedPrinter.Trim(),
            HeaderTitle = HeaderTitle.Trim(),
            ReceiptRemarks = ReceiptRemarks,
            HeaderImagePath = HeaderImagePath.Trim(),
            IsVatEnabled = IsVatEnabled,
            VatRatePercent = ParseVatRatePercent()
        };

        await _store.SaveAsync(settings);
        _onSaved?.Invoke(settings);
        StatusMessage = "Settings saved.";
    }

     private decimal ParseVatRatePercent()
    {
        if (decimal.TryParse(VatRatePercent, out var parsed))
        {
            return Math.Round(Math.Clamp(parsed, 0m, 100m), 2);
        }

        return 12.5m;
    }

    public async Task SetHeaderImageAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            StatusMessage = "Image not found.";
            return;
        }

        var destPath = CopyHeaderImage(sourcePath);
        HeaderImagePath = destPath;
        HeaderImage = LoadBitmap(destPath);
        StatusMessage = "Header image updated.";
        await Task.CompletedTask;
    }

    private static string CopyHeaderImage(string sourcePath)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(root, "ModernPOS", "branding");
        Directory.CreateDirectory(folder);

        var extension = Path.GetExtension(sourcePath);
        var destPath = Path.Combine(folder, $"header-logo{extension}");
        File.Copy(sourcePath, destPath, true);
        return destPath;
    }

    private static Bitmap? LoadBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
