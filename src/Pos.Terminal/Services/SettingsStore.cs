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
            HeaderImagePath = GetValue(lookup, HeaderImagePathKey)
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

        await db.SaveChangesAsync(ct);
    }

    private static string GetValue(Dictionary<string, string> lookup, string key)
        => lookup.TryGetValue(key, out var value) ? value : "";

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
