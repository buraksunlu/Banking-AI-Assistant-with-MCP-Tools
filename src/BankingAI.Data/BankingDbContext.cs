using BankingAI.Data.Entities;
using BankingAI.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data;

public sealed class BankingDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TransactionCategory> TransactionCategories => Set<TransactionCategory>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MarketAsset> MarketAssets => Set<MarketAsset>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connectionString = Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set BANKINGAI_CONNECTION_STRING before running a database command.");

        optionsBuilder.UseNpgsql(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new MarketAssetConfiguration());
        modelBuilder.HasSequence<long>("CustomerNumberSequence");
        var customer = modelBuilder.Entity<Customer>();
        customer.ToTable("Customers", table =>
        {
            table.HasCheckConstraint("CK_Customers_FirstName_NotBlank", "char_length(btrim(\"FirstName\")) > 0");
            table.HasCheckConstraint("CK_Customers_LastName_NotBlank", "char_length(btrim(\"LastName\")) > 0");
            table.HasCheckConstraint("CK_Customers_CustomerNumber_Format", "\"CustomerNumber\" ~ '^CUST-[1-9][0-9]*$'");
            table.HasCheckConstraint("CK_Customers_PreferredCurrency", "\"PreferredCurrency\" IN ('TRY', 'USD', 'EUR')");
            table.HasCheckConstraint("CK_Customers_TimeZone", "public.is_valid_customer_time_zone(\"TimeZone\")");
        });
        customer.HasKey(c => c.Id);
        customer.Property(c => c.Id).ValueGeneratedNever();
        customer.Property(c => c.CustomerNumber).HasMaxLength(32).IsRequired()
            .HasDefaultValueSql("'CUST-' || nextval('\"CustomerNumberSequence\"')::text");
        customer.HasIndex(c => c.CustomerNumber).IsUnique();
        customer.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
        customer.Property(c => c.LastName).HasMaxLength(100).IsRequired();
        customer.Property(c => c.PreferredCurrency).HasMaxLength(3).IsRequired().HasDefaultValue("TRY");
        customer.Property(c => c.TimeZone).HasMaxLength(100).IsRequired().HasDefaultValue("Europe/Istanbul");
        customer.Property(c => c.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
    }
}
