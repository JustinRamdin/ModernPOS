namespace Pos.Terminal.Models;

public sealed class AppSettings
{
    public string ReceiptPrinterName { get; set; } = "";
    public string A4PrinterName { get; set; } = "";
    public bool UseTspReceiptStyle { get; set; }
    public bool IsVatEnabled { get; set; } = true;
    public decimal VatRatePercent { get; set; } = 12.5m;
    public bool IsPracticeMode { get; set; }
    public bool IsDualInventoryEnabled { get; set; }
    public bool UseEasyInventoryNames { get; set; }
}
