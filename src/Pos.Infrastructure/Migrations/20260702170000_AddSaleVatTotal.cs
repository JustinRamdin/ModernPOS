using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702170000_AddSaleVatTotal")]
public partial class AddSaleVatTotal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<decimal>(
        name: "VatTotal", table: "Sales", type: "numeric(18,2)", nullable: false, defaultValue: 0m);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "VatTotal", table: "Sales");
}
