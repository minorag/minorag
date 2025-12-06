using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minorag.Cli.Migrations
{
    /// <inheritdoc />
    public partial class AddedClientAndProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chunks_repositories_RepositoryId",
                table: "chunks");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "chunks",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RepositoryId",
                table: "chunks",
                newName: "repository_id");

            migrationBuilder.AddColumn<int>(
                name: "project_id",
                table: "repositories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    client_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                    table.ForeignKey(
                        name: "FK_projects_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repositories_project_id",
                table: "repositories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_projects_client_id",
                table: "projects",
                column: "client_id");

            migrationBuilder.AddForeignKey(
                name: "FK_chunks_repositories_repository_id",
                table: "chunks",
                column: "repository_id",
                principalTable: "repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_repositories_projects_project_id",
                table: "repositories",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chunks_repositories_repository_id",
                table: "chunks");

            migrationBuilder.DropForeignKey(
                name: "FK_repositories_projects_project_id",
                table: "repositories");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropIndex(
                name: "IX_repositories_project_id",
                table: "repositories");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "repositories");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "chunks",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "repository_id",
                table: "chunks",
                newName: "RepositoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_chunks_repositories_RepositoryId",
                table: "chunks",
                column: "RepositoryId",
                principalTable: "repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
