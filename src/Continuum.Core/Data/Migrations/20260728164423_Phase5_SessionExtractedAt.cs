using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_SessionExtractedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractedAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedAt",
                table: "Sessions");
        }
    }
}
