using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueueRoundsAndAutoFillGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId",
                table: "QueueEntries");

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "QueueEntries",
                type: "integer",
                nullable: false,
                // Все существующие записи — первый круг.
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "QueueEventAutoFillGroups",
                columns: table => new
                {
                    AutoFillGroupsId = table.Column<int>(type: "integer", nullable: false),
                    QueueEventId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEventAutoFillGroups", x => new { x.AutoFillGroupsId, x.QueueEventId });
                    table.ForeignKey(
                        name: "FK_QueueEventAutoFillGroups_Groups_AutoFillGroupsId",
                        column: x => x.AutoFillGroupsId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QueueEventAutoFillGroups_QueueEvents_QueueEventId",
                        column: x => x.QueueEventId,
                        principalTable: "QueueEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId_Active",
                table: "QueueEntries",
                columns: new[] { "QueueEventId", "StudentId" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId_Round",
                table: "QueueEntries",
                columns: new[] { "QueueEventId", "StudentId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEventAutoFillGroups_QueueEventId",
                table: "QueueEventAutoFillGroups",
                column: "QueueEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueEventAutoFillGroups");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId_Active",
                table: "QueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId_Round",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "QueueEntries");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueEventId_StudentId",
                table: "QueueEntries",
                columns: new[] { "QueueEventId", "StudentId" },
                unique: true);
        }
    }
}
