using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankingAI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketAssets",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BaseUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "char(3)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketAssets", x => x.Id);
                    table.CheckConstraint("CK_MarketAssets_AssetType", "\"AssetType\" IN ('Currency', 'Commodity')");
                    table.CheckConstraint("CK_MarketAssets_BaseUnit_NotBlank", "\"BaseUnit\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_MarketAssets_Id_Positive", "\"Id\" > 0");
                    table.CheckConstraint("CK_MarketAssets_Name_NotBlank", "\"Name\" ~ '[^[:space:]]'");
                    table.CheckConstraint("CK_MarketAssets_QuoteCurrency", "\"QuoteCurrency\" = 'TRY'");
                    table.CheckConstraint("CK_MarketAssets_Symbol_NotBlank", "\"Symbol\" ~ '[^[:space:]]'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketAssets_Symbol",
                table: "MarketAssets",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketAssets");
        }
    }
}
