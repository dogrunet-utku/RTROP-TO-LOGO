using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RTROPToLogoIntegration.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpItemParametersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MRP_ITEM_PARAMETERS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirmNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ItemID = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ABCDClassification = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PlanningType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SafetyStock = table.Column<double>(type: "float", nullable: false),
                    ROP = table.Column<double>(type: "float", nullable: false),
                    Max = table.Column<double>(type: "float", nullable: false),
                    OrderQuantity = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MRP_ITEM_PARAMETERS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MRP_ITEM_PARAMETERS_FirmNo_ItemID",
                table: "MRP_ITEM_PARAMETERS",
                columns: new[] { "FirmNo", "ItemID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MRP_ITEM_PARAMETERS");
        }
    }
}
