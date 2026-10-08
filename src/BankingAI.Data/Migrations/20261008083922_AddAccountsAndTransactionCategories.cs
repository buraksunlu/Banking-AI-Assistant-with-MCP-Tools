using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndTransactionCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_AccountName_NotBlank", "\"AccountName\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_Accounts_AccountNumber_NotBlank", "\"AccountNumber\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_Accounts_Balance_NotNaN", "\"Balance\" <> 'NaN'::numeric");
                    table.CheckConstraint("CK_Accounts_CurrencyCode", "\"CurrencyCode\" IN ('TRY', 'USD', 'EUR', 'GBP')");
                    table.CheckConstraint("CK_Accounts_CustomerId_NotEmpty", "\"CustomerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Accounts_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_Accounts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionCategories",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCategories", x => x.Id);
                    table.CheckConstraint("CK_TransactionCategories_Code_NotBlank", "\"Code\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_TransactionCategories_DisplayName_NotBlank", "\"DisplayName\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_TransactionCategories_Id_Positive", "\"Id\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountNumber",
                table: "Accounts",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CustomerId",
                table: "Accounts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCategories_Code",
                table: "TransactionCategories",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "TransactionCategories");
        }
    }
}
