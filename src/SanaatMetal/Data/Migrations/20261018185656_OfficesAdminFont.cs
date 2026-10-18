using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanaatMetal.Data.Migrations
{
    /// <inheritdoc />
    public partial class OfficesAdminFont : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address2En",
                table: "SiteSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address2Fa",
                table: "SiteSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdminFontSize",
                table: "SiteSettings",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<string>(
                name: "Office1TitleEn",
                table: "SiteSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Office1TitleFa",
                table: "SiteSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Office2TitleEn",
                table: "SiteSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Office2TitleFa",
                table: "SiteSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone2",
                table: "SiteSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address2En",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Address2Fa",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "AdminFontSize",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Office1TitleEn",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Office1TitleFa",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Office2TitleEn",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Office2TitleFa",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "Phone2",
                table: "SiteSettings");
        }
    }
}
