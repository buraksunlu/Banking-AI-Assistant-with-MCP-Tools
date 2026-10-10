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
            table.HasCheckConstraint("CK_Transactions_NormalizedMerchant_NotBlank", "\"NormalizedMerchant\" IS NULL OR \"NormalizedMerchant\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Transactions_Categorization", """
                ("CategoryId" IS NULL AND "CategorizationSource" IS NULL AND "CategoryConfidence" IS NULL AND "CategorizedAt" IS NULL)
                OR
                ("CategoryId" IS NOT NULL AND "CategoryId" > 0 AND "CategorizationSource" IS NOT NULL AND "CategorizedAt" IS NOT NULL
                 AND (("CategorizationSource" IN (1, 3) AND "CategoryConfidence" IS NULL)
                      OR ("CategorizationSource" = 2 AND "CategoryConfidence" IS NOT NULL AND "CategoryConfidence" BETWEEN 0 AND 1)))
                """);
            // KIND-01 rules are written as implications so each one rejects a single kind of mistake.
            table.HasCheckConstraint("CK_Transactions_Kind", "\"Kind\" IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_Transactions_Kind_Direction",
                "(\"Kind\" <> 1 OR \"Direction\" = 1) AND (\"Kind\" NOT IN (2, 4, 5) OR \"Direction\" = 2)");
            table.HasCheckConstraint("CK_Transactions_Kind_Pair",
                "(\"Kind\" NOT IN (2, 3) OR \"PairId\" IS NOT NULL) AND (\"Kind\" NOT IN (4, 5) OR \"PairId\" IS NULL)");
            table.HasCheckConstraint("CK_Transactions_Kind_Category", "\"Kind\" = 1 OR \"CategoryId\" IS NULL");
            table.HasCheckConstraint("CK_Transactions_PairId_NotEmpty",
                "\"PairId\" IS NULL OR \"PairId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Transactions_Timestamps", """
                "CreatedAt" > '0001-01-01T00:00:00Z'::timestamptz AND "CreatedAt" < '9999-12-31T23:59:59.999999Z'::timestamptz
                AND "OccurredAt" > '0001-01-01T00:00:00Z'::timestamptz AND "OccurredAt" <= "CreatedAt"
                AND ("CategorizedAt" IS NULL OR ("CategorizedAt" >= "CreatedAt" AND "CategorizedAt" < '9999-12-31T23:59:59.999999Z'::timestamptz))
                """);
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Direction).HasConversion<short>().IsRequired();
        builder.Property(t => t.Kind).HasConversion<short>().IsRequired();
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
        // One Debit and one Credit leg per pair; NULL pairs are distinct in PostgreSQL.
        builder.HasIndex(t => new { t.PairId, t.Direction }).IsUnique();
    }
}
