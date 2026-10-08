using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BankingAI.Data.Configurations;

public sealed class TransactionCategoryConfiguration : IEntityTypeConfiguration<TransactionCategory>
{
    public void Configure(EntityTypeBuilder<TransactionCategory> builder)
    {
        builder.ToTable("TransactionCategories", table =>
        {
            table.HasCheckConstraint("CK_TransactionCategories_Id_Positive", "\"Id\" > 0");
            table.HasCheckConstraint("CK_TransactionCategories_Code_NotBlank", "\"Code\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_TransactionCategories_DisplayName_NotBlank", "\"DisplayName\" ~ '[^[:space:]]'");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
    }
}
