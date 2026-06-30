using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecondWaveFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_SubjectId",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SubjectId_Name",
                table: "Tasks",
                columns: new[] { "SubjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Name",
                table: "Subjects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_SubjectId_Name",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Name",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Groups_Name",
                table: "Groups");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SubjectId",
                table: "Tasks",
                column: "SubjectId");
        }
    }
}
