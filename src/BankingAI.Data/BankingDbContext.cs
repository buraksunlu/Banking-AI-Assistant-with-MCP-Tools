using BankingAI.Data.Entities;
using BankingAI.Data.Configurations;
using Microsoft.EntityFrameworkCore;
using BankingAI.Data.Validation;

namespace BankingAI.Data;

public sealed class BankingDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TransactionCategory> TransactionCategories => Set<TransactionCategory>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MarketAsset> MarketAssets => Set<MarketAsset>();
    public DbSet<DailyMarketPrice> DailyMarketPrices => Set<DailyMarketPrice>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateChanges()
    {
        var referenceUtc = DateTimeOffset.UtcNow;
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("CreatedAt") is not null)
            {
                var createdAt = entry.Property("CreatedAt");
                InputRules.Timestamp((DateTimeOffset)createdAt.CurrentValue!, referenceUtc, "CreatedAt");
                if (entry.Entity is Transaction { CategorizedAt: { } categoryTime })
                    InputRules.Require(categoryTime >= (DateTimeOffset)createdAt.CurrentValue!, "CategorizedAt", "Kayıt zamanından önce olamaz.");
                if (entry.State == EntityState.Added)
                {
                    // Normal writes use one server reference, never a caller-provided creation time.
                    createdAt.CurrentValue = referenceUtc;
                }
                else
                    InputRules.Require(!createdAt.IsModified, "CreatedAt", "Kayıt zamanı sonradan değiştirilemez.");
            }

            if (entry.Entity is Transaction transaction && transaction.CategorizedAt is { } at)
            {
                if (entry.State == EntityState.Modified)
                    InputRules.Require(
                        !Equals(entry.Property("CategorizationSource").OriginalValue, Enums.CategorizationSource.Manual) ||
                        transaction.CategorizationSource == Enums.CategorizationSource.Manual,
                        "CategorizationSource", "Manuel kategori otomatik yöntemle ezilemez.");
                InputRules.Timestamp(at, referenceUtc, "CategorizedAt");
                var categoryChanged = entry.State == EntityState.Added ||
                    new[] { "CategoryId", "CategorizationSource", "CategoryConfidence", "CategorizedAt" }
                        .Any(name => entry.Property(name).IsModified);
                if (categoryChanged)
                {
                    if (entry.State != EntityState.Added)
                        InputRules.Require(at >= transaction.CreatedAt, "CategorizedAt", "Kayıt zamanından önce olamaz.");
                    transaction.CategorizedAt = referenceUtc;
                }
            }
            EntityValidation.Validate(entry.Entity, referenceUtc);
        }
    }

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
        modelBuilder.ApplyConfiguration(new DailyMarketPriceConfiguration());
        modelBuilder.HasSequence<long>("CustomerNumberSequence");
        var customer = modelBuilder.Entity<Customer>();
        customer.ToTable("Customers", table =>
        {
            table.HasCheckConstraint("CK_Customers_FirstName_NotBlank", "\"FirstName\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Customers_LastName_NotBlank", "\"LastName\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_Customers_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Customers_CreatedAt_Finite", "\"CreatedAt\" > '0001-01-01T00:00:00Z'::timestamptz AND \"CreatedAt\" < '9999-12-31T23:59:59.999999Z'::timestamptz");
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
