using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActiveAchievementEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProfileMediaAssets_AchievementDefinitionId",
                table: "ProfileMediaAssets");

            migrationBuilder.AddColumn<int>(
                name: "ActiveAwardId",
                table: "UserProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "UserProfiles"
                SET "ActiveAwardId" = (
                    SELECT "AchievementAwardId" FROM "PinnedAchievements"
                    WHERE "PinnedAchievements"."UserId" = "UserProfiles"."UserId"
                    AND "Position" = 1
                );
                """);
            migrationBuilder.DropTable(name: "PinnedAchievements");
            migrationBuilder.AddColumn<int>(
                name: "Slot",
                table: "ProfileMediaAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ActiveByDefault",
                table: "AchievementDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CompiledCss",
                table: "AchievementDefinitions",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomCss",
                table: "AchievementDefinitions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectsJson",
                table: "AchievementDefinitions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EffectsVersion",
                table: "AchievementDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_ActiveAwardId",
                table: "UserProfiles",
                column: "ActiveAwardId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMediaAssets_AchievementDefinitionId_Slot",
                table: "ProfileMediaAssets",
                columns: new[] { "AchievementDefinitionId", "Slot" },
                unique: true,
                filter: "\"AchievementDefinitionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_AchievementAwards_ActiveAwardId",
                table: "UserProfiles",
                column: "ActiveAwardId",
                principalTable: "AchievementAwards",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_AchievementAwards_ActiveAwardId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_ActiveAwardId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProfileMediaAssets_AchievementDefinitionId_Slot",
                table: "ProfileMediaAssets");

            migrationBuilder.DropColumn(
                name: "ActiveAwardId",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "ProfileMediaAssets");

            migrationBuilder.DropColumn(
                name: "ActiveByDefault",
                table: "AchievementDefinitions");

            migrationBuilder.DropColumn(
                name: "CompiledCss",
                table: "AchievementDefinitions");

            migrationBuilder.DropColumn(
                name: "CustomCss",
                table: "AchievementDefinitions");

            migrationBuilder.DropColumn(
                name: "EffectsJson",
                table: "AchievementDefinitions");

            migrationBuilder.DropColumn(
                name: "EffectsVersion",
                table: "AchievementDefinitions");

            migrationBuilder.CreateTable(
                name: "PinnedAchievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    AchievementAwardId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinnedAchievements", x => new { x.UserId, x.Position });
                    table.CheckConstraint("CK_PinnedAchievements_Position", "\"Position\" >= 1 AND \"Position\" <= 3");
                    table.ForeignKey(
                        name: "FK_PinnedAchievements_AchievementAwards_AchievementAwardId",
                        column: x => x.AchievementAwardId,
                        principalTable: "AchievementAwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PinnedAchievements_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMediaAssets_AchievementDefinitionId",
                table: "ProfileMediaAssets",
                column: "AchievementDefinitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PinnedAchievements_AchievementAwardId",
                table: "PinnedAchievements",
                column: "AchievementAwardId",
                unique: true);
        }
    }
}
