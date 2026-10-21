using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class StatementReviewAndCategoryBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyBudgets_UserId",
                table: "MonthlyBudgets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "Transactions",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<int>(
                name: "ImportRowId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReconfirmation",
                table: "MonthlyBudgets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "MonthlyBudgets",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Preserve all historical amounts. Abort if legacy duplicates require manual reconciliation.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM MonthlyBudgets GROUP BY UserId, Year, Month HAVING COUNT(*) > 1) THROW 51000, 'Duplicate monthly budgets exist. Reconcile them explicitly before migrating; no values have been discarded.', 1;");
            migrationBuilder.Sql("UPDATE MonthlyBudgets SET NeedsReconfirmation = 1");

            migrationBuilder.CreateTable(
                name: "CategoryLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MonthlyBudgetId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    LimitAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryLimits", x => x.Id);
                    table.CheckConstraint("CK_CategoryLimit_Amount", "[LimitAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_CategoryLimits_MonthlyBudgets_MonthlyBudgetId",
                        column: x => x.MonthlyBudgetId,
                        principalTable: "MonthlyBudgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedCount = table.Column<int>(type: "int", nullable: false),
                    NoticesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatementStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatementEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PossibleDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    Included = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRows_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportRowId",
                table: "Transactions",
                column: "ImportRowId",
                unique: true,
                filter: "[ImportRowId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Date",
                table: "Transactions",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyBudgets_UserId_Year_Month",
                table: "MonthlyBudgets",
                columns: new[] { "UserId", "Year", "Month" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Budget_Month",
                table: "MonthlyBudgets",
                sql: "[Month] BETWEEN 1 AND 12 AND [Year] BETWEEN 1900 AND 9998");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryLimits_MonthlyBudgetId_Category",
                table: "CategoryLimits",
                columns: new[] { "MonthlyBudgetId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UserId_ExpiresAt",
                table: "ImportBatches",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId",
                table: "ImportRows",
                column: "ImportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ImportRows_ImportRowId",
                table: "Transactions",
                column: "ImportRowId",
                principalTable: "ImportRows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ImportRows_ImportRowId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "CategoryLimits");

            migrationBuilder.DropTable(
                name: "ImportRows");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ImportRowId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Date",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyBudgets_UserId_Year_Month",
                table: "MonthlyBudgets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Budget_Month",
                table: "MonthlyBudgets");

            migrationBuilder.DropColumn(
                name: "ImportRowId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NeedsReconfirmation",
                table: "MonthlyBudgets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MonthlyBudgets");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "Transactions",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyBudgets_UserId",
                table: "MonthlyBudgets",
                column: "UserId");
        }
    }
}
