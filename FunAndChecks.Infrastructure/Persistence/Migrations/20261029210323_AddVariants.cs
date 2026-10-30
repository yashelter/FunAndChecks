using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VariantSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Uniqueness = table.Column<int>(type: "integer", nullable: false),
                    ClaimsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    ClaimDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariantSets_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Variants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VariantSetId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Variants_VariantSets_VariantSetId",
                        column: x => x.VariantSetId,
                        principalTable: "VariantSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VariantAssignments",
                columns: table => new
                {
                    VariantSetId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelfClaimed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantAssignments", x => new { x.VariantSetId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_VariantAssignments_Admins_AssignedByAdminId",
                        column: x => x.AssignedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VariantAssignments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VariantAssignments_VariantSets_VariantSetId",
                        column: x => x.VariantSetId,
                        principalTable: "VariantSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VariantAssignments_Variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "Variants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VariantAssignments_AssignedByAdminId",
                table: "VariantAssignments",
                column: "AssignedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAssignments_StudentId",
                table: "VariantAssignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAssignments_VariantId",
                table: "VariantAssignments",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Variants_VariantSetId_Number",
                table: "Variants",
                columns: new[] { "VariantSetId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariantSets_SubjectId",
                table: "VariantSets",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VariantAssignments");

            migrationBuilder.DropTable(
                name: "Variants");

            migrationBuilder.DropTable(
                name: "VariantSets");
        }
    }
}
