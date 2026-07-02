using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

[Migration("20260702160000_AddCustomerIsCompany")]
public partial class AddCustomerIsCompany : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "IsCompany", table: "Customers", type: "boolean", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IsCompany", table: "Customers");
}
