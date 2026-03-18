using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Contracts;
using Pos.Terminal.Commands;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class BackupViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();

    private sealed record ScheduleOption(string Key, string Label, string Description, TimeOnly? LocalTime)
    {
        public override string ToString() => Label;
    }

    private sealed record RetentionOption(string Label, string Description, int Count)
    {
        public override string ToString() => Label;
    }

    private static readonly IReadOnlyList<ScheduleOption> ScheduleOptionsList =
    [
        new("overnight", "Every night at 2:00 AM", "Best when the shop is closed and the server is idle.", new TimeOnly(2, 0)),
        new("morning", "Every morning at 8:00 AM", "Creates a fresh backup before the day gets busy.", new TimeOnly(8, 0)),
        new("midday", "Every day at 12:00 PM", "Useful when you want a daytime recovery point.", new TimeOnly(12, 0)),
        new("close", "Every evening at 6:00 PM", "Good for backing up after closing tasks are done.", new TimeOnly(18, 0)),
        new("custom", "Pick my own time", "Use a custom daily time that fits your workflow.", null)
    ];

    private static readonly IReadOnlyList<RetentionOption> RetentionOptionsList =
    [
        new("Keep the last 7 backups", "One week of restore points.", 7),
        new("Keep the last 14 backups", "Two weeks of restore points.", 14),
        new("Keep the last 30 backups", "Recommended for most stores.", 30),
        new("Keep the last 90 backups", "Longer retention with more disk usage.", 90),
        new("Keep everything", "No automatic cleanup.", 0)
    ];

    public IReadOnlyList<object> ScheduleOptions { get; } = ScheduleOptionsList.Cast<object>().ToList();
    public IReadOnlyList<object> RetentionOptions { get; } = RetentionOptionsList.Cast<object>().ToList();

    private string _backupFolder = string.Empty;
    public string BackupFolder
    {
        get => _backupFolder;
        set
        {
            _backupFolder = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBackupFolder));
            OnPropertyChanged(nameof(BackupFolderSummary));
        }
    }

    public bool HasBackupFolder => !string.IsNullOrWhiteSpace(BackupFolder);
    public string BackupFolderSummary => HasBackupFolder
        ? $"Backups will be saved in: {BackupFolder}"
        : "No folder selected yet. The server default folder will be used.";

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

    private bool _scheduledBackupsEnabled;
    public bool ScheduledBackupsEnabled
    {
        get => _scheduledBackupsEnabled;
        set
        {
            _scheduledBackupsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    private object? _selectedScheduleOption;
    public object? SelectedScheduleOption
    {
        get => _selectedScheduleOption;
        set
        {
            _selectedScheduleOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomTimeVisible));
            OnPropertyChanged(nameof(SelectedScheduleDescription));
            OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool IsCustomTimeVisible => (SelectedScheduleOption as ScheduleOption)?.Key == "custom";
    public string SelectedScheduleDescription => (SelectedScheduleOption as ScheduleOption)?.Description
        ?? "Choose when the server should automatically create backups.";

    private string _customScheduleTime = "02:00";
    public string CustomScheduleTime
    {
        get => _customScheduleTime;
        set
        {
            _customScheduleTime = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    private object? _selectedRetentionOption;
    public object? SelectedRetentionOption
    {
        get => _selectedRetentionOption;
        set
        {
            _selectedRetentionOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRetentionDescription));
            OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public string SelectedRetentionDescription => (SelectedRetentionOption as RetentionOption)?.Description
        ?? "Choose how many scheduled backups should be kept automatically.";

    private string _restoreFolder = string.Empty;
    public string RestoreFolder
    {
        get => _restoreFolder;
        set
        {
            _restoreFolder = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRestoreFolder));
        }
    }

    public bool HasRestoreFolder => !string.IsNullOrWhiteSpace(RestoreFolder);

    private string _restoreFilePath = string.Empty;
    public string RestoreFilePath
    {
        get => _restoreFilePath;
        set
        {
            _restoreFilePath = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRestore));
            (RestoreBackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanRestore => !IsBusy && !string.IsNullOrWhiteSpace(RestoreFilePath);

    public string ScheduleSummary
    {
        get
        {
            if (!ScheduledBackupsEnabled)
                return "Automatic backups are turned off.";

            var retention = (SelectedRetentionOption as RetentionOption)?.Label ?? "Keep the last 14 backups";
            var localTimeText = GetSelectedLocalTime() is { } localTime
                ? localTime.ToString("HH\\:mm")
                : CustomScheduleTime;

            return $"Automatic backups run every day at {localTimeText} and {retention.ToLowerInvariant()}.";
        }
    }
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRestore));
            (RefreshStatusCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (RunBackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SaveScheduleCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (RestoreBackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshStatusCommand { get; }
    public ICommand RunBackupCommand { get; }
    public ICommand SaveScheduleCommand { get; }
    public ICommand RestoreBackupCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BackupViewModel()
    {
        SelectedScheduleOption = ScheduleOptionsList[0];
        SelectedRetentionOption = RetentionOptionsList[1];

        RefreshStatusCommand = new AsyncRelayCommand(_ => LoadStatusAsync(), _ => !IsBusy);
        RunBackupCommand = new AsyncRelayCommand(_ => RunBackupAsync(), _ => !IsBusy);
        SaveScheduleCommand = new AsyncRelayCommand(_ => SaveScheduleAsync(), _ => !IsBusy);
        RestoreBackupCommand = new AsyncRelayCommand(_ => RestoreBackupAsync(), _ => CanRestore);

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
            LastBackupPath = HasBackupFolder
                ? $"Latest backups are stored in {BackupFolder}"
                : "No backup file has been recorded yet.";
            ApplySchedule(dashboard.Schedule);
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

    public async Task SaveScheduleAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Saving scheduled backup settings...";

            var localTime = GetSelectedLocalTime()
                ?? throw new InvalidOperationException("Enter a valid custom time using HH:mm, for example 18:30.");
            var retentionCount = (SelectedRetentionOption as RetentionOption)?.Count ?? 14;

            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.SaveBackupScheduleAsync(new ScheduledBackupSettings(
                ScheduledBackupsEnabled,
                string.IsNullOrWhiteSpace(BackupFolder) ? string.Empty : BackupFolder.Trim(),
                ConvertLocalTimeToUtc(localTime),
                retentionCount));

            StatusMessage = ScheduledBackupsEnabled
                ? "Scheduled backup settings saved."
                : "Scheduled backups were turned off.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save schedule: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreBackupAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(RestoreFilePath))
                throw new InvalidOperationException("Select the backup file to restore.");

            IsBusy = true;
            StatusMessage = "Requesting restore on the server...";

            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.RestoreBackupAsync(RestoreFilePath.Trim());

            LastBackupPath = RestoreFilePath.Trim();
            StatusMessage = "Restore request sent successfully. Restart the server app if it does not reopen the restored data automatically.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetBackupFolder(string folderPath)
    {
        BackupFolder = folderPath?.Trim() ?? string.Empty;
        StatusMessage = HasBackupFolder
            ? "Backup destination updated. Save the schedule if you want automatic backups to use this folder too."
            : "Backup destination cleared. The server default folder will be used.";
    }

    public void SetRestoreFolder(string folderPath)
    {
        RestoreFolder = folderPath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(RestoreFolder) && string.IsNullOrWhiteSpace(RestoreFilePath))
            StatusMessage = "Restore folder selected. Now choose the backup file to restore.";
    }

    public void SetRestoreFile(string filePath)
    {
        RestoreFilePath = filePath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(RestoreFilePath))
        {
            RestoreFolder = Path.GetDirectoryName(RestoreFilePath) ?? RestoreFolder;
            StatusMessage = "Backup file selected and ready to restore.";
        }
    }

    private void ApplySchedule(ScheduledBackupSettings schedule)
    {
        ScheduledBackupsEnabled = schedule.Enabled;
        BackupFolder = string.IsNullOrWhiteSpace(schedule.BackupFolder) ? BackupFolder : schedule.BackupFolder;

        var localTime = ConvertUtcTimeToLocal(schedule.DailyTimeUtc);
        var preset = ScheduleOptionsList.FirstOrDefault(option => option.LocalTime == localTime)
            ?? ScheduleOptionsList.First(option => option.Key == "custom");

        SelectedScheduleOption = preset;
        CustomScheduleTime = localTime.ToString("HH\\:mm");
        SelectedRetentionOption = RetentionOptionsList.FirstOrDefault(option => option.Count == schedule.RetentionCount)
            ?? RetentionOptionsList[1];
    }

    private TimeOnly? GetSelectedLocalTime()
    {
        var presetTime = (SelectedScheduleOption as ScheduleOption)?.LocalTime;
        if (presetTime is not null)
            return presetTime;

        return TimeOnly.TryParse(CustomScheduleTime, out var customTime)
            ? customTime
            : null;
    }

    private static TimeOnly ConvertUtcTimeToLocal(TimeOnly utcTime)
    {
        var utcDateTime = DateTime.SpecifyKind(DateTime.Today.Add(utcTime.ToTimeSpan()), DateTimeKind.Utc);
        return TimeOnly.FromDateTime(utcDateTime.ToLocalTime());
    }

    private static TimeOnly ConvertLocalTimeToUtc(TimeOnly localTime)
    {
        var localDateTime = DateTime.SpecifyKind(DateTime.Today.Add(localTime.ToTimeSpan()), DateTimeKind.Local);
        return TimeOnly.FromDateTime(localDateTime.ToUniversalTime());
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
