using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterEndpointUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Handle any existing duplicate (IpAddress, Port) records before adding the unique index.
            // Deactivate duplicates and change their Port to a unique value (negative of their Id)
            // so the unique constraint succeeds. The lowest-Id record is kept unchanged.
            migrationBuilder.Sql(@"
                UPDATE printers
                SET IsActive = 0,
                    Port = -Id,
                    Name = Name || ' (deactivated-duplicate)'
                WHERE Id NOT IN (
                    SELECT MIN(Id) FROM printers GROUP BY IpAddress, Port
                )
                AND EXISTS (
                    SELECT 1 FROM printers p2
                    WHERE p2.IpAddress = printers.IpAddress AND p2.Port = printers.Port AND p2.Id != printers.Id
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_printers_IpAddress_Port",
                table: "printers",
                columns: new[] { "IpAddress", "Port" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_printers_IpAddress_Port",
                table: "printers");
        }
    }
}
