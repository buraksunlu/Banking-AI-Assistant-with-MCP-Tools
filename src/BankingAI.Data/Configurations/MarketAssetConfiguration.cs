using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class MarketAssetConfiguration : IEntityTypeConfiguration<MarketAsset>
{
    public void Configure(EntityTypeBuilder<MarketAsset> builder)
    {
        builder.ToTable("MarketAssets", table =>
        {
            table.HasCheckConstraint("CK_MarketAssets_Id_Positive", "\"Id\" > 0");
            table.HasCheckConstraint("CK_MarketAssets_Name_NotBlank", "\"Name\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_MarketAssets_Symbol_NotBlank", "\"Symbol\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_MarketAssets_BaseUnit_NotBlank", "\"BaseUnit\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_MarketAssets_AssetType", "\"AssetType\" IN ('Currency', 'Commodity')");
            table.HasCheckConstraint("CK_MarketAssets_QuoteCurrency", "\"QuoteCurrency\" = 'TRY'");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnType("smallint").ValueGeneratedNever();
        builder.Property(a => a.Symbol).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AssetType).HasMaxLength(30).IsRequired();
        builder.Property(a => a.BaseUnit).HasMaxLength(20).IsRequired();
        builder.Property(a => a.QuoteCurrency).HasColumnType("char(3)").IsRequired();
        builder.Property(a => a.IsActive).HasColumnType("boolean").IsRequired();
        builder.HasIndex(a => a.Symbol).IsUnique();
    }
}
