using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Contracts;
using Pos.Terminal.Commands;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class UpdatesViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();
    private readonly LocalUpdateService _localUpdateService = new();
    private readonly ClientUpdateInstaller _installer = new();
    private DeploymentSettings _deployment = new();
    private TerminalUpdateManifest? _manifest;
    private string? _installerPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public UpdatesViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsBusy);
        CheckLocalUpdatesCommand = new AsyncRelayCommand(_ => CheckForUpdatesAsync(), _ => !IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(_ => InstallUpdateAsync(), _ => CanInstall);
    }

    private string _currentClientVersion = "Loading...";
    public string CurrentClientVersion
    {
        get => _currentClientVersion;
        set { _currentClientVersion = value; OnPropertyChanged(); }
    }

    private string _connectedServerVersion = "Checking...";
    public string ConnectedServerVersion
    {
        get => _connectedServerVersion;
        set { _connectedServerVersion = value; OnPropertyChanged(); }
    }

    private string _selectedUpdateFolder = string.Empty;
    public string SelectedUpdateFolder
    {
        get => _selectedUpdateFolder;
        set
        {
            _selectedUpdateFolder = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedFolder));
        }
    }

    public bool HasSelectedFolder => !string.IsNullOrWhiteSpace(SelectedUpdateFolder);

    private string _availableUpdateVersion = "No update checked yet.";
    public string AvailableUpdateVersion
    {
        get => _availableUpdateVersion;
        set { _availableUpdateVersion = value; OnPropertyChanged(); }
    }

    private string _releaseNotes = "Release notes will appear after checking the selected local folder.";
    public string ReleaseNotes
    {
        get => _releaseNotes;
        set { _releaseNotes = value; OnPropertyChanged(); }
    }

    private string _compatibilitySummary = "This screen only installs client-only update packages.";
    public string CompatibilitySummary
    {
        get => _compatibilitySummary;
        set { _compatibilitySummary = value; OnPropertyChanged(); }
    }

    private string _warningSummary = "Server updates remain manual and are never triggered from this screen.";
    public string WarningSummary
    {
        get => _warningSummary;
        set { _warningSummary = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "Select a local folder, then check for updates.";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _isInstallBlocked = true;
    public bool IsInstallBlocked
    {
        get => _isInstallBlocked;
        set
        {
            _isInstallBlocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
            (InstallUpdateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanInstall => !IsBusy && !IsInstallBlocked;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
            (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CheckLocalUpdatesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (InstallUpdateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand CheckLocalUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading update information...";
            CurrentClientVersion = ApplicationVersionService.GetCurrentVersion();

            _deployment = await _settingsStore.LoadDeploymentAsync();
            SelectedUpdateFolder = _deployment.UpdateSourceFolder;

            await LoadServerInformationAsync();
            StatusMessage = "Select a local update folder and check for updates.";
        }
        catch (Exception ex)
        {
            ConnectedServerVersion = "Unavailable";
            WarningSummary = $"Could not load update settings: {ex.Message}";
            StatusMessage = "Update screen loaded with warnings.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetSelectedFolderAsync(string folderPath)
    {
        SelectedUpdateFolder = folderPath?.Trim() ?? string.Empty;
        _deployment.UpdateSourceFolder = SelectedUpdateFolder;
        await _settingsStore.SaveDeploymentAsync(_deployment);
        StatusMessage = "Local update folder saved.";
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            IsBusy = true;
            ResetManifestResults();
            StatusMessage = "Checking selected folder for a local update package...";

            if (string.IsNullOrWhiteSpace(SelectedUpdateFolder))
                throw new InvalidOperationException("Select a local update folder before checking for updates.");

            _manifest = await _localUpdateService.LoadManifestAsync(SelectedUpdateFolder);
            _installerPath = _localUpdateService.ResolveInstallerPath(SelectedUpdateFolder, _manifest);
            AvailableUpdateVersion = _manifest.Version;
            ReleaseNotes = _manifest.Notes;

            var isClientOnlyPackage = string.Equals(_manifest.Type, "client-only", StringComparison.OrdinalIgnoreCase);
            var hasNewerVersion = VersionComparer.Compare(_manifest.Version, CurrentClientVersion) > 0;
            var hasServerVersion = VersionComparer.TryParse(ConnectedServerVersion, out _);
            var serverCompatible = hasServerVersion && VersionComparer.IsInRange(ConnectedServerVersion, _manifest.MinServerVersion, _manifest.MaxServerVersion);

            if (!isClientOnlyPackage)
            {
                IsInstallBlocked = true;
                CompatibilitySummary = $"Package type '{_manifest.Type}' is not installable from the terminal. Server updates remain manual.";
                WarningSummary = "Blocked to protect the live server database and prevent accidental schema changes.";
                StatusMessage = "Update blocked.";
                return;
            }

            if (!hasNewerVersion)
            {
                IsInstallBlocked = true;
                CompatibilitySummary = $"Installed client version {CurrentClientVersion} is already up to date or newer than {_manifest.Version}.";
                WarningSummary = "No install needed.";
                StatusMessage = "No newer client update found.";
                return;
            }

            if (!hasServerVersion)
            {
                IsInstallBlocked = true;
                CompatibilitySummary = $"The terminal could not verify the connected server version against the required range {_manifest.MinServerVersion} - {_manifest.MaxServerVersion}.";
                WarningSummary = "Install is blocked until the server version can be confirmed.";
                StatusMessage = "Update blocked.";
                return;
            }

            if (!serverCompatible)
            {
                IsInstallBlocked = true;
                CompatibilitySummary = $"Client {_manifest.Version} requires server versions between {_manifest.MinServerVersion} and {_manifest.MaxServerVersion}. Connected server version is {ConnectedServerVersion}.";
                WarningSummary = "Install blocked to avoid client/server mismatch and live data access issues.";
                StatusMessage = "Update blocked.";
                return;
            }

            IsInstallBlocked = false;
            CompatibilitySummary = $"Safe client-only update detected. Client {_manifest.Version} is compatible with server {ConnectedServerVersion}.";
            WarningSummary = "Installer will update terminal files only. No server migration, database replacement, or schema change is triggered here.";
            StatusMessage = "Update is ready to install.";
        }
        catch (Exception ex)
        {
            IsInstallBlocked = true;
            AvailableUpdateVersion = "No valid update package found.";
            ReleaseNotes = "Release notes unavailable.";
            CompatibilitySummary = "The selected folder did not contain a valid client update package.";
            WarningSummary = ex.Message;
            StatusMessage = "Update check failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallUpdateAsync()
    {
        try
        {
            if (IsInstallBlocked || _manifest is null || string.IsNullOrWhiteSpace(_installerPath))
                throw new InvalidOperationException("Check a compatible client-only update before installing.");

            IsBusy = true;
            StatusMessage = "Launching installer. ModernPOS will close so terminal files can be replaced safely.";
            await _installer.LaunchInstallerAsync(_installerPath);
        }
        catch (Exception ex)
        {
            WarningSummary = ex.Message;
            StatusMessage = "Install failed.";
            IsInstallBlocked = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadServerInformationAsync()
    {
        try
        {
            using var api = new RemoteServerApi(_deployment.ServerHost, _deployment.ServerPort, _deployment.AuthToken);
            var server = await api.GetServerVersionInfoAsync();
            ConnectedServerVersion = server.Version;

            var safetyMessage = server.DatabasePathIsProtected
                ? $"Server database path is outside the app install folder: {server.DatabasePath}"
                : $"Warning: server database path is inside or near the install folder and should be moved before any server update: {server.DatabasePath}";

            WarningSummary = $"{safetyMessage}{Environment.NewLine}Server updates remain manual and are never triggered from this screen.";
            CompatibilitySummary = server.ManualServerUpdatesRequired
                ? "Connected server reports that server updates require a separate manual process."
                : CompatibilitySummary;
        }
        catch
        {
            ConnectedServerVersion = "Unavailable";
            WarningSummary = "Connected server version could not be read. Compatibility checks will stay blocked until the server can be reached.";
        }
    }

    private void ResetManifestResults()
    {
        _manifest = null;
        _installerPath = null;
        IsInstallBlocked = true;
        AvailableUpdateVersion = "No update checked yet.";
        ReleaseNotes = "Release notes will appear after checking the selected local folder.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
