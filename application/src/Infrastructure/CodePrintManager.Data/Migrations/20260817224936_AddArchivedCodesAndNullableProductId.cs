using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedCodesAndNullableProductId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_codes_product_nodes_ProductId",
                table: "codes");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "codes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateTable(
                name: "archived_codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalCodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    CodeText = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ImportOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportBatch = table.Column<string>(type: "TEXT", nullable: true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_codes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archived_codes_ProductId_ArchivedAt",
                table: "archived_codes",
                columns: new[] { "ProductId", "ArchivedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_codes_product_nodes_ProductId",
                table: "codes",
                column: "ProductId",
                principalTable: "product_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_codes_product_nodes_ProductId",
                table: "codes");

            migrationBuilder.DropTable(
                name: "archived_codes");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "codes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_codes_product_nodes_ProductId",
                table: "codes",
                column: "ProductId",
                principalTable: "product_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
