using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Terminal.Commands;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class BackupViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();

    private string _backupFolder = string.Empty;
    public string BackupFolder
    {
        get => _backupFolder;
        set { _backupFolder = value ?? string.Empty; OnPropertyChanged(); }
    }

    private string _lastBackup = "Unknown";
    public string LastBackup
    {
        get => _lastBackup;
        set { _lastBackup = value; OnPropertyChanged(); }
    }

    private string _lastBackupPath = "Not run yet";
    public string LastBackupPath
    {
        get => _lastBackupPath;
        set { _lastBackupPath = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "Ready to request backup.";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            (RefreshStatusCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (RunBackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshStatusCommand { get; }
    public ICommand RunBackupCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BackupViewModel()
    {
        RefreshStatusCommand = new AsyncRelayCommand(_ => LoadStatusAsync(), _ => !IsBusy);
        RunBackupCommand = new AsyncRelayCommand(_ => RunBackupAsync(), _ => !IsBusy);

        _ = LoadStatusAsync();
    }

    public async Task LoadStatusAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading backup status...";

            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            var dashboard = await api.GetDashboardAsync();

            BackupFolder = dashboard.Schedule.BackupFolder;
            LastBackup = dashboard.LastBackupAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "No backups yet";
            StatusMessage = "Backup status loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load backup status: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RunBackupAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Requesting backup from server...";

            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            var response = await api.TriggerBackupAsync(string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim());

            LastBackupPath = response.Path;
            LastBackup = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            StatusMessage = "Backup completed successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
