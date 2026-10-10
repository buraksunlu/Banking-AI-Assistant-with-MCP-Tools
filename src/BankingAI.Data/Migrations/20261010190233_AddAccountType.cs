using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added as nullable first so existing rows are classified explicitly, never defaulted to 0.
            migrationBuilder.AddColumn<short>(
                name: "AccountType",
                table: "Accounts",
                type: "smallint",
                nullable: true);

            // No business account existed before this migration: every current account is Personal.
            migrationBuilder.Sql("""UPDATE "Accounts" SET "AccountType" = 1;""");

            migrationBuilder.AlterColumn<short>(
                name: "AccountType",
                table: "Accounts",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_AccountType",
                table: "Accounts",
                sql: "\"AccountType\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_AccountType",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "Accounts");
        }
    }
}
