using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase13_RoomParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "RoomMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FromUserId",
                table: "AgentMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomMembers_UserId",
                table: "RoomMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessages_FromUserId",
                table: "AgentMessages",
                column: "FromUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomMembers_UserId",
                table: "RoomMembers");

            migrationBuilder.DropIndex(
                name: "IX_AgentMessages_FromUserId",
                table: "AgentMessages");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "RoomMembers");

            migrationBuilder.DropColumn(
                name: "FromUserId",
                table: "AgentMessages");
        }
    }
}
