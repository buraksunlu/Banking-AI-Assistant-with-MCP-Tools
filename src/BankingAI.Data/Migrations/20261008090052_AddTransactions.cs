using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Accounts_Id_CurrencyCode",
                table: "Accounts",
                columns: new[] { "Id", "CurrencyCode" });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<short>(type: "smallint", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<short>(type: "smallint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false),
                    RawDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedMerchant = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CategorizationSource = table.Column<short>(type: "smallint", nullable: true),
                    CategoryConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    CategorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0 AND \"Amount\" <> 'NaN'::numeric");
                    table.CheckConstraint("CK_Transactions_CategorizationSource", "\"CategorizationSource\" IS NULL OR \"CategorizationSource\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_Transactions_CategoryConfidence", "\"CategoryConfidence\" IS NULL OR (\"CategoryConfidence\" >= 0 AND \"CategoryConfidence\" <= 1)");
                    table.CheckConstraint("CK_Transactions_CurrencyCode", "\"CurrencyCode\" IN ('TRY', 'USD', 'EUR', 'GBP')");
                    table.CheckConstraint("CK_Transactions_Direction", "\"Direction\" IN (1, 2)");
                    table.CheckConstraint("CK_Transactions_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Transactions_RawDescription_NotBlank", "\"RawDescription\" ~ '[^[:space:]]'");
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId_CurrencyCode",
                        columns: x => new { x.AccountId, x.CurrencyCode },
                        principalTable: "Accounts",
                        principalColumns: new[] { "Id", "CurrencyCode" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_TransactionCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "TransactionCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_CurrencyCode",
                table: "Transactions",
                columns: new[] { "AccountId", "CurrencyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_OccurredAt",
                table: "Transactions",
                columns: new[] { "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId_OccurredAt",
                table: "Transactions",
                columns: new[] { "CategoryId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_NormalizedMerchant",
                table: "Transactions",
                column: "NormalizedMerchant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Accounts_Id_CurrencyCode",
                table: "Accounts");
        }
    }
}
