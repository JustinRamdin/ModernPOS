using Avalonia.Controls;
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

    private async void Backup_Click(object? sender, RoutedEventArgs e)
    {
        await _api.TriggerBackupAsync(BackupFolder.Text);
        await RefreshDashboardAsync();
        StatusText.Text = "Backup completed.";
    }

    private async void SaveSchedule_Click(object? sender, RoutedEventArgs e)
    {
        var settings = new ScheduledBackupSettings(EnableSchedule.IsChecked == true, BackupFolder.Text ?? AppContext.BaseDirectory, new TimeOnly(2,0), int.TryParse(RetentionBox.Text, out var r) ? r : 14);
        await _api.SaveScheduleAsync(settings);
        StatusText.Text = "Schedule saved.";
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        await _api.RestoreAsync(RestoreFileBox.Text ?? string.Empty);
        StatusText.Text = "Restore completed. Restart server app recommended.";
    }
}
