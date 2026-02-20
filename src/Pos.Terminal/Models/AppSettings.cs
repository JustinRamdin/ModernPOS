namespace Pos.Terminal.Models;

public sealed class AppSettings
{
    public string CompanyName { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyContact { get; set; } = "";
    public string ReceiptPrinterName { get; set; } = "";
    public string HeaderTitle { get; set; } = "";
    public string HeaderImagePath { get; set; } = "";
    public string LogoImagePath { get; set; } = "";
    public int LogoScaleMultiplier { get; set; } = 1;
    public string ReceiptRemarks { get; set; } = "";
    public bool IsVatEnabled { get; set; } = true;
    public decimal VatRatePercent { get; set; } = 12.5m;
}
