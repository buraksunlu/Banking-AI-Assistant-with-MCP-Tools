using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A CHECK cannot query pg_timezone_names directly, so use a small function.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.is_valid_customer_time_zone(zone_name text)
                RETURNS boolean
                LANGUAGE sql STABLE STRICT
                AS $$
                    SELECT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_timezone_names WHERE name = zone_name
                    );
                $$;
                """);

            migrationBuilder.CreateSequence(
                name: "CustomerNumberSequence");

            migrationBuilder.AddColumn<string>(
                name: "CustomerNumber",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "'CUST-' || nextval('\"CustomerNumberSequence\"')::text");

            migrationBuilder.AddColumn<string>(
                name: "PreferredCurrency",
                table: "Customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "TRY");

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Europe/Istanbul");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerNumber",
                table: "Customers",
                column: "CustomerNumber",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CustomerNumber_Format",
                table: "Customers",
                sql: "\"CustomerNumber\" ~ '^CUST-[1-9][0-9]*$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_PreferredCurrency",
                table: "Customers",
                sql: "\"PreferredCurrency\" IN ('TRY', 'USD', 'EUR')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_TimeZone",
                table: "Customers",
                sql: "public.is_valid_customer_time_zone(\"TimeZone\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerNumber",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CustomerNumber_Format",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_PreferredCurrency",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_TimeZone",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PreferredCurrency",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Customers");

            migrationBuilder.DropSequence(
                name: "CustomerNumberSequence");

            migrationBuilder.Sql("DROP FUNCTION public.is_valid_customer_time_zone(text);");
        }
    }
}
