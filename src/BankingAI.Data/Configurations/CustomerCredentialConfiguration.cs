using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class CustomerCredentialConfiguration : IEntityTypeConfiguration<CustomerCredential>
{
    public void Configure(EntityTypeBuilder<CustomerCredential> builder)
    {
        builder.ToTable("CustomerCredentials", table =>
        {
            table.HasCheckConstraint("CK_CustomerCredentials_Username", "\"Username\" ~ '^[a-z0-9][a-z0-9._-]{2,49}$'");
            table.HasCheckConstraint("CK_CustomerCredentials_PasswordHash_NotBlank", "\"PasswordHash\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_CustomerCredentials_Timestamps", """
                "CreatedAt" > '0001-01-01T00:00:00Z'::timestamptz AND "CreatedAt" < '9999-12-31T23:59:59.999999Z'::timestamptz
                AND "UpdatedAt" >= "CreatedAt" AND "UpdatedAt" < '9999-12-31T23:59:59.999999Z'::timestamptz
                """);
        });

        // One credential per customer: the customer id is also the primary key.
        builder.HasKey(c => c.CustomerId);
        builder.Property(c => c.CustomerId).ValueGeneratedNever();
        builder.Property(c => c.Username).HasMaxLength(50).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(c => c.Username).IsUnique();

        builder.HasOne<Customer>().WithOne().HasForeignKey<CustomerCredential>(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
