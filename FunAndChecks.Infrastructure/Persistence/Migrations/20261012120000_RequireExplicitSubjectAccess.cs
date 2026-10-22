using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FunAndChecks.Infrastructure.Persistence.Migrations;

/// <summary>Preserve existing permissions before switching missing subject access to default deny.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012120000_RequireExplicitSubjectAccess")]
public class RequireExplicitSubjectAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO "AdminSubjectAccesses" ("AdminId", "SubjectId", "IsRestricted", "IsHidden")
        SELECT a."Id", s."Id", FALSE, FALSE
        FROM "Admins" a CROSS JOIN "Subjects" s
        WHERE NOT EXISTS (
            SELECT 1 FROM "AdminSubjectAccesses" x
            WHERE x."AdminId" = a."Id" AND x."SubjectId" = s."Id"
        );
        """);

    // Grants cannot be distinguished from permissions edited after the upgrade.
    // Keep them during rollback instead of discarding administrators' current access.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
