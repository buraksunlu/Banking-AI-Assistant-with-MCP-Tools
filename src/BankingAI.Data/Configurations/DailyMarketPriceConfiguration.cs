using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class DailyMarketPriceConfiguration : IEntityTypeConfiguration<DailyMarketPrice>
{
    public void Configure(EntityTypeBuilder<DailyMarketPrice> builder)
    {
        builder.ToTable("DailyMarketPrices", table =>
        {
            table.HasCheckConstraint("CK_DailyMarketPrices_Source_NotBlank", "\"Source\" IS NULL OR \"Source\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_DailyMarketPrices_PriceDate_Finite", "\"PriceDate\" > DATE '0001-01-01' AND \"PriceDate\" < DATE '9999-12-31'");
            table.HasCheckConstraint(
                "CK_DailyMarketPrices_Price_PositiveFinite",
                "\"Price\" > 0 AND \"Price\" NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnType("bigint").UseIdentityByDefaultColumn();
        builder.Property(p => p.MarketAssetId).HasColumnType("smallint").IsRequired();
        builder.Property(p => p.PriceDate).HasColumnType("date").IsRequired();
        builder.Property(p => p.Price).HasPrecision(20, 8).IsRequired();
        builder.Property(p => p.Source).HasMaxLength(100).IsRequired(false);

        builder.HasOne<MarketAsset>().WithMany()
            .HasForeignKey(p => p.MarketAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.MarketAssetId, p.PriceDate }).IsUnique();
    }
}
