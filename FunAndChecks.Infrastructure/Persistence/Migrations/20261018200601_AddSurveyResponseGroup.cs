using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyResponseGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "SurveyResponses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_GroupId",
                table: "SurveyResponses",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_SurveyId_GroupId",
                table: "SurveyResponses",
                columns: new[] { "SurveyId", "GroupId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_Groups_GroupId",
                table: "SurveyResponses",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SurveyResponses_Groups_GroupId",
                table: "SurveyResponses");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_GroupId",
                table: "SurveyResponses");

            migrationBuilder.DropIndex(
                name: "IX_SurveyResponses_SurveyId_GroupId",
                table: "SurveyResponses");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "SurveyResponses");
        }
    }
}
