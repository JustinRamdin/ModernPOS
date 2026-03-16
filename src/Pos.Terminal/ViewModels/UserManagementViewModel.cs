using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pos.Contracts;
using Pos.Terminal.Commands;
using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed class UserManagementViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();

    public ObservableCollection<UserSummaryDto> Users { get; } = [];
    public IReadOnlyList<string> RoleOptions { get; } = ["Cashier", "Manager", "Accountant", "SuperUser"];

    private UserSummaryDto? _selectedUser;
    public UserSummaryDto? SelectedUser
    {
        get => _selectedUser;
        set
        {
            _selectedUser = value;
            if (value is null)
            {
                EditDisplayName = string.Empty;
                SelectedRole = "Cashier";
                IsActive = true;
            }
            else
            {
                EditDisplayName = value.DisplayName;
                SelectedRole = value.Role;
                IsActive = value.IsActive;
            }

            OnPropertyChanged();
            RefreshCommandStates();
        }
    }

    private string _newUsername = string.Empty;
    public string NewUsername
    {
        get => _newUsername;
        set { _newUsername = value ?? string.Empty; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private string _newDisplayName = string.Empty;
    public string NewDisplayName
    {
        get => _newDisplayName;
        set { _newDisplayName = value ?? string.Empty; OnPropertyChanged(); }
    }

    private string _newPassword = string.Empty;
    public string NewPassword
    {
        get => _newPassword;
        set { _newPassword = value ?? string.Empty; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private string _newRole = "Cashier";
    public string NewRole
    {
        get => _newRole;
        set { _newRole = NormalizeRole(value); OnPropertyChanged(); }
    }

    private string _editDisplayName = string.Empty;
    public string EditDisplayName
    {
        get => _editDisplayName;
        set { _editDisplayName = value ?? string.Empty; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private string _selectedRole = "Cashier";
    public string SelectedRole
    {
        get => _selectedRole;
        set { _selectedRole = NormalizeRole(value); OnPropertyChanged(); RefreshCommandStates(); }
    }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private string _resetPassword = string.Empty;
    public string ResetPassword
    {
        get => _resetPassword;
        set { _resetPassword = value ?? string.Empty; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); RefreshCommandStates(); }
    }

    private string _statusMessage = "Manage users and roles.";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateUserCommand { get; }
    public ICommand SaveChangesCommand { get; }
    public ICommand ResetPasswordCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UserManagementViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsBusy);
        CreateUserCommand = new AsyncRelayCommand(_ => CreateUserAsync(), _ => !IsBusy && CanCreateUser());
        SaveChangesCommand = new AsyncRelayCommand(_ => SaveUserChangesAsync(), _ => !IsBusy && SelectedUser != null && CanSaveUser());
        ResetPasswordCommand = new AsyncRelayCommand(_ => ResetSelectedPasswordAsync(), _ => !IsBusy && SelectedUser != null && ResetPassword.Length >= 4);

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading users...";

            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            var users = await api.GetUsersAsync();

            Users.Clear();
            foreach (var user in users)
                Users.Add(user);

            if (SelectedUser != null)
            {
                SelectedUser = Users.FirstOrDefault(x => x.Id == SelectedUser.Id);
            }

            StatusMessage = Users.Count == 0 ? "No users found." : $"Loaded {Users.Count} user(s).";
        }
        catch (HttpRequestException)
        {
            StatusMessage = "Unable to connect to server while loading users.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load users: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateUser() => !string.IsNullOrWhiteSpace(NewUsername) && NewPassword.Length >= 4;

    private async Task CreateUserAsync()
    {
        try
        {
            IsBusy = true;
            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.CreateUserAsync(new CreateUserApiRequest(NewUsername.Trim(), NewPassword, NormalizeRole(NewRole), string.IsNullOrWhiteSpace(NewDisplayName) ? NewUsername.Trim() : NewDisplayName.Trim()));

            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
            NewPassword = string.Empty;
            NewRole = "Cashier";

            await LoadAsync();
            StatusMessage = "User created successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create user failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveUser() => !string.IsNullOrWhiteSpace(EditDisplayName);

    private async Task SaveUserChangesAsync()
    {
        if (SelectedUser is null)
            return;

        try
        {
            IsBusy = true;
            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.UpdateUserAsync(SelectedUser.Id, new UpdateUserApiRequest(EditDisplayName.Trim(), NormalizeRole(SelectedRole), IsActive));

            await LoadAsync();
            StatusMessage = "User updated successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetSelectedPasswordAsync()
    {
        if (SelectedUser is null)
            return;

        try
        {
            IsBusy = true;
            var deploy = await _settingsStore.LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.ResetUserPasswordAsync(SelectedUser.Id, ResetPassword);
            ResetPassword = string.Empty;
            StatusMessage = "Password reset successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Password reset failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role, "Accounts", StringComparison.OrdinalIgnoreCase))
            return "Accountant";
        return string.IsNullOrWhiteSpace(role) ? "Cashier" : role;
    }

    private void RefreshCommandStates()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateUserCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveChangesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ResetPasswordCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
