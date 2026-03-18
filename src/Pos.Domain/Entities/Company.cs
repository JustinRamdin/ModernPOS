namespace Pos.Domain.Entities;

public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ReceiptAddressLine1 { get; set; } = string.Empty;
    public string ReceiptAddressLine2 { get; set; } = string.Empty;
    public string ReceiptPhone { get; set; } = string.Empty;
    public string ReceiptEmail { get; set; } = string.Empty;
    public string TaxRegistrationNumber { get; set; } = string.Empty;
    public string ReceiptFooter { get; set; } = string.Empty;
    public string ReceiptHeaderTitle { get; set; } = string.Empty;
    public byte[]? HeaderImage { get; set; }
    public byte[]? LogoImage { get; set; }
    public int LogoScaleMultiplier { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<UserAccount> Users { get; set; } = new List<UserAccount>();
}
