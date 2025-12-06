using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minorag.Cli.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create repositories table IF NOT EXISTS
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS repositories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    root_path TEXT NOT NULL,
                    name TEXT NOT NULL
                );
            ");

            // Unique index for root_path (ignore errors if already exists)
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_repositories_root_path
                ON repositories(root_path);
            ");

            // Create chunks table IF NOT EXISTS
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS chunks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL,
                    extension TEXT NOT NULL,
                    language TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    symbol_name TEXT,
                    content TEXT NOT NULL,
                    embedding BLOB NOT NULL,
                    file_hash TEXT NOT NULL,
                    chunk_index INTEGER NOT NULL,
                    RepositoryId INTEGER NOT NULL,
                    FOREIGN KEY (RepositoryId) REFERENCES repositories (Id) ON DELETE CASCADE
                );
            ");

            // Normal indexes but wrapped with IF NOT EXISTS
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_chunks_extension
                ON chunks(extension);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_chunks_path
                ON chunks(path);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_chunks_repo_path
                ON chunks(RepositoryId, path);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS chunks;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS repositories;");
        }
    }
}
