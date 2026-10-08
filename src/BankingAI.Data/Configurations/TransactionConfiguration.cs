using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Transactions_Amount_Positive", "\"Amount\" > 0 AND \"Amount\" <> 'NaN'::numeric");
            table.HasCheckConstraint("CK_Transactions_Direction", "\"Direction\" IN (1, 2)");
            table.HasCheckConstraint("CK_Transactions_CurrencyCode", "\"CurrencyCode\" IN ('TRY', 'USD', 'EUR', 'GBP')");
            table.HasCheckConstraint("CK_Transactions_RawDescription_NotBlank", "\"RawDescription\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Transactions_CategorizationSource", "\"CategorizationSource\" IS NULL OR \"CategorizationSource\" IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Transactions_CategoryConfidence", "\"CategoryConfidence\" IS NULL OR (\"CategoryConfidence\" >= 0 AND \"CategoryConfidence\" <= 1)");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Direction).HasConversion<short>().IsRequired();
        builder.Property(t => t.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(t => t.RawDescription).HasMaxLength(500).IsRequired();
        builder.Property(t => t.NormalizedMerchant).HasMaxLength(150);
        builder.Property(t => t.CategorizationSource).HasConversion<short>();
        builder.Property(t => t.CategoryConfidence).HasPrecision(5, 4);
        builder.Property(t => t.CategorizedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // The composite FK also prevents a transaction currency differing from its account.
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(t => new { t.AccountId, t.CurrencyCode })
            .HasPrincipalKey(a => new { a.Id, a.CurrencyCode })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TransactionCategory>().WithMany().HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.AccountId, t.OccurredAt });
        builder.HasIndex(t => new { t.CategoryId, t.OccurredAt });
        builder.HasIndex(t => t.NormalizedMerchant);
    }
}
