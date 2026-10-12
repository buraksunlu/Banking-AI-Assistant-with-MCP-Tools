using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerCredentials",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCredentials", x => x.CustomerId);
                    table.CheckConstraint("CK_CustomerCredentials_PasswordHash_NotBlank", "\"PasswordHash\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_CustomerCredentials_Timestamps", "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz\nAND \"UpdatedAt\" >= \"CreatedAt\" AND \"UpdatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz");
                    table.CheckConstraint("CK_CustomerCredentials_Username", "\"Username\" ~ '^[a-z0-9][a-z0-9._-]{2,49}$'");
                    table.ForeignKey(
                        name: "FK_CustomerCredentials_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCredentials_Username",
                table: "CustomerCredentials",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerCredentials");
        }
    }
}
