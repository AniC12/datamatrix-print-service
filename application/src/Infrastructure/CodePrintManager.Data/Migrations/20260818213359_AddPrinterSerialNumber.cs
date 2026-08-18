using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterSerialNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "printers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "printers");
        }
    }
}
