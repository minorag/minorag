using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minorag.Cli.Migrations
{
    /// <inheritdoc />
    public partial class AddedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "file_id",
                table: "chunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    extension = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    symbol_name = table.Column<string>(type: "TEXT", nullable: true),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    repository_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_files_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                -- Insert one file per (repository_id, path)
                INSERT INTO files (path, extension, language, kind, symbol_name, content, file_hash, repository_id)
                SELECT
                c.path,
                MAX(c.extension) AS extension,
                MAX(c.language)  AS language,
                'file'           AS kind,
                NULLIF(MAX(c.symbol_name), '') AS symbol_name,
                (
                    SELECT group_concat(t.content, '')
                    FROM (
                    SELECT cc.content
                    FROM chunks cc
                    WHERE cc.repository_id = c.repository_id
                        AND cc.path = c.path
                    ORDER BY cc.chunk_index
                    ) AS t
                ) AS content,
                MAX(c.file_hash) AS file_hash,
                c.repository_id
                FROM chunks c
                LEFT JOIN files f
                ON f.repository_id = c.repository_id
                AND f.path = c.path
                WHERE f.id IS NULL
                GROUP BY c.repository_id, c.path;

                -- Set chunks.file_id
                UPDATE chunks
                SET file_id = (
                SELECT f.id
                FROM files f
                WHERE f.repository_id = chunks.repository_id
                    AND f.path = chunks.path
                )
                WHERE file_id = 0;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_chunks_file_id",
                table: "chunks",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "IX_files_repository_id",
                table: "files",
                column: "repository_id");

            migrationBuilder.AddForeignKey(
                name: "FK_chunks_files_file_id",
                table: "chunks",
                column: "file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chunks_files_file_id",
                table: "chunks");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropIndex(
                name: "IX_chunks_file_id",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "file_id",
                table: "chunks");
        }
    }
}
