using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FunAndChecks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QueueEntryFinishedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinishedByAdminId",
                table: "QueueEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_FinishedByAdminId",
                table: "QueueEntries",
                column: "FinishedByAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_QueueEntries_Admins_FinishedByAdminId",
                table: "QueueEntries",
                column: "FinishedByAdminId",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QueueEntries_Admins_FinishedByAdminId",
                table: "QueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_FinishedByAdminId",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "FinishedByAdminId",
                table: "QueueEntries");
        }
    }
}
