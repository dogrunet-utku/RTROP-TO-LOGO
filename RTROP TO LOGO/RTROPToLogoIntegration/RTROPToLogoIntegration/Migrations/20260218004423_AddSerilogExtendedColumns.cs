using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RTROPToLogoIntegration.Migrations
{
    /// <inheritdoc />
    public partial class AddSerilogExtendedColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Master Architect: Self-healing idempotent script.
            // Checks if the Logs table exists, and only adds the columns if they are missing.
            // This prevents conflicts with Serilog's autoCreateSqlTable feature.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Logs')
                BEGIN
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Logs]') AND name = 'TransactionId')
                    BEGIN
                        ALTER TABLE [Logs] ADD [TransactionId] nvarchar(50) NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Logs]') AND name = 'UserId')
                    BEGIN
                        ALTER TABLE [Logs] ADD [UserId] nvarchar(100) NULL;
                    END
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Logs");
        }
    }
}
