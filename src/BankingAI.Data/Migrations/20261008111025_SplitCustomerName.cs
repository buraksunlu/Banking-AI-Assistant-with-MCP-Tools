using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitCustomerName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("CK_Customers_FullName_NotBlank", "Customers");
            migrationBuilder.RenameColumn("FullName", "Customers", "FirstName");

            migrationBuilder.AddColumn<string>("LastName", "Customers", "character varying(100)", maxLength: 100, nullable: true);

            // Preserve the existing synthetic name before making both columns required.
            migrationBuilder.Sql("""
                UPDATE "Customers"
                SET "LastName" = CASE
                    WHEN position(' ' IN btrim("FirstName")) > 0
                    THEN substring(btrim("FirstName") from position(' ' IN btrim("FirstName")) + 1)
                    ELSE 'Unknown'
                END,
                "FirstName" = split_part(btrim("FirstName"), ' ', 1);
                """);

            migrationBuilder.AlterColumn<string>("LastName", "Customers", "character varying(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);

            migrationBuilder.AddCheckConstraint("CK_Customers_FirstName_NotBlank", "Customers", "char_length(btrim(\"FirstName\")) > 0");
            migrationBuilder.AddCheckConstraint("CK_Customers_LastName_NotBlank", "Customers", "char_length(btrim(\"LastName\")) > 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("CK_Customers_FirstName_NotBlank", "Customers");
            migrationBuilder.DropCheckConstraint("CK_Customers_LastName_NotBlank", "Customers");
            migrationBuilder.AlterColumn<string>("LastName", "Customers", "character varying(100)", maxLength: 100, nullable: true,
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
            migrationBuilder.Sql("UPDATE \"Customers\" SET \"FirstName\" = btrim(\"FirstName\" || ' ' || \"LastName\");");
            migrationBuilder.DropColumn("LastName", "Customers");
            migrationBuilder.RenameColumn("FirstName", "Customers", "FullName");
            migrationBuilder.AddCheckConstraint("CK_Customers_FullName_NotBlank", "Customers", "char_length(btrim(\"FullName\")) > 0");
        }
    }
}
