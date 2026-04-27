using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using Pos.Local.Entities;
using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public sealed class SettingsStore
{
    private const string CompanyNameKey = "company.name";
    private const string CompanyAddressKey = "company.address";
    private const string CompanyContactKey = "company.contact";
    private const string ReceiptPrinterKey = "printer.receipt.name";
    private const string ReceiptPrinterTspStyleKey = "printer.receipt.tsp.style";
    private const string HeaderTitleKey = "header.title";
    private const string HeaderImagePathKey = "header.image.path";
    private const string LogoImagePathKey = "logo.image.path";
    private const string LogoScaleMultiplierKey = "logo.scale.multiplier";
    private const string FinanceVatEnabledKey = "finance.vat.enabled";
    private const string FinanceVatRatePercentKey = "finance.vat.rate.percent";
    private const string ReceiptRemarksKey = "receipt.remarks";
    private const string PracticeModeEnabledKey = "practice.mode.enabled";

    private const string DeployConfiguredKey = "deploy.configured";
    private const string DeployModeKey = "deploy.mode";
    private const string DeployServerHostKey = "deploy.server.host";
    private const string DeployServerPortKey = "deploy.server.port";
    private const string DeployCompanyNameKey = "deploy.company.name";
    private const string DeployAuthTokenKey = "deploy.auth.token";
    private const string DeployUsernameKey = "deploy.username";
    private const string DeployRoleKey = "deploy.role";
    private const string DeployUpdateSourceFolderKey = "deploy.update.source.folder";

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        var entries = await db.DeviceConfig.AsNoTracking().ToListAsync(ct);
        var lookup = entries.ToDictionary(x => x.Key, x => x.Value ?? "");

        return new AppSettings
        {
            ReceiptPrinterName = GetValue(lookup, ReceiptPrinterKey),
            UseTspReceiptStyle = GetBoolValue(lookup, ReceiptPrinterTspStyleKey, false),
            IsVatEnabled = GetBoolValue(lookup, FinanceVatEnabledKey, true),
            VatRatePercent = GetDecimalValue(lookup, FinanceVatRatePercentKey, 12.5m),
            IsPracticeMode = GetBoolValue(lookup, PracticeModeEnabledKey, false)
        };
    }

    public async Task<DeploymentSettings> LoadDeploymentAsync(CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);
        var entries = await db.DeviceConfig.AsNoTracking().ToListAsync(ct);
        var lookup = entries.ToDictionary(x => x.Key, x => x.Value ?? "");

        return new DeploymentSettings
        {
            IsConfigured = GetBoolValue(lookup, DeployConfiguredKey, false),
            Mode = GetValue(lookup, DeployModeKey, "Client"),
            ServerHost = GetValue(lookup, DeployServerHostKey, "127.0.0.1"),
            ServerPort = GetIntValue(lookup, DeployServerPortKey, 5050, 1, 65535),
            CompanyName = GetValue(lookup, DeployCompanyNameKey),
            AuthToken = GetValue(lookup, DeployAuthTokenKey),
            Username = GetValue(lookup, DeployUsernameKey),
            Role = GetValue(lookup, DeployRoleKey),
            UpdateSourceFolder = GetValue(lookup, DeployUpdateSourceFolderKey)
        };
    }

    public async Task SaveDeploymentAsync(DeploymentSettings deployment, CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        await UpsertAsync(db, DeployConfiguredKey, deployment.IsConfigured ? "true" : "false", ct);
        await UpsertAsync(db, DeployModeKey, deployment.Mode, ct);
        await UpsertAsync(db, DeployServerHostKey, deployment.ServerHost, ct);
        await UpsertAsync(db, DeployServerPortKey, deployment.ServerPort.ToString(), ct);
        await UpsertAsync(db, DeployCompanyNameKey, deployment.CompanyName, ct);
        await UpsertAsync(db, DeployAuthTokenKey, deployment.AuthToken, ct);
        await UpsertAsync(db, DeployUsernameKey, deployment.Username, ct);
        await UpsertAsync(db, DeployRoleKey, deployment.Role, ct);
        await UpsertAsync(db, DeployUpdateSourceFolderKey, deployment.UpdateSourceFolder, ct);
        await db.SaveChangesAsync(ct);
    }


    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        await UpsertAsync(db, ReceiptPrinterKey, settings.ReceiptPrinterName, ct);
        await UpsertAsync(db, ReceiptPrinterTspStyleKey, settings.UseTspReceiptStyle ? "true" : "false", ct);
        await UpsertAsync(db, FinanceVatEnabledKey, settings.IsVatEnabled ? "true" : "false", ct);
        await UpsertAsync(db, FinanceVatRatePercentKey, settings.VatRatePercent.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        await UpsertAsync(db, PracticeModeEnabledKey, settings.IsPracticeMode ? "true" : "false", ct);

        await db.SaveChangesAsync(ct);
    }
   public async Task ClearLegacyReceiptIdentityAsync(CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        var legacyKeys = new[]
        {
            CompanyNameKey,
            CompanyAddressKey,
            CompanyContactKey,
            HeaderTitleKey,
            HeaderImagePathKey,
            LogoImagePathKey,
            LogoScaleMultiplierKey,
            ReceiptRemarksKey
        };

        var legacyEntries = await db.DeviceConfig.Where(x => legacyKeys.Contains(x.Key)).ToListAsync(ct);
        if (legacyEntries.Count == 0)
            return;

        db.DeviceConfig.RemoveRange(legacyEntries);
        await db.SaveChangesAsync(ct);
    }

    private static string GetValue(Dictionary<string, string> lookup, string key, string defaultValue = "")
        => lookup.TryGetValue(key, out var value) ? value : defaultValue;

    private static bool GetBoolValue(Dictionary<string, string> lookup, string key, bool defaultValue)
    {
        if (!lookup.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static int GetIntValue(Dictionary<string, string> lookup, string key, int defaultValue, int minValue, int maxValue)
    {
        if (!lookup.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!int.TryParse(value, out var parsed))
            return defaultValue;

        return Math.Clamp(parsed, minValue, maxValue);
    }

    private static decimal GetDecimalValue(Dictionary<string, string> lookup, string key, decimal defaultValue)
    {
        if (!lookup.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static async Task UpsertAsync(PosLocalDbContext db, string key, string value, CancellationToken ct)
    {
        var entry = await db.DeviceConfig.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (entry == null)
        {
            db.DeviceConfig.Add(new DeviceConfig { Key = key, Value = value ?? "" });
        }
        else
        {
            entry.Value = value ?? "";
        }
    }

    private static PosLocalDbContext CreateLocalDb() => new(LocalDb.BuildOptions());
}
