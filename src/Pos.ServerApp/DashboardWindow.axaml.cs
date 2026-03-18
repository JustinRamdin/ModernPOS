using System.Linq;
using System.IO;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Microsoft.Extensions.Hosting;
using Pos.Contracts;
using Pos.Server.Hosting;
using Pos.ServerApp.Services;

namespace Pos.ServerApp;

public partial class DashboardWindow : Window
{
    private readonly ServerAppSettings _settings;
    private readonly ServerAdminApi _api;
    private byte[]? _headerImageBytes;
    private byte[]? _logoImageBytes;
    private IHost? _host;

    public DashboardWindow() : this(new ServerAppSettings())
    {
    }
    public DashboardWindow(ServerAppSettings settings, IHost? existingHost = null)
    {
        InitializeComponent();
        _settings = settings;
        _api = new ServerAdminApi("127.0.0.1", _settings.Port);
        _host = existingHost;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _host ??= await ModernPosServerHost.StartAsync(new ModernPosServerOptions(_settings.ConnectionString, _settings.Port, _settings.CompanyName));
        await RefreshDashboardAsync();
        await LoadCompanyProfileAsync();
    }

    private async Task RefreshDashboardAsync()
    {
        var dto = await _api.GetDashboardAsync();
        if (dto is null) return;
        CompanyText.Text = $"Company: {dto.CompanyName}";
        PortText.Text = $"Port: {dto.Port}";
        DbText.Text = $"Database: {dto.DatabasePath}";
        BackupText.Text = $"Last backup: {dto.LastBackupAtUtc?.ToLocalTime().ToString() ?? "Never"}";
        EnableSchedule.IsChecked = dto.Schedule.Enabled;
        BackupFolder.Text = dto.Schedule.BackupFolder;
        RetentionBox.Text = dto.Schedule.RetentionCount.ToString();
    }

    private async Task LoadCompanyProfileAsync()
    {
        var profile = await _api.GetCompanyProfileAsync();
        if (profile is null)
            return;

        ProfileCompanyNameBox.Text = profile.CompanyName;
        AddressLine1Box.Text = profile.AddressLine1;
        AddressLine2Box.Text = profile.AddressLine2;
        PhoneBox.Text = profile.Phone;
        EmailBox.Text = profile.Email;
        TaxRegistrationBox.Text = profile.TaxRegistrationNumber;
        HeaderTitleBox.Text = profile.HeaderTitle;
        ReceiptFooterBox.Text = profile.ReceiptFooter;
        LogoScaleBox.SelectedIndex = Math.Clamp(profile.LogoScaleMultiplier, 1, 4) - 1;
        _headerImageBytes = profile.HeaderImage;
        _logoImageBytes = profile.LogoImage;
        HeaderImageStatusText.Text = DescribeImage(profile.HeaderImage, "Header image");
        LogoStatusText.Text = DescribeImage(profile.LogoImage, "Logo");
    }

    private async void Backup_Click(object? sender, RoutedEventArgs e)
    {
        await _api.TriggerBackupAsync(BackupFolder.Text);
        await RefreshDashboardAsync();
        StatusText.Text = "Backup completed.";
    }

    private async void SaveSchedule_Click(object? sender, RoutedEventArgs e)
    {
        var settings = new ScheduledBackupSettings(EnableSchedule.IsChecked == true, BackupFolder.Text ?? AppContext.BaseDirectory, new TimeOnly(2, 0), int.TryParse(RetentionBox.Text, out var r) ? r : 14);
        await _api.SaveScheduleAsync(settings);
        StatusText.Text = "Schedule saved.";
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        await _api.RestoreAsync(RestoreFileBox.Text ?? string.Empty);
        StatusText.Text = "Restore completed. Restart server app recommended.";
    }
    private async void SaveCompanyProfile_Click(object? sender, RoutedEventArgs e)
    {
        var request = new UpdateCompanyProfileRequest(
            ProfileCompanyNameBox.Text ?? string.Empty,
            AddressLine1Box.Text ?? string.Empty,
            AddressLine2Box.Text ?? string.Empty,
            PhoneBox.Text ?? string.Empty,
            EmailBox.Text ?? string.Empty,
            TaxRegistrationBox.Text ?? string.Empty,
            ReceiptFooterBox.Text ?? string.Empty,
            HeaderTitleBox.Text ?? string.Empty,
            _headerImageBytes,
            _logoImageBytes,
            (LogoScaleBox.SelectedIndex >= 0 ? LogoScaleBox.SelectedIndex : 0) + 1);

        await _api.SaveCompanyProfileAsync(request);
        await RefreshDashboardAsync();
        await LoadCompanyProfileAsync();
        StatusText.Text = "Shared company profile saved.";
    }

    private async void ReloadCompanyProfile_Click(object? sender, RoutedEventArgs e)
    {
        await LoadCompanyProfileAsync();
        StatusText.Text = "Shared company profile reloaded.";
    }

    private async void UploadHeaderImage_Click(object? sender, RoutedEventArgs e)
    {
        _headerImageBytes = await PickImageBytesAsync("Select header image");
        HeaderImageStatusText.Text = DescribeImage(_headerImageBytes, "Header image");
    }

    private void ClearHeaderImage_Click(object? sender, RoutedEventArgs e)
    {
        _headerImageBytes = null;
        HeaderImageStatusText.Text = "Header image cleared.";
    }

    private async void UploadLogo_Click(object? sender, RoutedEventArgs e)
    {
        _logoImageBytes = await PickImageBytesAsync("Select logo image");
        LogoStatusText.Text = DescribeImage(_logoImageBytes, "Logo");
    }

    private void ClearLogo_Click(object? sender, RoutedEventArgs e)
    {
        _logoImageBytes = null;
        LogoStatusText.Text = "Logo cleared.";
    }

    private async Task<byte[]?> PickImageBytesAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static string DescribeImage(byte[]? bytes, string label)
        => bytes is { Length: > 0 } ? $"{label} loaded ({bytes.Length} bytes)." : $"{label} not set.";
}
