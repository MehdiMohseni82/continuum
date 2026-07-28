using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_Bus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineName = table.Column<string>(type: "text", nullable: true),
                    CurrentSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Capabilities = table.Column<string>(type: "text", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Read = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentMessages_Agents_FromAgentId",
                        column: x => x.FromAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Handoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedByAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Task = table.Column<string>(type: "text", nullable: false),
                    ContextRef = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Handoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Handoffs_Agents_ClaimedByAgentId",
                        column: x => x.ClaimedByAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Handoffs_Agents_FromAgentId",
                        column: x => x.FromAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessages_ChannelId_Id",
                table: "AgentMessages",
                columns: new[] { "ChannelId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessages_FromAgentId",
                table: "AgentMessages",
                column: "FromAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessages_ToAgentId_Read",
                table: "AgentMessages",
                columns: new[] { "ToAgentId", "Read" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OwnerId_Name",
                table: "Agents",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_OwnerId_Name",
                table: "Channels",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Handoffs_ClaimedByAgentId",
                table: "Handoffs",
                column: "ClaimedByAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Handoffs_FromAgentId",
                table: "Handoffs",
                column: "FromAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Handoffs_Status",
                table: "Handoffs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMessages");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Handoffs");

            migrationBuilder.DropTable(
                name: "Agents");
        }
    }
}
