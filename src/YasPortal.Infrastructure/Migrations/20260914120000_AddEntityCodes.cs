using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YasPortal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Organizations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Employees",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Positions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "PermissionGroups",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with a unique placeholder code before the
            // unique indexes below are created, since new rows going forward are
            // expected to supply a real, user-entered code.
            migrationBuilder.Sql("""
                UPDATE [dbo].[Organizations] SET [Code] = CONCAT('ORG-', CONVERT(nvarchar(36), [Id])) WHERE [Code] = '';
                UPDATE [dbo].[Employees] SET [Code] = CONCAT('EMP-', CONVERT(nvarchar(36), [Id])) WHERE [Code] = '';
                UPDATE [dbo].[Positions] SET [Code] = CONCAT('POS-', CONVERT(nvarchar(36), [Id])) WHERE [Code] = '';
                UPDATE [dbo].[PermissionGroups] SET [Code] = CONCAT('GRP-', CONVERT(nvarchar(36), [Id])) WHERE [Code] = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Code",
                table: "Organizations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Code",
                table: "Employees",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Code",
                table: "Positions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGroups_Code",
                table: "PermissionGroups",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_Code",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Employees_Code",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Positions_Code",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_PermissionGroups_Code",
                table: "PermissionGroups");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "PermissionGroups");
        }
    }
}
