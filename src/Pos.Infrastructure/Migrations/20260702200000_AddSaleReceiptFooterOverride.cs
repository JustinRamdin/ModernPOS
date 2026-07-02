using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702200000_AddSaleReceiptFooterOverride")]
public partial class AddSaleReceiptFooterOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "ReceiptFooterOverride", table: "Sales", type: "character varying(2000)", maxLength: 2000, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ReceiptFooterOverride", table: "Sales");
}
