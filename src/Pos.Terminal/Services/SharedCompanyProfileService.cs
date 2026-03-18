using Pos.Contracts;

namespace Pos.Terminal.Services;

public sealed class SharedCompanyProfileService(SettingsStore settingsStore)
{
    private readonly SettingsStore _settingsStore = settingsStore;

    public SharedCompanyProfileService() : this(new SettingsStore())
    {
    }

    public async Task<CompanyProfileDto> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var deployment = await _settingsStore.LoadDeploymentAsync(ct);
        using var api = new RemoteServerApi(deployment.ServerHost, deployment.ServerPort, deployment.AuthToken);
        return await api.GetCompanyProfileAsync();
    }
}
