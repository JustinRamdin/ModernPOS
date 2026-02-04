using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pos.Local.Data;

#nullable disable

namespace Pos.Local.Data.Migrations;

[DbContext(typeof(PosLocalDbContext))]
[Migration("20260127160000_AddVatAndLength")]
public partial class AddVatAndLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Products: add Description, CostPrice, VatInclusive, IsLength
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "Products",
            type: "TEXT",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CostPrice",
            table: "Products",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<bool>(
            name: "VatInclusive",
            table: "Products",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsLength",
            table: "Products",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        // Inventory: add OnHandInches
        migrationBuilder.AddColumn<int>(
            name: "OnHandInches",
            table: "Inventory",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        // Sales: replace old totals with stable net/vat/gross
        migrationBuilder.AddColumn<decimal>(
            name: "NetTotal",
            table: "Sales",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "VatTotal",
            table: "Sales",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossTotal",
            table: "Sales",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        // Keep backward compat: if existing rows exist, copy Subtotal/Tax/Total into new columns
        migrationBuilder.Sql("UPDATE Sales SET NetTotal = Subtotal, VatTotal = Tax, GrossTotal = Total;");

        // SaleLines: add stable fields + inches qty + kind
        migrationBuilder.AddColumn<int>(
            name: "QuantityKind",
            table: "SaleLines",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "QtyInches",
            table: "SaleLines",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitNet",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitVat",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "UnitGross",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "NetTotal",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "VatTotal",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "GrossTotal",
            table: "SaleLines",
            type: "TEXT",
            nullable: false,
            defaultValue: 0m);

        // Backfill for existing lines (old data): treat UnitPrice as gross, no VAT info
        migrationBuilder.Sql(@"
UPDATE SaleLines 
SET 
    QuantityKind = 0,
    UnitGross = UnitPrice,
    UnitNet = UnitPrice,
    UnitVat = 0,
    GrossTotal = LineTotal,
    NetTotal = LineTotal,
    VatTotal = 0;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Description", table: "Products");
        migrationBuilder.DropColumn(name: "CostPrice", table: "Products");
        migrationBuilder.DropColumn(name: "VatInclusive", table: "Products");
        migrationBuilder.DropColumn(name: "IsLength", table: "Products");

        migrationBuilder.DropColumn(name: "OnHandInches", table: "Inventory");

        migrationBuilder.DropColumn(name: "NetTotal", table: "Sales");
        migrationBuilder.DropColumn(name: "VatTotal", table: "Sales");
        migrationBuilder.DropColumn(name: "GrossTotal", table: "Sales");

        migrationBuilder.DropColumn(name: "QuantityKind", table: "SaleLines");
        migrationBuilder.DropColumn(name: "QtyInches", table: "SaleLines");
        migrationBuilder.DropColumn(name: "UnitNet", table: "SaleLines");
        migrationBuilder.DropColumn(name: "UnitVat", table: "SaleLines");
        migrationBuilder.DropColumn(name: "UnitGross", table: "SaleLines");
        migrationBuilder.DropColumn(name: "NetTotal", table: "SaleLines");
        migrationBuilder.DropColumn(name: "VatTotal", table: "SaleLines");
        migrationBuilder.DropColumn(name: "GrossTotal", table: "SaleLines");
    }
}
