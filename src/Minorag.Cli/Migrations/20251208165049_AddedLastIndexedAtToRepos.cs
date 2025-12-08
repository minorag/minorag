using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minorag.Cli.Migrations
{
    /// <inheritdoc />
    public partial class AddedLastIndexedAtToRepos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAt",
                table: "repositories",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastIndexedAt",
                table: "repositories");
        }
    }
}
