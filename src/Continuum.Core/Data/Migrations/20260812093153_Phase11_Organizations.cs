using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_ProjectKey",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_OwnerId_Name",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Channels_OwnerId_Name",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Agents_OwnerId_Name",
                table: "Agents");

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Workspaces",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Rooms",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Memories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Channels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrgId",
                table: "Agents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000002"));

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMemberships_Organizations_OrgId",
                        column: x => x.OrgId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrgMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OrgId_ProjectKey",
                table: "Workspaces",
                columns: new[] { "OrgId", "ProjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_OrgId",
                table: "Sessions",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_OrgId_Name",
                table: "Rooms",
                columns: new[] { "OrgId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_OwnerId",
                table: "Rooms",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_OrgId",
                table: "Memories",
                column: "OrgId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_OrgId_Name",
                table: "Channels",
                columns: new[] { "OrgId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_OwnerId",
                table: "Channels",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OrgId_Name",
                table: "Agents",
                columns: new[] { "OrgId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OwnerId",
                table: "Agents",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberships_OrgId_UserId",
                table: "OrgMemberships",
                columns: new[] { "OrgId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberships_UserId",
                table: "OrgMemberships",
                column: "UserId");

            // Every pre-tenancy row was backfilled to the default organization above; create it, and
            // enrol every existing account, so nobody logs in belonging to nothing. Both statements are
            // idempotent, and this also covers `dotnet ef database update` without the app running.
            migrationBuilder.Sql(@"
                INSERT INTO ""Organizations"" (""Id"", ""Name"", ""Slug"", ""CreatedAt"")
                VALUES ('00000000-0000-0000-0000-000000000002', 'Default', 'default', now())
                ON CONFLICT (""Id"") DO NOTHING;");

            migrationBuilder.Sql(@"
                INSERT INTO ""OrgMemberships"" (""Id"", ""OrgId"", ""UserId"", ""Role"", ""JoinedAt"")
                SELECT gen_random_uuid(),
                       '00000000-0000-0000-0000-000000000002',
                       u.""Id"",
                       CASE WHEN u.""Id"" = '00000000-0000-0000-0000-000000000001' THEN 2
                            WHEN u.""Role"" = 1 THEN 1
                            ELSE 0 END,
                       now()
                FROM ""Users"" u
                WHERE NOT EXISTS (SELECT 1 FROM ""OrgMemberships"" m WHERE m.""UserId"" = u.""Id"");");

            // The default existed only to backfill. Keeping it would let a service that forgets to set
            // OrgId write silently into the default tenant — exactly the bug tenancy must not have.
            foreach (var table in new[] { "Workspaces", "Sessions", "Rooms", "Memories", "Channels", "Agents" })
                migrationBuilder.Sql($@"ALTER TABLE ""{table}"" ALTER COLUMN ""OrgId"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgMemberships");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OrgId_ProjectKey",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_OrgId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_OrgId_Name",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_OwnerId",
                table: "Rooms");

            migrationBuilder.DropIndex(
                name: "IX_Memories_OrgId",
                table: "Memories");

            migrationBuilder.DropIndex(
                name: "IX_Channels_OrgId_Name",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_OwnerId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Agents_OrgId_Name",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_OwnerId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Memories");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "OrgId",
                table: "Agents");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_ProjectKey",
                table: "Workspaces",
                column: "ProjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_OwnerId_Name",
                table: "Rooms",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_OwnerId_Name",
                table: "Channels",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OwnerId_Name",
                table: "Agents",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }
    }
}
