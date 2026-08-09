using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPausedToActiveJobFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_print_jobs_PrinterId",
                table: "print_jobs");

            migrationBuilder.DropIndex(
                name: "IX_print_jobs_ProductId",
                table: "print_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_PrinterId",
                table: "print_jobs",
                column: "PrinterId",
                unique: true,
                filter: "[Status] IN ('Preparing', 'Ready', 'Printing', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_ProductId",
                table: "print_jobs",
                column: "ProductId",
                unique: true,
                filter: "[Status] IN ('Preparing', 'Ready', 'Printing', 'Paused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_print_jobs_PrinterId",
                table: "print_jobs");

            migrationBuilder.DropIndex(
                name: "IX_print_jobs_ProductId",
                table: "print_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_PrinterId",
                table: "print_jobs",
                column: "PrinterId",
                unique: true,
                filter: "[Status] IN ('Preparing', 'Ready', 'Printing')");

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_ProductId",
                table: "print_jobs",
                column: "ProductId",
                unique: true,
                filter: "[Status] IN ('Preparing', 'Ready', 'Printing')");
        }
    }
}
