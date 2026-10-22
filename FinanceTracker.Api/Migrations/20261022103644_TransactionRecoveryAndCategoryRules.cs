using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class TransactionRecoveryAndCategoryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategorizationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorizationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategorizationRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationRules_UserId_Pattern",
                table: "CategorizationRules",
                columns: new[] { "UserId", "Pattern" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategorizationRules");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Transactions");
        }
    }
}
