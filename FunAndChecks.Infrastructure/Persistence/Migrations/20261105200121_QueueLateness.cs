using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueueLateness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LateAfterMinutes",
                table: "QueueEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LateExcused",
                table: "QueueEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateAfterMinutes",
                table: "QueueEvents");

            migrationBuilder.DropColumn(
                name: "LateExcused",
                table: "QueueEntries");
        }
    }
}
