using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceInputValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_FirstName_NotBlank",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_LastName_NotBlank",
                table: "Customers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Categorization",
                table: "Transactions",
                sql: "(\"CategoryId\" IS NULL AND \"CategorizationSource\" IS NULL AND \"CategoryConfidence\" IS NULL AND \"CategorizedAt\" IS NULL)\nOR\n(\"CategoryId\" IS NOT NULL AND \"CategoryId\" > 0 AND \"CategorizationSource\" IS NOT NULL AND \"CategorizedAt\" IS NOT NULL\n AND ((\"CategorizationSource\" IN (1, 3) AND \"CategoryConfidence\" IS NULL)\n      OR (\"CategorizationSource\" = 2 AND \"CategoryConfidence\" IS NOT NULL AND \"CategoryConfidence\" BETWEEN 0 AND 1)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Credit_Uncategorized",
                table: "Transactions",
                sql: "\"Direction\" <> 2 OR \"CategoryId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_NormalizedMerchant_NotBlank",
                table: "Transactions",
                sql: "\"NormalizedMerchant\" IS NULL OR \"NormalizedMerchant\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Timestamps",
                table: "Transactions",
                sql: "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz\nAND \"OccurredAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"OccurredAt\" <= \"CreatedAt\"\nAND (\"CategorizedAt\" IS NULL OR (\"CategorizedAt\" >= \"CreatedAt\" AND \"CategorizedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyMarketPrices_PriceDate_Finite",
                table: "DailyMarketPrices",
                sql: "\"PriceDate\" > DATE '0001-01-01' AND \"PriceDate\" < DATE '9999-12-31'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyMarketPrices_Source_NotBlank",
                table: "DailyMarketPrices",
                sql: "\"Source\" IS NULL OR \"Source\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CreatedAt_Finite",
                table: "Customers",
                sql: "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_FirstName_NotBlank",
                table: "Customers",
                sql: "\"FirstName\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Id_NotEmpty",
                table: "Customers",
                sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_LastName_NotBlank",
                table: "Customers",
                sql: "\"LastName\" ~ '[^[:space:]]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Balance_NonNegative",
                table: "Accounts",
                sql: "\"Balance\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_CreatedAt_Finite",
                table: "Accounts",
                sql: "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Categorization",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Credit_Uncategorized",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_NormalizedMerchant_NotBlank",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Timestamps",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyMarketPrices_PriceDate_Finite",
                table: "DailyMarketPrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyMarketPrices_Source_NotBlank",
                table: "DailyMarketPrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CreatedAt_Finite",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_FirstName_NotBlank",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Id_NotEmpty",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_LastName_NotBlank",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Balance_NonNegative",
                table: "Accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_CreatedAt_Finite",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_FirstName_NotBlank",
                table: "Customers",
                sql: "char_length(btrim(\"FirstName\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_LastName_NotBlank",
                table: "Customers",
                sql: "char_length(btrim(\"LastName\")) > 0");
        }
    }
}
