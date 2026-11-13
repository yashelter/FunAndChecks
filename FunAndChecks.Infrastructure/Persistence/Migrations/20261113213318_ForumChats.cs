using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForumChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "ForumReactions",
                newName: "ReactionTypeId");

            migrationBuilder.AddColumn<string>(
                name: "DirectKey",
                table: "ForumTopics",
                type: "character varying(90)",
                maxLength: 90,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ForumTopics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ForwardedFromPostId",
                table: "ForumPosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StickerId",
                table: "ForumPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ForumReactionTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ImageData = table.Column<byte[]>(type: "bytea", nullable: true),
                    ImageContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ImageVersion = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumReactionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumStickerPacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumStickerPacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumTopicMembers",
                columns: table => new
                {
                    TopicId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumTopicMembers", x => new { x.TopicId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ForumTopicMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForumTopicMembers_ForumTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "ForumTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ForumStickers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumStickers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumStickers_ForumStickerPacks_PackId",
                        column: x => x.PackId,
                        principalTable: "ForumStickerPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ForumReactionTypes",
                columns: new[] { "Id", "Emoji", "ImageContentType", "ImageData", "ImageVersion", "IsEnabled", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, "👍", null, null, 0, true, "Нравится", 1 },
                    { 2, "❤️", null, null, 0, true, "Сердце", 2 },
                    { 3, "😂", null, null, 0, true, "Смешно", 3 },
                    { 4, "🎉", null, null, 0, true, "Праздник", 4 },
                    { 5, "🤔", null, null, 0, true, "Думаю", 5 },
                    { 6, "👀", null, null, 0, true, "Смотрю", 6 }
                });

            // Старые реакции хранили значение перечисления 0..5; в наборе это id 1..6.
            // Сдвиг в два шага через отрицательные числа: иначе строки столкнутся по первичному ключу.
            migrationBuilder.Sql("UPDATE \"ForumReactions\" SET \"ReactionTypeId\" = -(\"ReactionTypeId\" + 1);");
            migrationBuilder.Sql("UPDATE \"ForumReactions\" SET \"ReactionTypeId\" = -\"ReactionTypeId\";");
            // Строки с явными id не двигают счётчик identity — без этого первая новая реакция получила бы id 1.
            if (migrationBuilder.IsNpgsql())
                migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"ForumReactionTypes\"', 'Id'), 6, true);");

            migrationBuilder.CreateIndex(
                name: "IX_ForumTopics_DirectKey",
                table: "ForumTopics",
                column: "DirectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForumReactions_ReactionTypeId",
                table: "ForumReactions",
                column: "ReactionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_ForwardedFromPostId",
                table: "ForumPosts",
                column: "ForwardedFromPostId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_StickerId",
                table: "ForumPosts",
                column: "StickerId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumStickers_PackId_SortOrder",
                table: "ForumStickers",
                columns: new[] { "PackId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumTopicMembers_UserId",
                table: "ForumTopicMembers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ForumPosts_ForumPosts_ForwardedFromPostId",
                table: "ForumPosts",
                column: "ForwardedFromPostId",
                principalTable: "ForumPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ForumPosts_ForumStickers_StickerId",
                table: "ForumPosts",
                column: "StickerId",
                principalTable: "ForumStickers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ForumReactions_ForumReactionTypes_ReactionTypeId",
                table: "ForumReactions",
                column: "ReactionTypeId",
                principalTable: "ForumReactionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ForumPosts_ForumPosts_ForwardedFromPostId",
                table: "ForumPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_ForumPosts_ForumStickers_StickerId",
                table: "ForumPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_ForumReactions_ForumReactionTypes_ReactionTypeId",
                table: "ForumReactions");

            migrationBuilder.Sql("DELETE FROM \"ForumReactions\" WHERE \"ReactionTypeId\" > 6;");
            migrationBuilder.Sql("UPDATE \"ForumReactions\" SET \"ReactionTypeId\" = -\"ReactionTypeId\";");
            migrationBuilder.Sql("UPDATE \"ForumReactions\" SET \"ReactionTypeId\" = -\"ReactionTypeId\" - 1;");

            migrationBuilder.DropTable(
                name: "ForumReactionTypes");

            migrationBuilder.DropTable(
                name: "ForumStickers");

            migrationBuilder.DropTable(
                name: "ForumTopicMembers");

            migrationBuilder.DropTable(
                name: "ForumStickerPacks");

            migrationBuilder.DropIndex(
                name: "IX_ForumTopics_DirectKey",
                table: "ForumTopics");

            migrationBuilder.DropIndex(
                name: "IX_ForumReactions_ReactionTypeId",
                table: "ForumReactions");

            migrationBuilder.DropIndex(
                name: "IX_ForumPosts_ForwardedFromPostId",
                table: "ForumPosts");

            migrationBuilder.DropIndex(
                name: "IX_ForumPosts_StickerId",
                table: "ForumPosts");

            migrationBuilder.DropColumn(
                name: "DirectKey",
                table: "ForumTopics");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ForumTopics");

            migrationBuilder.DropColumn(
                name: "ForwardedFromPostId",
                table: "ForumPosts");

            migrationBuilder.DropColumn(
                name: "StickerId",
                table: "ForumPosts");

            migrationBuilder.RenameColumn(
                name: "ReactionTypeId",
                table: "ForumReactions",
                newName: "Kind");
        }
    }
}
