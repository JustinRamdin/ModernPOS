using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702150000_AddProductZeroRated")]
public partial class AddProductZeroRated : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "ZeroRated", table: "Products", type: "boolean", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ZeroRated", table: "Products");
}
