using Pos.Terminal.Services;

namespace Pos.Terminal.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _loggedInUserLabel = "Not logged in";
    public string LoggedInUserLabel
    {
        get => _loggedInUserLabel;
        set { _loggedInUserLabel = value; OnPropertyChanged(); }
    }

    private string _connectedCompany = "No server";
    public string ConnectedCompany
    {
        get => _connectedCompany;
        set { _connectedCompany = value; OnPropertyChanged(); }
    }

    public async Task LoadSessionHeaderAsync()
    {
        var deploy = await new SettingsStore().LoadDeploymentAsync();
        LoggedInUserLabel = $"User: {deploy.Username} ({deploy.Role})";
        ConnectedCompany = deploy.CompanyName;
    }

    public async Task TriggerServerBackupAsync()
    {
        try
        {
            var deploy = await new SettingsStore().LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            await api.TriggerBackupAsync();
            Toast("Backup request sent to server.");
        }
        catch (HttpRequestException)
        {
            Toast("Unable to reach the server for backup. Verify server connection and try again.");
        }
    }
}
