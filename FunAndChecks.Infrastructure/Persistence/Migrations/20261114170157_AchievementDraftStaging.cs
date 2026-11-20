using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AchievementDraftStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AchievementDefinitions_CreatedByUserId",
                table: "AchievementDefinitions");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedFromDraftId",
                table: "AchievementDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AchievementDrafts",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDrafts", x => new { x.OwnerId, x.Id });
                    table.ForeignKey(
                        name: "FK_AchievementDrafts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AchievementDraftMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PosterData = table.Column<byte[]>(type: "bytea", nullable: false),
                    PosterContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsAnimated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDraftMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementDraftMedia_AchievementDrafts_OwnerId_DraftId",
                        columns: x => new { x.OwnerId, x.DraftId },
                        principalTable: "AchievementDrafts",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDefinitions_CreatedByUserId_CreatedFromDraftId",
                table: "AchievementDefinitions",
                columns: new[] { "CreatedByUserId", "CreatedFromDraftId" },
                unique: true,
                filter: "\"CreatedFromDraftId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDraftMedia_OwnerId_DraftId_Slot",
                table: "AchievementDraftMedia",
                columns: new[] { "OwnerId", "DraftId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDrafts_ExpiresAtUtc",
                table: "AchievementDrafts",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AchievementDraftMedia");

            migrationBuilder.DropTable(
                name: "AchievementDrafts");

            migrationBuilder.DropIndex(
                name: "IX_AchievementDefinitions_CreatedByUserId_CreatedFromDraftId",
                table: "AchievementDefinitions");

            migrationBuilder.DropColumn(
                name: "CreatedFromDraftId",
                table: "AchievementDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDefinitions_CreatedByUserId",
                table: "AchievementDefinitions",
                column: "CreatedByUserId");
        }
    }
}
