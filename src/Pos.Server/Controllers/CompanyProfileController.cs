using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Contracts;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/company-profile")]
public sealed class CompanyProfileController(PosDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanyProfileDto>> Get(CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.Accountant, UserRole.SuperUser))
            return Unauthorized();

        var company = await db.Companies.AsNoTracking().OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (company is null)
            return NotFound();

        return Ok(ToDto(company));
    }

    internal static CompanyProfileDto ToDto(Company company)
        => new(
            company.Id,
            company.Name,
            company.ReceiptAddressLine1,
            company.ReceiptAddressLine2,
            company.ReceiptPhone,
            company.ReceiptEmail,
            company.TaxRegistrationNumber,
            company.ReceiptFooter,
            string.IsNullOrWhiteSpace(company.ReceiptHeaderTitle) ? company.Name : company.ReceiptHeaderTitle,
            company.HeaderImage,
            company.LogoImage,
            Math.Clamp(company.LogoScaleMultiplier, 1, 4));

    internal static void Apply(Company company, UpdateCompanyProfileRequest request)
    {
        company.Name = (request.CompanyName ?? string.Empty).Trim();
        company.ReceiptAddressLine1 = (request.AddressLine1 ?? string.Empty).Trim();
        company.ReceiptAddressLine2 = (request.AddressLine2 ?? string.Empty).Trim();
        company.ReceiptPhone = (request.Phone ?? string.Empty).Trim();
        company.ReceiptEmail = (request.Email ?? string.Empty).Trim();
        company.TaxRegistrationNumber = (request.TaxRegistrationNumber ?? string.Empty).Trim();
        company.ReceiptFooter = (request.ReceiptFooter ?? string.Empty).Trim();
        company.ReceiptHeaderTitle = (request.HeaderTitle ?? string.Empty).Trim();
        company.HeaderImage = request.HeaderImage is { Length: > 0 } ? request.HeaderImage : null;
        company.LogoImage = request.LogoImage is { Length: > 0 } ? request.LogoImage : null;
        company.LogoScaleMultiplier = Math.Clamp(request.LogoScaleMultiplier, 1, 4);
    }
}
