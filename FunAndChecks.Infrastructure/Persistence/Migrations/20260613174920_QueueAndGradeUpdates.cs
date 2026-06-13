using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueueAndGradeUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSelfJoin",
                table: "QueueEvents",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPoints",
                table: "GradeComponents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowSelfJoin",
                table: "QueueEvents");

            migrationBuilder.DropColumn(
                name: "MinPoints",
                table: "GradeComponents");
        }
    }
}
