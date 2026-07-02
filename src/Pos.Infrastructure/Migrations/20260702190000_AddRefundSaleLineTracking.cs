using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702190000_AddRefundSaleLineTracking")]
public partial class AddRefundSaleLineTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RefundedFromSaleLineId", table: "SaleLines", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SaleLines_RefundedFromSaleLineId", table: "SaleLines", column: "RefundedFromSaleLineId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_SaleLines_RefundedFromSaleLineId", table: "SaleLines");
        migrationBuilder.DropColumn(name: "RefundedFromSaleLineId", table: "SaleLines");
    }
}
