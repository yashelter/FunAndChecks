using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilesAndAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AchievementDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementDefinitions_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AboutMe = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    GitHubUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PortfolioUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AchievementAwards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AchievementId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedByName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedByName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementAwards_AchievementDefinitions_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "AchievementDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AchievementAwards_AspNetUsers_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AchievementAwards_AspNetUsers_RevokedByUserId",
                        column: x => x.RevokedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AchievementAwards_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileMediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    AchievementDefinitionId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_ProfileMediaAssets", x => x.Id);
                    table.CheckConstraint("CK_ProfileMediaAssets_OneOwner", "(\"UserProfileId\" IS NOT NULL AND \"AchievementDefinitionId\" IS NULL) OR (\"UserProfileId\" IS NULL AND \"AchievementDefinitionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProfileMediaAssets_AchievementDefinitions_AchievementDefini~",
                        column: x => x.AchievementDefinitionId,
                        principalTable: "AchievementDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileMediaAssets_UserProfiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "UserProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_AchievementAwards_AchievementId_UserId",
                table: "AchievementAwards",
                columns: new[] { "AchievementId", "UserId" },
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementAwards_IssuedByUserId",
                table: "AchievementAwards",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementAwards_RevokedByUserId",
                table: "AchievementAwards",
                column: "RevokedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementAwards_UserId_GrantedAtUtc",
                table: "AchievementAwards",
                columns: new[] { "UserId", "GrantedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDefinitions_CreatedByUserId",
                table: "AchievementDefinitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDefinitions_IsArchived_Name",
                table: "AchievementDefinitions",
                columns: new[] { "IsArchived", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_PinnedAchievements_AchievementAwardId",
                table: "PinnedAchievements",
                column: "AchievementAwardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMediaAssets_AchievementDefinitionId",
                table: "ProfileMediaAssets",
                column: "AchievementDefinitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileMediaAssets_UserProfileId",
                table: "ProfileMediaAssets",
                column: "UserProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PinnedAchievements");

            migrationBuilder.DropTable(
                name: "ProfileMediaAssets");

            migrationBuilder.DropTable(
                name: "AchievementAwards");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "AchievementDefinitions");
        }
    }
}
