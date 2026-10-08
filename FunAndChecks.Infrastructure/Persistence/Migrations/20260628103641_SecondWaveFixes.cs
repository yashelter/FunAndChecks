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
            // Уникальные индексы ниже не создадутся, если в существующих данных уже есть
            // одноимённые записи: перед созданием переименовываем дубликаты (первая запись
            // сохраняет имя, остальные получают суффикс « (2)», « (3)», ...).
            // Миграция выполняется только на PostgreSQL (SQLite-тесты её не прогоняют).
            migrationBuilder.Sql("""
                UPDATE "Groups" AS t
                SET "Name" = t."Name" || ' (' || (
                    SELECT COUNT(*) + 1 FROM "Groups" AS earlier
                    WHERE earlier."Name" = t."Name" AND earlier."Id" < t."Id"
                ) || ')'
                WHERE EXISTS (
                    SELECT 1 FROM "Groups" AS other
                    WHERE other."Name" = t."Name" AND other."Id" < t."Id"
                );
                """);
            migrationBuilder.Sql("""
                UPDATE "Subjects" AS t
                SET "Name" = t."Name" || ' (' || (
                    SELECT COUNT(*) + 1 FROM "Subjects" AS earlier
                    WHERE earlier."Name" = t."Name" AND earlier."Id" < t."Id"
                ) || ')'
                WHERE EXISTS (
                    SELECT 1 FROM "Subjects" AS other
                    WHERE other."Name" = t."Name" AND other."Id" < t."Id"
                );
                """);
            // Дубли задания уникальны в пределах предмета — дедуп по паре (SubjectId, Name).
            migrationBuilder.Sql("""
                UPDATE "Tasks" AS t
                SET "Name" = t."Name" || ' (' || (
                    SELECT COUNT(*) + 1 FROM "Tasks" AS earlier
                    WHERE earlier."SubjectId" = t."SubjectId" AND earlier."Name" = t."Name"
                      AND earlier."Id" < t."Id"
                ) || ')'
                WHERE EXISTS (
                    SELECT 1 FROM "Tasks" AS other
                    WHERE other."SubjectId" = t."SubjectId" AND other."Name" = t."Name"
                      AND other."Id" < t."Id"
                );
                """);

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
