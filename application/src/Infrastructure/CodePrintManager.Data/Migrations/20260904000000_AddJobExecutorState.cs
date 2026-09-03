using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodePrintManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobExecutorState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CounterOffset",
                table: "print_jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousCounter",
                table: "print_jobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastKnownLifetime",
                table: "print_jobs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterOffset",
                table: "print_jobs");

            migrationBuilder.DropColumn(
                name: "PreviousCounter",
                table: "print_jobs");

            migrationBuilder.DropColumn(
                name: "LastKnownLifetime",
                table: "print_jobs");
        }
    }
}
