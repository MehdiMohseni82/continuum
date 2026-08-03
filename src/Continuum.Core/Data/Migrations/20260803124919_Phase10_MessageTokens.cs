using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_MessageTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CacheCreationTokens",
                table: "AgentMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "AgentMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "AgentMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "AgentMessages",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheCreationTokens",
                table: "AgentMessages");

            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                table: "AgentMessages");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "AgentMessages");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "AgentMessages");
        }
    }
}
