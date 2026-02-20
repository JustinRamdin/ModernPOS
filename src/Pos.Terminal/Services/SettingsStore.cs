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
    private const string HeaderTitleKey = "header.title";
    private const string HeaderImagePathKey = "header.image.path";
     private const string LogoImagePathKey = "logo.image.path";
    private const string LogoScaleMultiplierKey = "logo.scale.multiplier";
    private const string FinanceVatEnabledKey = "finance.vat.enabled";
    private const string FinanceVatRatePercentKey = "finance.vat.rate.percent";
     private const string ReceiptRemarksKey = "receipt.remarks";

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        var entries = await db.DeviceConfig.AsNoTracking().ToListAsync(ct);
        var lookup = entries.ToDictionary(x => x.Key, x => x.Value ?? "");

        return new AppSettings
        {
            CompanyName = GetValue(lookup, CompanyNameKey),
            CompanyAddress = GetValue(lookup, CompanyAddressKey),
            CompanyContact = GetValue(lookup, CompanyContactKey),
            ReceiptPrinterName = GetValue(lookup, ReceiptPrinterKey),
            HeaderTitle = GetValue(lookup, HeaderTitleKey),
            HeaderImagePath = GetValue(lookup, HeaderImagePathKey),
            LogoImagePath = GetValue(lookup, LogoImagePathKey),
            LogoScaleMultiplier = GetIntValue(lookup, LogoScaleMultiplierKey, 1, 1, 4),
            ReceiptRemarks = GetValue(lookup, ReceiptRemarksKey),
            IsVatEnabled = GetBoolValue(lookup, FinanceVatEnabledKey, true),
            VatRatePercent = GetDecimalValue(lookup, FinanceVatRatePercentKey, 12.5m)
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await using var db = CreateLocalDb();
        await db.Database.EnsureCreatedAsync(ct);

        await UpsertAsync(db, CompanyNameKey, settings.CompanyName, ct);
        await UpsertAsync(db, CompanyAddressKey, settings.CompanyAddress, ct);
        await UpsertAsync(db, CompanyContactKey, settings.CompanyContact, ct);
        await UpsertAsync(db, ReceiptPrinterKey, settings.ReceiptPrinterName, ct);
        await UpsertAsync(db, HeaderTitleKey, settings.HeaderTitle, ct);
        await UpsertAsync(db, HeaderImagePathKey, settings.HeaderImagePath, ct);
        await UpsertAsync(db, ReceiptRemarksKey, settings.ReceiptRemarks, ct);
        await UpsertAsync(db, FinanceVatEnabledKey, settings.IsVatEnabled ? "true" : "false", ct);
        await UpsertAsync(db, FinanceVatRatePercentKey, settings.VatRatePercent.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);

        await db.SaveChangesAsync(ct);
    }

    private static string GetValue(Dictionary<string, string> lookup, string key)
        => lookup.TryGetValue(key, out var value) ? value : "";

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

    private static PosLocalDbContext CreateLocalDb()
        => new PosLocalDbContext(LocalDb.BuildOptions());
}
