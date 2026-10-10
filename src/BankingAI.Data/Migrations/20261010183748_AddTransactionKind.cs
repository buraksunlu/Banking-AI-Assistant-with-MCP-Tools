using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Credit_Uncategorized",
                table: "Transactions");

            // Added as nullable first so existing rows can be classified explicitly, never defaulted.
            migrationBuilder.AddColumn<short>(
                name: "Kind",
                table: "Transactions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PairId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            // VAL-05: every Debit produced so far is a completed goods/service payment.
            migrationBuilder.Sql("""
                UPDATE "Transactions" SET "Kind" = 1 WHERE "Direction" = 1;
                UPDATE "Transactions" SET "Kind" = 5 WHERE "Direction" = 2 AND "RawDescription" LIKE '%açılış fonlaması%';
                UPDATE "Transactions" SET "Kind" = 4 WHERE "Direction" = 2 AND "RawDescription" LIKE '%maaş ödemesi%';
                """);

            // An unrecognized Credit must stop the migration instead of receiving a guessed kind.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unclassified integer;
                BEGIN
                    SELECT count(*) INTO unclassified FROM "Transactions" WHERE "Kind" IS NULL;
                    IF unclassified > 0 THEN
                        RAISE EXCEPTION 'AddTransactionKind: % işlemin türü belirlenemedi; migration durduruldu.', unclassified;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<short>(
                name: "Kind",
                table: "Transactions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PairId_Direction",
                table: "Transactions",
                columns: new[] { "PairId", "Direction" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Kind",
                table: "Transactions",
                sql: "\"Kind\" IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Kind_Category",
                table: "Transactions",
                sql: "\"Kind\" = 1 OR \"CategoryId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Kind_Direction",
                table: "Transactions",
                sql: "(\"Kind\" <> 1 OR \"Direction\" = 1) AND (\"Kind\" NOT IN (2, 4, 5) OR \"Direction\" = 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Kind_Pair",
                table: "Transactions",
                sql: "(\"Kind\" NOT IN (2, 3) OR \"PairId\" IS NOT NULL) AND (\"Kind\" NOT IN (4, 5) OR \"PairId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_PairId_NotEmpty",
                table: "Transactions",
                sql: "\"PairId\" IS NULL OR \"PairId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_PairId_Direction",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Kind",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Kind_Category",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Kind_Direction",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Kind_Pair",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_PairId_NotEmpty",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PairId",
                table: "Transactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Credit_Uncategorized",
                table: "Transactions",
                sql: "\"Direction\" <> 2 OR \"CategoryId\" IS NULL");
        }
    }
}
