using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttendanceEnabled",
                table: "Subjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MarkedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => new { x.SessionId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Admins_MarkedByAdminId",
                        column: x => x.MarkedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceSessionGroups",
                columns: table => new
                {
                    AttendanceSessionId = table.Column<int>(type: "integer", nullable: false),
                    GroupsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessionGroups", x => new { x.AttendanceSessionId, x.GroupsId });
                    table.ForeignKey(
                        name: "FK_AttendanceSessionGroups_AttendanceSessions_AttendanceSessio~",
                        column: x => x.AttendanceSessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceSessionGroups_Groups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_GroupId",
                table: "AttendanceRecords",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_MarkedByAdminId",
                table: "AttendanceRecords",
                column: "MarkedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StudentId_SessionId",
                table: "AttendanceRecords",
                columns: new[] { "StudentId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessionGroups_GroupsId",
                table: "AttendanceSessionGroups",
                column: "GroupsId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_CreatedByAdminId",
                table: "AttendanceSessions",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_SubjectId_StartsAt",
                table: "AttendanceSessions",
                columns: new[] { "SubjectId", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "AttendanceSessionGroups");

            migrationBuilder.DropTable(
                name: "AttendanceSessions");

            migrationBuilder.DropColumn(
                name: "AttendanceEnabled",
                table: "Subjects");
        }
    }
}
