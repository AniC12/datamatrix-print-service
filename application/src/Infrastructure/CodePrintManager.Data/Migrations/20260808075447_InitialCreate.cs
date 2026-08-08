using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "printers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 9100),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    AdapterType = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "savema_tto"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_nodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsLeaf = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    TemplateFile = table.Column<string>(type: "TEXT", nullable: true),
                    PrinterCsvName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_nodes_product_nodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "product_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "print_jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Preparing"),
                    TotalBaseline = table.Column<int>(type: "INTEGER", nullable: true),
                    CodesConfirmed = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_print_jobs_printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_print_jobs_product_nodes_ProductId",
                        column: x => x.ProductId,
                        principalTable: "product_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    CodeText = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Available"),
                    ImportOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportBatch = table.Column<string>(type: "TEXT", nullable: true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_codes_print_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "print_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_codes_product_nodes_ProductId",
                        column: x => x.ProductId,
                        principalTable: "product_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_CreatedAt",
                table: "audit_log",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ProductId",
                table: "audit_log",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_codes_CodeText",
                table: "codes",
                column: "CodeText",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_codes_JobId",
                table: "codes",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_codes_ProductId_Status",
                table: "codes",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_codes_Status",
                table: "codes",
                column: "Status");

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

            migrationBuilder.CreateIndex(
                name: "IX_print_jobs_Status",
                table: "print_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_product_nodes_ParentId",
                table: "product_nodes",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "codes");

            migrationBuilder.DropTable(
                name: "print_jobs");

            migrationBuilder.DropTable(
                name: "printers");

            migrationBuilder.DropTable(
                name: "product_nodes");
        }
    }
}
