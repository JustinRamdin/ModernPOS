using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702180000_AddSaleLineVatTotal")]
public partial class AddSaleLineVatTotal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<decimal>(
        name: "VatTotal", table: "SaleLines", type: "numeric(18,2)", nullable: false, defaultValue: 0m);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "VatTotal", table: "SaleLines");
}
