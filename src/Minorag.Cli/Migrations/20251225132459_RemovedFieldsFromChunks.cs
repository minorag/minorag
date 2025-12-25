using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minorag.Cli.Migrations
{
    /// <inheritdoc />
    public partial class RemovedFieldsFromChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chunks_repositories_repository_id",
                table: "chunks");

            migrationBuilder.DropIndex(
                name: "IX_files_repository_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "idx_chunks_extension",
                table: "chunks");

            migrationBuilder.DropIndex(
                name: "idx_chunks_path",
                table: "chunks");

            migrationBuilder.DropIndex(
                name: "idx_chunks_repo_path",
                table: "chunks");

            migrationBuilder.DropIndex(
                name: "IX_chunks_file_id",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "extension",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "file_hash",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "language",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "path",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "repository_id",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "symbol_name",
                table: "chunks");

            migrationBuilder.CreateIndex(
                name: "idx_file_repo_path",
                table: "files",
                columns: ["repository_id", "path"]);

            migrationBuilder.CreateIndex(
                name: "idx_chunks_file_index",
                table: "chunks",
                columns: ["file_id", "chunk_index"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_file_repo_path",
                table: "files");

            migrationBuilder.DropIndex(
                name: "idx_chunks_file_index",
                table: "chunks");

            migrationBuilder.AddColumn<string>(
                name: "extension",
                table: "chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "file_hash",
                table: "chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "repository_id",
                table: "chunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "symbol_name",
                table: "chunks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_repository_id",
                table: "files",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "idx_chunks_extension",
                table: "chunks",
                column: "extension");

            migrationBuilder.CreateIndex(
                name: "idx_chunks_path",
                table: "chunks",
                column: "path");

            migrationBuilder.CreateIndex(
                name: "idx_chunks_repo_path",
                table: "chunks",
                columns: new[] { "repository_id", "path" });

            migrationBuilder.CreateIndex(
                name: "IX_chunks_file_id",
                table: "chunks",
                column: "file_id");

            migrationBuilder.AddForeignKey(
                name: "FK_chunks_repositories_repository_id",
                table: "chunks",
                column: "repository_id",
                principalTable: "repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
