namespace Pos.Terminal.Models;

public sealed class AppSettings
{
    public string CompanyName { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string CompanyContact { get; set; } = "";
    public string ReceiptPrinterName { get; set; } = "";
    public string HeaderTitle { get; set; } = "";
    public string HeaderImagePath { get; set; } = "";
}
