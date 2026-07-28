using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Continuum.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_SessionSummaryEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "SummaryEmbedding",
                table: "Sessions",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SummaryEmbedding",
                table: "Sessions",
                column: "SummaryEmbedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_SummaryEmbedding",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SummaryEmbedding",
                table: "Sessions");
        }
    }
}
