using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table =>
        {
            table.HasCheckConstraint("CK_Accounts_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Accounts_CustomerId_NotEmpty", "\"CustomerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Accounts_AccountName_NotBlank", "\"AccountName\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Accounts_AccountNumber_NotBlank", "\"AccountNumber\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Accounts_CurrencyCode", "\"CurrencyCode\" IN ('TRY', 'USD', 'EUR', 'GBP')");
            table.HasCheckConstraint("CK_Accounts_AccountType", "\"AccountType\" IN (1, 2)");
            table.HasCheckConstraint("CK_Accounts_Balance_NotNaN", "\"Balance\" <> 'NaN'::numeric");
            table.HasCheckConstraint("CK_Accounts_Balance_NonNegative", "\"Balance\" >= 0");
            table.HasCheckConstraint("CK_Accounts_CreatedAt_Finite", "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.AccountName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(a => a.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(a => a.AccountType).HasConversion<short>().IsRequired();
        builder.Property(a => a.Balance).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(a => a.AccountNumber).IsUnique();
        builder.HasIndex(a => a.CustomerId);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
